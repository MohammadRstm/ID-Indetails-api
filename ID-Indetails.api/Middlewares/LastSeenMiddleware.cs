using ID_Indetails.api.Constants;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;
using ID_Indetails.api.Services.TimeOff.Abstraction;

namespace ID_Indetails.api.Middlewares;

/// <summary>
/// Records when a signed-in person was last seen, at most every few minutes each (<see cref="IPresence"/>), and tells the
/// super admins when someone on time off is active (<see cref="ITimeOffWatch.SeenAsync"/>). A failed write never fails
/// the request.
/// </summary>
public sealed class LastSeenMiddleware(RequestDelegate next, ILogger<LastSeenMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IPresence presence, IUserRepository users, ITimeOffWatch timeOff, IClock clock)
    {
        if (long.TryParse(context.User.FindFirst(AuthClaimTypes.UserId)?.Value, out var userId)
            && presence.ShouldRecordSeen(userId, clock.UtcNow))
        {
            try
            {
                await users.SetLastSeenAsync(userId, clock.UtcNow, context.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Couldn't record when user {UserId} was last seen.", userId);
            }

            await timeOff.SeenAsync(userId, context.RequestAborted);
        }

        await next(context);
    }
}
