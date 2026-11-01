using System.Diagnostics;
using ID_Indetails.api.Data;

namespace ID_Indetails.api.Middlewares;

/// <summary>
/// Logs every API request's time and number of database commands, to find the slow ones. On where
/// <c>Diagnostics:LogRequestCost</c> is true (development).
/// </summary>
public sealed class RequestCostMiddleware(RequestDelegate next, ILogger<RequestCostMiddleware> logger)
{
    public const string SettingName = "Diagnostics:LogRequestCost";

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
                context.Request.QueryString,
                context.Response.StatusCode,
                watch.ElapsedMilliseconds,
                counter.Commands);
        }
    }
}
