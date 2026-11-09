using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.TimeOff;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.TimeOff.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Push.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;
using ID_Indetails.api.Services.TimeOff.Abstraction;

namespace ID_Indetails.api.Services.TimeOff.Implementation;

public sealed class TimeOffWatch(
    ICurrentUser currentUser,
    ITimeOffRepository timeOff,
    IUserRepository users,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IServiceScopeFactory scopes,
    IClock clock,
    ILogger<TimeOffWatch> logger) : ITimeOffWatch
{
    public Task<IReadOnlyList<(long UserId, TimeOffRules.Period Period)>> GrantedAsync(
        IReadOnlyCollection<long> userIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => userIds.Count == 0
            ? Task.FromResult<IReadOnlyList<(long, TimeOffRules.Period)>>([])
            : timeOff.GrantedAsync(userIds, from, to, cancellationToken);

    public async Task CheckHoursAsync(long userId, IReadOnlyCollection<DateOnly> entryDays, CancellationToken cancellationToken = default)
    {
        var today = clock.CompanyToday;
        var from = entryDays.Append(today).Min();
        var to = entryDays.Append(today).Max();
        var periods = (await timeOff.GrantedAsync([userId], from, to, cancellationToken)).Select(x => x.Period).ToList();
        if (periods.Count == 0)
        {
            return;
        }

        // forced or released hours are logged for them, not by them
        if (!currentUser.IsActing && TimeOffRules.OffOn(today, periods) is { } period)
        {
            await AlertAsync(NotificationKinds.TimeOffHours, userId, period, $"time-off-hours-{period.Id}-{clock.ToCompanyTime(clock.UtcNow):yyyyMMddHH}", cancellationToken);
            TimeOffRules.CheckNotOff(today, periods).ThrowIfDenied();
        }

        TimeOffRules.CheckEntryDays(entryDays, periods).ThrowIfDenied();
    }

    public async Task SeenAsync(long userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var today = clock.CompanyToday;
            var periods = (await timeOff.GrantedAsync([userId], today, today, cancellationToken)).Select(x => x.Period);
            if (TimeOffRules.OffOn(today, periods) is { } period)
            {
                await AlertAsync(NotificationKinds.TimeOffOnline, userId, period, $"time-off-online-{period.Id}-{today:yyyyMMdd}", cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't check whether user {UserId} is on time off.", userId);
        }
    }

    public async Task CheckMeetingAsync(IReadOnlyCollection<long> attendeeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var off = await GrantedAsync(attendeeIds, from, to, cancellationToken);
        if (off.Count == 0)
        {
            return;
        }

        var (userId, period) = off[0];
        var person = await users.GetByIdAsync(userId, cancellationToken);
        var name = person is null ? "Someone" : PersonName.Of(person.FirstName, person.LastName, person.Email);
        throw new BusinessRuleException(
            ErrorCodes.MeetingAttendeeOff,
            $"{name} is on time off until {period.LastTaken:d MMM yyyy}, so they can't be put in this meeting.");
    }

    public async Task EndAsync(User person, long actorId, string why, CancellationToken cancellationToken = default)
    {
        var today = clock.CompanyToday;
        var open = await timeOff.OpenFromAsync(person.Id, today, cancellationToken);
        if (open.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var request in open)
        {
            if (request.Status == TimeOffStatus.Pending)
            {
                request.Status = TimeOffStatus.Cancelled;
                request.DecidedAt = now;
                request.DecidedById = actorId;
                request.DecisionNote = why;
            }
            else
            {
                request.Status = TimeOffStatus.Ended;
                request.EndedOn = today;
                request.EndedAt = now;
                request.EndedById = actorId;
                request.EndNote = why;
            }

            activity.Add(TimeOffService.Event(request, ActivityEventKinds.TimeOffEnded, actorId, now));
        }

        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.TimeOffEnded, actorId, DomainCodes.Design, null, "user", person.Id,
                new { name = PersonName.Of(person.FirstName, person.LastName, person.Email), userId = person.Id, why }),
            await notifications.SuperAdminsAsync(cancellationToken),
            cancellationToken);
    }

    public async Task<IReadOnlyList<long>> HeadsUpRecipientsAsync(long userId, long? teamId, CancellationToken cancellationToken = default)
    {
        var ids = (await notifications.SuperAdminsAsync(cancellationToken)).ToHashSet();
        if (teamId is { } team)
        {
            ids.UnionWith(await timeOff.ActiveTeamMembersAsync(team, cancellationToken));
        }

        ids.Remove(userId);
        return ids.ToList();
    }

    /// <summary>Saved and sent on its own, straight away: the request that set it off fails, or may yet.</summary>
    private async Task AlertAsync(string kind, long userId, TimeOffRules.Period period, string key, CancellationToken cancellationToken)
    {
        var person = await users.GetByIdAsync(userId, cancellationToken);
        if (person is null)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<INotificationSender>();
        await sender.SendOnceAsync(
            new NotificationDraft(kind, null, DomainCodes.Design, null, "user", userId,
                new { name = PersonName.Of(person.FirstName, person.LastName, person.Email), userId, lastDay = period.LastTaken }),
            key,
            await sender.SuperAdminsAsync(cancellationToken),
            cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<ILiveUpdates>().FlushAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IPushQueue>().FlushAsync(cancellationToken);
    }
}
