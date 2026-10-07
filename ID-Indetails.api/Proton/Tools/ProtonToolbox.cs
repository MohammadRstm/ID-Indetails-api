using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentValidation;
using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Proton.Safety;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Proton.Tools;

public enum ProtonToolStatus
{
    Ok,
    Invalid,
    NotFound,
    Forbidden,
    Refused,
    Timeout,
    Failed,
}

/// <summary>One tool call: what the model gets back, and what is logged and kept in the transcript.</summary>
public sealed record ProtonToolResult(
    string Tool,
    JsonElement Input,
    ProtonToolStatus Status,
    string Content,
    IReadOnlyList<ProtonRef> Refs,
    DayRange? Period,
    int ElapsedMs);

/// <summary>
/// The shared tool layer every Proton interface uses. It offers a caller only the tools whose endpoints they may call,
/// and runs each call read-only, inside the existing services and their authorization, with a time limit.
/// </summary>
public sealed class ProtonToolbox(
    IEnumerable<IProtonTool> tools,
    IAuthorizationService authorization,
    IPermissionService permissions,
    ICompanySettingsService companySettings,
    IClock clock,
    IServiceProvider services,
    IOptions<ProtonOptions> options,
    ILoggerFactory loggers)
{
    public const string LogCategory = "ID_Indetails.api.Proton";

    private const int MaxContentLength = 40_000;

    private readonly ILogger _log = loggers.CreateLogger(LogCategory);

    public async Task<ProtonToolSession> OpenAsync(ClaimsPrincipal caller, long userId, CancellationToken cancellationToken)
    {
        var settings = await companySettings.GetAsync(cancellationToken);
        var context = new ProtonToolContext(
            caller,
            authorization,
            services,
            clock.CompanyToday,
            settings.WeekStart == 0 ? DayOfWeek.Sunday : DayOfWeek.Monday);

        var access = await permissions.GetAccessAsync(userId, cancellationToken);
        var available = new List<IProtonTool>();
        foreach (var tool in tools)
        {
            if ((tool.OnlyInDomain is null || access.CanSee(tool.OnlyInDomain)) && await context.CanCallAsync(tool.Endpoint))
            {
                available.Add(tool);
            }
        }

        return new ProtonToolSession(this, context, available);
    }

    internal async Task<ProtonToolResult> RunAsync(
        ProtonToolContext context, IReadOnlyList<IProtonTool> available, string name, JsonElement input, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var tool = available.FirstOrDefault(t => t.Name == name);
        ProtonToolResult result;
        if (tool is null || !await context.CanCallAsync(tool.Endpoint))
        {
            result = Fail(name, input, ProtonToolStatus.Forbidden, "This tool isn't available to this user.", watch);
        }
        else
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ToolTimeoutSeconds));
            try
            {
                var output = await ProtonReadOnly.RunAsync(() => tool.RunAsync(input, context, timeout.Token));
                var content = JsonSerializer.Serialize(new { status = "ok", data = output.Data }, ProtonJson.Options);
                result = content.Length > MaxContentLength
                    ? Fail(name, input, ProtonToolStatus.Invalid, "The result is too large. Ask for fewer rows or a shorter period.", watch)
                    : new ProtonToolResult(name, input, ProtonToolStatus.Ok, content, output.Refs, output.Period, Elapsed(watch));
            }
            catch (ProtonToolInputException exception)
            {
                result = Fail(name, input, ProtonToolStatus.Invalid, exception.Message, watch);
            }
            catch (ValidationException exception)
            {
                result = Fail(name, input, ProtonToolStatus.Invalid, string.Join(" ", exception.Errors.Select(e => e.ErrorMessage).Distinct()), watch);
            }
            catch (NotFoundException)
            {
                // Missing and not visible read the same, as in the API: nothing about other domains leaks.
                result = Fail(name, input, ProtonToolStatus.NotFound, "Not found, or not visible to this user.", watch);
            }
            catch (ForbiddenException exception)
            {
                result = Fail(name, input, ProtonToolStatus.Forbidden, exception.Message, watch);
            }
            catch (AppException exception)
            {
                result = Fail(name, input, ProtonToolStatus.Refused, exception.Message, watch);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                result = Fail(name, input, ProtonToolStatus.Timeout, "This took too long. Try a narrower question.", watch);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _log.LogError(exception, "Proton tool {Tool} failed.", name);
                result = Fail(name, input, ProtonToolStatus.Failed, "The data couldn't be read right now.", watch);
            }
        }

        var level = result.Status is ProtonToolStatus.Forbidden ? LogLevel.Warning : LogLevel.Information;
        _log.Log(level, "Proton tool {Tool} {Status} in {ElapsedMs} ms with {Input}.",
            name, result.Status, result.ElapsedMs, ProtonInputSummary.Of(input));
        return result;
    }

    private static ProtonToolResult Fail(string tool, JsonElement input, ProtonToolStatus status, string message, Stopwatch watch)
        => new(tool, input, status,
            JsonSerializer.Serialize(new { status, message }, ProtonJson.Options),
            [], null, Elapsed(watch));

    private static int Elapsed(Stopwatch watch) => (int)watch.ElapsedMilliseconds;
}

/// <summary>The tools one caller may use during one question.</summary>
public sealed class ProtonToolSession(ProtonToolbox toolbox, ProtonToolContext context, IReadOnlyList<IProtonTool> tools)
{
    public IReadOnlyList<IProtonTool> Tools => tools;

    public Task<ProtonToolResult> RunAsync(string name, JsonElement input, CancellationToken cancellationToken)
        => toolbox.RunAsync(context, tools, name, input, cancellationToken);
}

/// <summary>Tool inputs for logs: ids, numbers, dates and enum values as they are; other text only as its length.</summary>
public static class ProtonInputSummary
{
    public static string Of(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            return "{}";
        }

        var summary = new JsonObject();
        foreach (var property in input.EnumerateObject())
        {
            summary[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String when IsCode(property.Value.GetString()!) => property.Value.GetString(),
                JsonValueKind.String => $"text({property.Value.GetString()!.Length})",
                JsonValueKind.Array => $"list({property.Value.GetArrayLength()})",
                JsonValueKind.Object => "object",
                _ => JsonNode.Parse(property.Value.GetRawText()),
            };
        }

        return summary.ToJsonString();
    }

    private static bool IsCode(string value)
        => value.Length <= 32 && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '-');
}
