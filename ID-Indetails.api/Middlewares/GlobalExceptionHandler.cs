using Microsoft.AspNetCore.Diagnostics;

namespace ID_Indetails.api.Middlewares;

/// <summary>
/// The one place exceptions become HTTP responses, in the uniform error contract: which error each exception becomes,
/// and how it is logged, is <see cref="ApiErrorMapping"/>.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var error = ApiErrorMapping.Of(exception, logger, httpContext);
        await ApiErrorWriter.WriteAsync(httpContext, error.Type, error.Code, error.Message, error.Errors, cancellationToken);
        return true;
    }
}
