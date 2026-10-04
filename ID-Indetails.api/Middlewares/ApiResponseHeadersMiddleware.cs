using Microsoft.Net.Http.Headers;

namespace ID_Indetails.api.Middlewares;

/// <summary>
/// Every answer is private to whoever asked for it (salaries, finances, messages): no browser or proxy cache keeps it,
/// and browsers never guess its type from its content. An endpoint that sets its own Cache-Control keeps it.
/// </summary>
public sealed class ApiResponseHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            if (!headers.ContainsKey(HeaderNames.CacheControl))
            {
                headers.CacheControl = "no-store";
            }

            headers.XContentTypeOptions = "nosniff";
            return Task.CompletedTask;
        });

        return next(context);
    }
}
