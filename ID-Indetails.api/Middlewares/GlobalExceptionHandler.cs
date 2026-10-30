using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Services.Projects.Abstraction;
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
        if (exception is ProfitFloorException floor)
        {
            // the refused change's transaction has rolled back: keep it for forcing, and alert whoever sees the money
            await httpContext.RequestServices.GetRequiredService<IBlockedChangeRecorder>().RecordAsync(floor, cancellationToken);
        }

        var error = ApiErrorMapping.Of(exception, logger, httpContext);
        await ApiErrorWriter.WriteAsync(httpContext, error.Type, error.Code, error.Message, error.Errors, cancellationToken,
            (exception as ProfitFloorException)?.Id);
        return true;
    }
}
