using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.TimeOff.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.TimeOff.Abstraction;

namespace ID_Indetails.api.Services.TimeOff.Implementation;

/// <summary>Run by the notification scheduler, which saves and sends what it stages. Each notification is keyed, so it goes once.</summary>
public sealed class TimeOffSchedule(
    ITimeOffRepository timeOff,
    ITimeOffWatch watch,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IClock clock) : ITimeOffSchedule
{
    /// <summary>The day-before reminder waits for the working morning.</summary>
    public const int RemindFromHour = 9;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.ToCompanyTime(clock.UtcNow);
        var today = DateOnly.FromDateTime(now);
        var tomorrow = today.AddDays(1);
        var superAdmins = await notifications.SuperAdminsAsync(cancellationToken);

        foreach (var request in await timeOff.PendingStartingByAsync(tomorrow, cancellationToken))
        {
            var name = PersonName.Of(request.User.FirstName, request.User.LastName, request.User.Email);
            if (request.FirstDay <= today)
            {
                request.Status = TimeOffStatus.Expired;
                request.DecidedAt = clock.UtcNow;
                activity.Add(TimeOffService.Event(request, ActivityEventKinds.TimeOffExpired, null, clock.UtcNow));
                await notifications.SendOnceAsync(
                    new NotificationDraft(NotificationKinds.TimeOffExpired, null, DomainCodes.Design, null, "time_off", request.Id,
                        new { firstDay = request.FirstDay, lastDay = request.LastDay }),
                    $"time-off-expired-{request.Id}",
                    [request.UserId],
                    cancellationToken);
            }
            else if (now.Hour >= RemindFromHour)
            {
                await notifications.SendOnceAsync(
                    new NotificationDraft(NotificationKinds.TimeOffUndecided, null, DomainCodes.Design, null, "time_off", request.Id,
                        new { name, firstDay = request.FirstDay, lastDay = request.LastDay }),
                    $"time-off-undecided-{request.Id}",
                    superAdmins,
                    cancellationToken);
            }
        }

        foreach (var request in await timeOff.UnderWayAsync(today, cancellationToken))
        {
            await notifications.SendOnceAsync(
                new NotificationDraft(NotificationKinds.TimeOffStarts, null, DomainCodes.Design, null, "user", request.UserId,
                    new
                    {
                        name = PersonName.Of(request.User.FirstName, request.User.LastName, request.User.Email),
                        userId = request.UserId,
                        lastDay = request.LastDay,
                    }),
                $"time-off-starts-{request.Id}",
                await watch.HeadsUpRecipientsAsync(request.UserId, request.User.TeamId, cancellationToken),
                cancellationToken);
        }
    }
}
