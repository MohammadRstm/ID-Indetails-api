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
        var scope = new RequestLogScope(context);
        using (logger.BeginScope(scope))
        {
            try
            {
                await next(context);
            }
            finally
            {
                scope.Complete();
            }
        }
    }
}

/// <summary>
/// The request's caller as log scope fields, read at the moment each line is written. Work the request started in the
/// background carries the scope on after it ends, so it then keeps who it was from and lets go of the request: the
/// server reuses that for the next one, which may be someone else's.
/// </summary>
public sealed class RequestLogScope(HttpContext context) : IReadOnlyList<KeyValuePair<string, object?>>
{
    private HttpContext? _context = context;
    private string? _userId;
    private string? _portalAccountId;

    /// <summary>The request is over: keep who it was from, and stop reading it.</summary>
    public void Complete()
    {
        Read();
        _context = null;
    }

    private void Read()
    {
        if (_context is { } request)
        {
            _userId = request.User.FindFirst(AuthClaimTypes.UserId)?.Value;
            _portalAccountId = request.User.FindFirst(AuthClaimTypes.PortalAccountId)?.Value;
        }
    }

    private KeyValuePair<string, object?>[] Fields
    {
        get
        {
            Read();
            return [new("UserId", _userId), new("PortalAccountId", _portalAccountId)];
        }
    }

    public int Count => 2;

    public KeyValuePair<string, object?> this[int index] => Fields[index];

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)Fields).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => string.Join(' ', Fields.Where(f => f.Value is not null).Select(f => $"{f.Key}:{f.Value}"));
}
