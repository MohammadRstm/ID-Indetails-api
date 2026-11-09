using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.TimeOff;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.TimeOff;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.TimeOff;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Repositories.TimeOff.Abstraction;
using ID_Indetails.api.Repositories.TimeOff.Implementation;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.TimeOff.Abstraction;

namespace ID_Indetails.api.Services.TimeOff.Implementation;

/// <summary>
/// Every decision locks the person, then the request (<c>SELECT … FOR UPDATE</c>), in that order: two super admins
/// deciding at once run one after the other, and the second is told it was already decided. The person's lock also keeps
/// a request, an approval and an excuse by hand from overlapping.
/// </summary>
public sealed class TimeOffService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IUserRepository users,
    ITimeOffRepository timeOff,
    ITimeOffWatch watch,
    IPayrollRepository payroll,
    IPayrollInputs payrollInputs,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : ITimeOffService
{
    private const string NotFoundMessage = "Request not found.";

    public async Task<IReadOnlyList<TimeOffResponse>> MineAsync(CancellationToken cancellationToken = default)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.TimeOffRequest);
        var rows = await timeOff.OfUserAsync(actor.UserId, cancellationToken);
        var calendar = (await payrollInputs.CalendarsAsync([actor.UserId], cancellationToken))[actor.UserId];
        return rows.Select(r => ToResponse(r, calendar, forSuperAdmins: false)).ToList();
    }

    public async Task<TimeOffResponse> AskAsync(AskTimeOffRequest request, CancellationToken cancellationToken = default)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.TimeOffRequest);
        long id;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var user = await users.GetByIdForUpdateAsync(actor.UserId, cancellationToken)
                ?? throw new NotFoundException(ErrorCodes.NotFound, "Employee not found.");
            if (user.RoleCode != RoleCodes.Employee || user.DomainCode != DomainCodes.Design)
            {
                throw new ForbiddenException(ErrorCodes.Forbidden, "Only Design's employees ask for time off.");
            }

            var today = clock.CompanyToday;
            var calendar = (await payrollInputs.CalendarsAsync([user.Id], cancellationToken))[user.Id];
            TimeOffRules.CheckRequest(
                    request.FirstDay,
                    request.LastDay,
                    today,
                    request.LastDay >= request.FirstDay && WorkingDays(calendar, request.FirstDay, request.LastDay) > 0,
                    await timeOff.HeldOnAsync(user.Id, request.FirstDay, request.LastDay, cancellationToken),
                    await ExcusedAsync(user.Id, request.FirstDay, request.LastDay, cancellationToken))
                .ThrowIfDenied();

            var now = clock.UtcNow;
            var asked = new TimeOffRequest
            {
                UserId = user.Id,
                FirstDay = request.FirstDay,
                LastDay = request.LastDay,
                Reason = request.Reason.Trim(),
                Status = TimeOffStatus.Pending,
                RequestedAt = now,
            };
            timeOff.Add(asked);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            activity.Add(Event(asked, ActivityEventKinds.TimeOffRequested, actor.UserId, now));
            await notifications.SendAsync(
                Draft(NotificationKinds.TimeOffRequested, actor.UserId, asked, PersonName.Of(user.FirstName, user.LastName, user.Email)),
                await notifications.SuperAdminsAsync(cancellationToken),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            id = asked.Id;
        }

        return await DescribeAsync(id, forSuperAdmins: false, cancellationToken);
    }

    public async Task<TimeOffResponse> WithdrawAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.TimeOffRequest);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var request = await timeOff.GetForUpdateAsync(id, cancellationToken);
            if (request is null || request.UserId != actor.UserId)
            {
                throw new NotFoundException(ErrorCodes.NotFound, NotFoundMessage);
            }

            TimeOffRules.CheckPending(request.Status).ThrowIfDenied();
            var now = clock.UtcNow;
            request.Status = TimeOffStatus.Withdrawn;
            request.DecidedAt = now;
            request.DecidedById = actor.UserId;
            activity.Add(Event(request, ActivityEventKinds.TimeOffWithdrawn, actor.UserId, now));
            await notifications.SendAsync(
                Draft(NotificationKinds.TimeOffWithdrawn, actor.UserId, request, currentUser.FullName),
                await notifications.SuperAdminsAsync(cancellationToken),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(id, forSuperAdmins: false, cancellationToken);
    }

    public async Task<PagedResponse<TimeOffResponse>> ListAsync(TimeOffListQuery query, CancellationToken cancellationToken = default)
    {
        await DeciderAsync(cancellationToken);
        var today = clock.CompanyToday;
        var (rows, total) = await timeOff.ListAsync(query.View, today, query.Page, query.PageSize, cancellationToken);
        var items = await DescribeAsync(rows, today, cancellationToken);
        return new PagedResponse<TimeOffResponse>(items, query.Page, query.PageSize, total);
    }

    public async Task<TimeOffResponse> ApproveAsync(long id, ApproveTimeOffRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await DeciderAsync(cancellationToken);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var asked = await LockAsync(id, cancellationToken);
            TimeOffRules.CheckApprove(
                    asked.Status,
                    asked.FirstDay,
                    clock.CompanyToday,
                    asked.Status == TimeOffStatus.Pending && await ExcusedAsync(asked.UserId, asked.FirstDay, asked.LastDay, cancellationToken))
                .ThrowIfDenied();

            var now = clock.UtcNow;
            asked.Status = TimeOffStatus.Approved;
            asked.IsPaid = request.IsPaid;
            asked.DecidedAt = now;
            asked.DecidedById = actor.UserId;
            asked.DecisionNote = Clean(request.Note);
            activity.Add(Event(asked, ActivityEventKinds.TimeOffApproved, actor.UserId, now));
            await notifications.SendAsync(
                new NotificationDraft(NotificationKinds.TimeOffApproved, actor.UserId, DomainCodes.Design, null, "time_off", asked.Id,
                    new { firstDay = asked.FirstDay, lastDay = asked.LastDay, isPaid = asked.IsPaid }),
                [asked.UserId],
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(id, forSuperAdmins: true, cancellationToken);
    }

    public async Task<TimeOffResponse> DeclineAsync(long id, TimeOffNoteRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await DeciderAsync(cancellationToken);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var asked = await LockAsync(id, cancellationToken);
            TimeOffRules.CheckPending(asked.Status).ThrowIfDenied();

            var now = clock.UtcNow;
            asked.Status = TimeOffStatus.Declined;
            asked.DecidedAt = now;
            asked.DecidedById = actor.UserId;
            asked.DecisionNote = Clean(request.Note);
            activity.Add(Event(asked, ActivityEventKinds.TimeOffDeclined, actor.UserId, now));
            await notifications.SendAsync(
                new NotificationDraft(NotificationKinds.TimeOffDeclined, actor.UserId, DomainCodes.Design, null, "time_off", asked.Id,
                    new { firstDay = asked.FirstDay, lastDay = asked.LastDay }),
                [asked.UserId],
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(id, forSuperAdmins: true, cancellationToken);
    }

    public async Task<TimeOffResponse> RejectAsync(long id, TimeOffNoteRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await DeciderAsync(cancellationToken);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var asked = await LockAsync(id, cancellationToken);
            var today = clock.CompanyToday;
            TimeOffRules.CheckReject(asked.Status, asked.LastDay, today).ThrowIfDenied();

            var now = clock.UtcNow;
            asked.Status = TimeOffStatus.Rejected;
            asked.EndedOn = today;
            asked.EndedAt = now;
            asked.EndedById = actor.UserId;
            asked.EndNote = Clean(request.Note);
            activity.Add(Event(asked, ActivityEventKinds.TimeOffRejected, actor.UserId, now));
            await notifications.SendAsync(
                new NotificationDraft(NotificationKinds.TimeOffRejected, actor.UserId, DomainCodes.Design, null, "time_off", asked.Id,
                    new { firstDay = asked.FirstDay, lastDay = asked.LastDay, endedOn = today }),
                [asked.UserId],
                cancellationToken);

            // under way: whoever was told they're off hears they're back
            if (asked.FirstDay <= today && await users.GetByIdAsync(asked.UserId, cancellationToken) is { } person)
            {
                await notifications.SendAsync(
                    new NotificationDraft(NotificationKinds.TimeOffBack, actor.UserId, DomainCodes.Design, null, "user", person.Id,
                        new { name = PersonName.Of(person.FirstName, person.LastName, person.Email), userId = person.Id }),
                    await watch.HeadsUpRecipientsAsync(person.Id, person.TeamId, cancellationToken),
                    cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(id, forSuperAdmins: true, cancellationToken);
    }

    public async Task<IReadOnlyList<AwayResponse>> AwayAsync(AwayQuery query, CancellationToken cancellationToken = default)
    {
        var from = query.From ?? clock.CompanyToday;
        var to = query.To ?? from;
        return (await timeOff.GrantedAsync(null, from, to, cancellationToken))
            .Select(x => new AwayResponse(x.UserId, x.Period.FirstDay, x.Period.LastTaken))
            .ToList();
    }

    /// <summary>The activity row every step writes; only those who decide time off see it.</summary>
    internal static ActivityEvent Event(TimeOffRequest request, string kind, long? actorId, DateTime at) => new()
    {
        DomainCode = DomainCodes.Design,
        Kind = kind,
        OccurredAt = at,
        ActorId = actorId,
        SubjectType = "user",
        SubjectId = request.UserId,
        Payload = JsonSerializer.Serialize(new
        {
            requestId = request.Id,
            firstDay = request.FirstDay,
            lastDay = request.LastDay,
            status = request.Status.ToString(),
            isPaid = request.IsPaid,
            endedOn = request.EndedOn,
        }),
    };

    internal static int WorkingDays(PersonCalendar calendar, DateOnly from, DateOnly to)
        => from > to ? 0 : TimeOffRules.DaysBetween(from, to).Count(d => calendar.HoursOn(d) is not null);

    private static NotificationDraft Draft(string kind, long? actorId, TimeOffRequest request, string name)
        => new(kind, actorId, DomainCodes.Design, null, "time_off", request.Id, new { name, firstDay = request.FirstDay, lastDay = request.LastDay });

    private async Task<UserAccess> DeciderAsync(CancellationToken cancellationToken)
        => (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.TimeOffDecide);

    /// <summary>The person, then the request: the order every change to someone's time off takes.</summary>
    private async Task<TimeOffRequest> LockAsync(long id, CancellationToken cancellationToken)
    {
        var row = await timeOff.GetRowAsync(id, cancellationToken) ?? throw new NotFoundException(ErrorCodes.NotFound, NotFoundMessage);
        await users.GetByIdForUpdateAsync(row.Request.UserId, cancellationToken);
        return await timeOff.GetForUpdateAsync(id, cancellationToken) ?? throw new NotFoundException(ErrorCodes.NotFound, NotFoundMessage);
    }

    /// <summary>Whether any of these days is excused by hand: a day excuse, or a month excused whole.</summary>
    private async Task<bool> ExcusedAsync(long userId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var months = new List<DateOnly>();
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
        {
            months.Add(month);
        }

        return (await payroll.ExcusesAsync([userId], months, cancellationToken))
            .Any(e => e.Kind == ExcuseKind.WholeMonth || e.Days.Any(d => d.Day >= from && d.Day <= to));
    }

    private async Task<TimeOffResponse> DescribeAsync(long id, bool forSuperAdmins, CancellationToken cancellationToken)
    {
        var row = await timeOff.GetRowAsync(id, cancellationToken) ?? throw new NotFoundException(ErrorCodes.NotFound, NotFoundMessage);
        if (forSuperAdmins)
        {
            return (await DescribeAsync([row], clock.CompanyToday, cancellationToken))[0];
        }

        var calendar = (await payrollInputs.CalendarsAsync([row.Request.UserId], cancellationToken))[row.Request.UserId];
        return ToResponse(row, calendar, forSuperAdmins: false);
    }

    /// <summary>What super admins see: also the days taken this year and the meetings during it.</summary>
    private async Task<IReadOnlyList<TimeOffResponse>> DescribeAsync(IReadOnlyList<TimeOffRow> rows, DateOnly today, CancellationToken cancellationToken)
    {
        var people = rows.Select(r => r.Request.UserId).Distinct().ToList();
        if (people.Count == 0)
        {
            return [];
        }

        var calendars = await payrollInputs.CalendarsAsync(people, cancellationToken);
        var yearStart = new DateOnly(today.Year, 1, 1);
        var yearEnd = new DateOnly(today.Year, 12, 31);
        var taken = (await timeOff.GrantedAsync(people, yearStart, yearEnd, cancellationToken))
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(x => WorkingDays(
                calendars[g.Key],
                x.Period.FirstDay < yearStart ? yearStart : x.Period.FirstDay,
                x.Period.LastTaken > yearEnd ? yearEnd : x.Period.LastTaken)));

        var items = new List<TimeOffResponse>();
        foreach (var row in rows)
        {
            var r = row.Request;
            IReadOnlyList<TimeOffMeetingResponse>? meetings = null;
            if (r.Status is TimeOffStatus.Pending or TimeOffStatus.Approved && r.LastDay >= today)
            {
                meetings = (await timeOff.MeetingsOfAsync(r.UserId, clock.CompanyDayStartUtc(r.FirstDay), clock.CompanyDayStartUtc(r.LastDay.AddDays(1)), cancellationToken))
                    .Select(m => new TimeOffMeetingResponse(m.Id, m.Topic, m.StartsAt, m.EndsAt))
                    .ToList();
            }

            items.Add(ToResponse(row, calendars[r.UserId], forSuperAdmins: true) with
            {
                TakenThisYear = taken.GetValueOrDefault(r.UserId),
                Meetings = meetings,
            });
        }

        return items;
    }

    private static TimeOffResponse ToResponse(TimeOffRow row, PersonCalendar calendar, bool forSuperAdmins)
    {
        var r = row.Request;
        var period = TimeOffRepository.PeriodOf(r);
        return new TimeOffResponse(
            r.Id,
            forSuperAdmins ? new TimeOffPersonResponse(r.UserId, row.PersonName, row.PhotoFileId, row.TeamName) : null,
            r.FirstDay,
            r.LastDay,
            r.Reason,
            r.Status,
            r.IsPaid,
            WorkingDays(calendar, r.FirstDay, r.LastDay),
            WorkingDays(calendar, r.FirstDay, period.LastTaken),
            r.RequestedAt,
            r.DecidedAt,
            forSuperAdmins ? row.DecidedByName : null,
            r.DecisionNote,
            r.EndedOn,
            r.EndedAt,
            forSuperAdmins ? row.EndedByName : null,
            r.EndNote);
    }

    private static string? Clean(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
