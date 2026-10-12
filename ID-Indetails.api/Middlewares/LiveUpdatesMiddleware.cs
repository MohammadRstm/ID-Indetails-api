using ID_Indetails.api.Services.Push.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;

namespace ID_Indetails.api.Middlewares;

/// <summary>
/// Sends the live signals and queues the pushes a request staged once it has succeeded, after its changes are committed.
/// A failed request sends none. Sending never fails the request: the client still reloads on its next poll or action.
/// </summary>
public sealed class LiveUpdatesMiddleware(RequestDelegate next, ILogger<LiveUpdatesMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ILiveUpdates live, IPushQueue pushes)
    {
        await next(context);
        if (context.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            live.Discard();
            pushes.Discard();
            return;
        }

        try
        {
            await live.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't send live updates.");
        }

        try
        {
            await pushes.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't queue pushes.");
        }
    }
}
