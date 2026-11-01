using System.Diagnostics;
using ID_Indetails.api.Data;

namespace ID_Indetails.api.Middlewares;

/// <summary>
/// Logs a request's time and number of database commands: always when it failed on the server (5xx) or was slow
/// (<c>Diagnostics:SlowRequestMilliseconds</c>, a second by default), and every request where
/// <c>Diagnostics:LogRequestCost</c> is true (development). Live connections and streams stay open by design, so
/// they are logged only when they fail. The live connections' access token, which they pass in the query string, is
/// never written to the log.
/// </summary>
public sealed class RequestCostMiddleware(RequestDelegate next, ILogger<RequestCostMiddleware> logger, IConfiguration configuration)
{
    public const string SettingName = "Diagnostics:LogRequestCost";
    public const string SlowSettingName = "Diagnostics:SlowRequestMilliseconds";
    public const int DefaultSlowMilliseconds = 1000;
    private const string TokenParameter = "access_token";

    private readonly bool _all = configuration.GetValue<bool>(SettingName);
    private readonly int _slowMilliseconds = configuration.GetValue(SlowSettingName, DefaultSlowMilliseconds);

    public async Task InvokeAsync(HttpContext context)
    {
        var counter = QueryCount.Start();
        var watch = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            var status = context.Response.StatusCode;
            var streaming = context.Request.Path.StartsWithSegments("/hubs")
                || (context.Response.ContentType?.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase) ?? false);
            var level = LevelOf(_all, status, watch.ElapsedMilliseconds, _slowMilliseconds, streaming);
            if (level is { } logLevel)
            {
                logger.Log(
                    logLevel,
                    "{Method} {Path}{Query} {Status} in {Milliseconds} ms, {Queries} queries",
                    context.Request.Method,
                    context.Request.Path,
                    LoggedQuery(context.Request),
                    status,
                    watch.ElapsedMilliseconds,
                    counter.Commands);
            }
        }
    }

    /// <summary>
    /// Whether a request is logged, and how: a server failure as an error, a slow request as a warning, anything else
    /// only when every request is logged. Null when it isn't logged.
    /// </summary>
    public static LogLevel? LevelOf(bool all, int status, long milliseconds, int slowMilliseconds, bool streaming)
        => status >= StatusCodes.Status500InternalServerError ? LogLevel.Error
            : !streaming && milliseconds >= slowMilliseconds ? LogLevel.Warning
            : all ? LogLevel.Information
            : null;

    /// <summary>The request's query string as logged: an access token in it is hidden.</summary>
    public static string LoggedQuery(HttpRequest request)
        => request.Query.ContainsKey(TokenParameter)
            ? QueryString.Create(request.Query.Select(q => KeyValuePair.Create(q.Key, (string?)(q.Key == TokenParameter ? "hidden" : q.Value.ToString()))))
                .ToString()
            : request.QueryString.ToString();
}
