using System.Diagnostics;
using ID_Indetails.api.Data;

namespace ID_Indetails.api.Middlewares;

/// <summary>
/// Logs every API request's time and number of database commands, to find the slow ones. On where
/// <c>Diagnostics:LogRequestCost</c> is true (development). The live connections' access token, which they pass in
/// the query string, is never written to the log.
/// </summary>
public sealed class RequestCostMiddleware(RequestDelegate next, ILogger<RequestCostMiddleware> logger)
{
    public const string SettingName = "Diagnostics:LogRequestCost";
    private const string TokenParameter = "access_token";

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
            logger.LogInformation(
                "{Method} {Path}{Query} {Status} in {Milliseconds} ms, {Queries} queries",
                context.Request.Method,
                context.Request.Path,
                LoggedQuery(context.Request),
                context.Response.StatusCode,
                watch.ElapsedMilliseconds,
                counter.Commands);
        }
    }

    /// <summary>The request's query string as logged: an access token in it is hidden.</summary>
    public static string LoggedQuery(HttpRequest request)
        => request.Query.ContainsKey(TokenParameter)
            ? QueryString.Create(request.Query.Select(q => KeyValuePair.Create(q.Key, (string?)(q.Key == TokenParameter ? "hidden" : q.Value.ToString()))))
                .ToString()
            : request.QueryString.ToString();
}
