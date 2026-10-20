using System.Net;
using System.Text;
using System.Text.Json;
using Google;
using Google.Apis.AnalyticsData.v1beta;
using Google.Apis.AnalyticsData.v1beta.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Http;
using Google.Apis.Services;
using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Services.GoogleAnalytics.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.GoogleAnalytics.Implementation;

/// <summary>
/// Google's Analytics Data API (v1beta), as the service account of <see cref="GoogleAnalyticsOptions"/> with read-only
/// access: two batches of reports per period and four small realtime reports. Nothing is retried, because Google
/// counts server errors against an hourly quota: a failure is logged with Google's reason (never the key) and answered
/// as unavailable.
/// </summary>
public sealed class GoogleAnalyticsClient(IOptions<GoogleAnalyticsOptions> options, ILogger<GoogleAnalyticsClient> logger)
    : IGoogleAnalyticsClient
{
    /// <summary>Below this share of the hour's tokens left, the log warns.</summary>
    private const double LowQuota = 0.1;

    private readonly Lazy<AnalyticsDataService> _service = new(() => CreateService(options.Value));

    private string Property => $"properties/{options.Value.PropertyId}";

    public async Task<GoogleAnalyticsReport> ReportAsync(AnalyticsPeriod period, CancellationToken cancellationToken = default)
    {
        var answers = await CallAsync(
            () => Task.WhenAll(
                _service.Value.Properties.BatchRunReports(GoogleAnalyticsReports.Traffic(period), Property).ExecuteAsync(cancellationToken),
                _service.Value.Properties.BatchRunReports(GoogleAnalyticsReports.Audience(period), Property).ExecuteAsync(cancellationToken)),
            cancellationToken);
        WarnIfLow(answers[0].Reports?.FirstOrDefault()?.PropertyQuota);
        return GoogleAnalyticsReports.Read(period, answers[0], answers[1]);
    }

    public async Task<GoogleAnalyticsRealtime> RealtimeAsync(CancellationToken cancellationToken = default)
    {
        var answers = await CallAsync(
            () => Task.WhenAll(GoogleAnalyticsReports.Realtime()
                .Select(r => _service.Value.Properties.RunRealtimeReport(r, Property).ExecuteAsync(cancellationToken))),
            cancellationToken);
        return GoogleAnalyticsReports.ReadRealtime(answers);
    }

    /// <summary>The service account's JSON key from its base64 form, or null when it isn't a service account's key.</summary>
    public static string? KeyJson(string base64)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64.Trim()));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && Text(root, "type") == "service_account"
                && !string.IsNullOrWhiteSpace(Text(root, "client_email"))
                && !string.IsNullOrWhiteSpace(Text(root, "private_key"))
                ? json
                : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return null;
        }

        static string? Text(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static AnalyticsDataService CreateService(GoogleAnalyticsOptions settings)
    {
        var json = KeyJson(settings.ServiceAccountKeyBase64)
            ?? throw new InvalidOperationException("GoogleAnalytics:ServiceAccountKeyBase64 is not a service account's JSON key.");
        var credential = CredentialFactory.FromJson<ServiceAccountCredential>(json)
            .ToGoogleCredential()
            .CreateScoped(AnalyticsDataService.Scope.AnalyticsReadonly);
        return new AnalyticsDataService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "ID-InDetails",
            HttpClientTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
            DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None,
        });
    }

    private async Task<T> CallAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call();
        }
        catch (GoogleApiException exception) when (!cancellationToken.IsCancellationRequested)
        {
            var status = (int)exception.HttpStatusCode;
            var reason = exception.Error?.Errors?.FirstOrDefault()?.Reason;
            var message = exception.Error?.Message;
            switch (exception.HttpStatusCode)
            {
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    logger.LogError(
                        "Google Analytics refused the request ({Status}, {Reason}): check that the service account has Viewer access to property {PropertyId} and that the Google Analytics Data API is enabled. {Message}",
                        status, reason, options.Value.PropertyId, message);
                    break;
                case HttpStatusCode.TooManyRequests:
                    logger.LogWarning("Google Analytics is out of quota for property {PropertyId}: {Message}", options.Value.PropertyId, message);
                    break;
                case HttpStatusCode.BadRequest:
                    logger.LogError("Google Analytics rejected a report ({Reason}): {Message}", reason, message);
                    break;
                default:
                    logger.LogWarning("Google Analytics answered {Status} ({Reason}): {Message}", status, reason, message);
                    break;
            }

            throw Unavailable(exception);
        }
        catch (TokenResponseException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                "Google refused the service account's key ({Error}): it may have been deleted or disabled. Create a new key (docs/website.md, \"Analytics\").",
                exception.Error?.Error);
            throw Unavailable(exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Google Analytics couldn't be reached.");
            throw Unavailable(exception);
        }
    }

    private static ServiceUnavailableException Unavailable(Exception exception)
        => new(ErrorCodes.WebsiteAnalyticsUnavailable, WebsiteAnalyticsRules.UnavailableMessage, exception);

    private void WarnIfLow(PropertyQuota? quota)
    {
        if (quota?.TokensPerHour is { Remaining: { } remaining, Consumed: { } consumed } && remaining < (remaining + consumed) * LowQuota)
        {
            logger.LogWarning("Google Analytics has {Remaining} of its hourly tokens left for property {PropertyId}.", remaining, options.Value.PropertyId);
        }
    }
}
