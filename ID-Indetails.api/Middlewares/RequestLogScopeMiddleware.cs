using System.Collections;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Middlewares;

/// <summary>
/// Puts who the request is from on every log line written while it runs: <c>UserId</c> for staff,
/// <c>PortalAccountId</c> for a client. The trace id is there already (<c>TraceId</c>, from the request's activity), and
/// is the one error responses carry, so a reference a user quotes finds their lines. First in the pipeline, so the
/// error handler's lines have it too; the ids are read when a line is written, so they appear once sign-in has run.
/// </summary>
public sealed class RequestLogScopeMiddleware(RequestDelegate next, ILogger<RequestLogScopeMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using (logger.BeginScope(new RequestLogScope(context)))
        {
            await next(context);
        }
    }
}

/// <summary>The request's caller as log scope fields, read at the moment each line is written.</summary>
public sealed class RequestLogScope(HttpContext context) : IReadOnlyList<KeyValuePair<string, object?>>
{
    private KeyValuePair<string, object?>[] Fields =>
    [
        new("UserId", context.User.FindFirst(AuthClaimTypes.UserId)?.Value),
        new("PortalAccountId", context.User.FindFirst(AuthClaimTypes.PortalAccountId)?.Value),
    ];

    public int Count => 2;

    public KeyValuePair<string, object?> this[int index] => Fields[index];

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)Fields).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => string.Join(' ', Fields.Where(f => f.Value is not null).Select(f => $"{f.Key}:{f.Value}"));
}
