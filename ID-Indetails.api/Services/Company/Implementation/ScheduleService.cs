using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;

namespace ID_Indetails.api.Services.Company.Implementation;

/// <summary>
/// Personal working weeks. Someone without one (or whose latest change is "follow the default")
/// works the company week. The break always comes from the company policy.
/// </summary>
public sealed class ScheduleService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IUserRepository users,
    IScheduleRepository schedules,
    IWorkingTimeService workingTime,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IScheduleService
{
    public async Task<SchedulesResponse> ListAsync(ScheduleListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var people = await schedules.PeopleAsync(
            actor.VisibleDomains, EmployeeRules.CanSee(actor, null), query.Domain, query.Search?.Trim(), cancellationToken);
        var byUser = (await schedules.SchedulesAsync(people.Select(p => p.Id).ToList(), cancellationToken)).ToLookup(s => s.UserId);
        var policy = await CompanyWeekAsync(cancellationToken);

        return new SchedulesResponse(
            WorkingTimeService.Describe(policy),
            people.Select(p => Describe(p, byUser[p.Id].ToList(), policy)).ToList());
    }

    public async Task<PersonScheduleResponse> SetAsync(long userId, SetScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var today = clock.CompanyToday;
        var effectiveFrom = request.EffectiveFrom ?? today;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null || !EmployeeRules.CanSee(actor, user.DomainCode))
        {
            throw new NotFoundException("Employee not found.");
        }

        if (user.Status == UserStatus.Deactivated)
        {
            throw new BusinessRuleException(ErrorCodes.UserDeactivated, "This account is deactivated.");
        }

        // A schedule sets the hours someone owes, which decides their pay; nobody sets their own pay.
        if (user.Id == actor.UserId)
        {
            throw new ForbiddenException(ErrorCodes.EmployeeChangeNotAllowed, "You cannot set your own schedule.");
        }

        EmployeeRules.CheckCanChange(actor, new EmployeeRules.Target(user.Id, user.RoleCode, user.DomainCode, user.Status)).ThrowIfDenied();

        var schedule = await schedules.GetForUpdateAsync(user.Id, effectiveFrom, cancellationToken);
        WorkWeek.CheckEffectiveFrom(effectiveFrom, today, schedule is not null).ThrowIfDenied();

        if (schedule is null)
        {
            schedule = new UserSchedule { UserId = user.Id, EffectiveFrom = effectiveFrom, CreatedAt = clock.UtcNow, CreatedById = actor.UserId };
            schedules.Add(schedule);
        }
        else
        {
            // Replacing a change that hasn't started yet.
            schedules.RemoveDays(schedule.Days.ToList());
            schedule.Days.Clear();
        }

        schedule.FollowsDefault = request.FollowsDefault;
        foreach (var day in request.FollowsDefault ? [] : request.Days)
        {
            schedule.Days.Add(new UserScheduleDay
            {
                Weekday = (byte)day.Weekday,
                StartMinute = (short)day.StartMinute,
                EndMinute = (short)day.EndMinute,
            });
        }

        activity.Add(new ActivityEvent
        {
            DomainCode = user.DomainCode,
            Kind = ActivityEventKinds.ScheduleChanged,
            OccurredAt = clock.UtcNow,
            ActorId = actor.UserId,
            SubjectType = "user",
            SubjectId = user.Id,
            Payload = JsonSerializer.Serialize(new { effectiveFrom, followsDefault = request.FollowsDefault }),
        });
        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.ScheduleChanged, actor.UserId, null, null, "user", user.Id,
                new { effectiveFrom, followsDefault = request.FollowsDefault }),
            [user.Id],
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var saved = await schedules.SchedulesAsync([user.Id], cancellationToken);
        return Describe(user, saved, await CompanyWeekAsync(cancellationToken));
    }

    public async Task<PersonScheduleResponse> DescribeAsync(User user, CancellationToken cancellationToken = default)
        => Describe(user, await schedules.SchedulesAsync([user.Id], cancellationToken), await CompanyWeekAsync(cancellationToken));

    private async Task<WorkingTimePolicy> CompanyWeekAsync(CancellationToken cancellationToken)
    {
        var policies = await workingTime.GetPoliciesAsync(cancellationToken);
        return policies.LastOrDefault(p => p.EffectiveFrom <= clock.CompanyToday) ?? policies[0];
    }

    private PersonScheduleResponse Describe(User user, IReadOnlyList<UserSchedule> history, WorkingTimePolicy policy)
    {
        var today = clock.CompanyToday;
        var ordered = history.OrderBy(s => s.EffectiveFrom).ToList();
        var current = ordered.LastOrDefault(s => s.EffectiveFrom <= today);
        var next = ordered.FirstOrDefault(s => s.EffectiveFrom > today);
        var own = current is { FollowsDefault: false } ? current : null;

        var days = own is not null
            ? own.Days.OrderBy(d => d.Weekday).Select(d => Day(d.Weekday, d.StartMinute, d.EndMinute, policy.BreakMinutes)).ToList()
            : WorkWeek.FromMask(policy.WorkDays).Select(d => Day(d, policy.WorkStartMinute, policy.WorkEndMinute, policy.BreakMinutes)).ToList();

        var name = $"{user.FirstName} {user.LastName}".Trim();
        return new PersonScheduleResponse(
            user.Id,
            name.Length > 0 ? name : user.Email,
            user.Position,
            user.RoleCode,
            user.DomainCode,
            user.Team is null ? null : new TeamSummaryResponse(user.Team.Id, user.Team.Name),
            own is not null,
            own?.EffectiveFrom,
            days,
            days.Sum(d => d.RequiredMinutes),
            next?.EffectiveFrom,
            user.PhotoFileId,
            current?.EffectiveFrom);
    }

    private static ScheduleDayResponse Day(int weekday, int start, int end, int breakMinutes)
        => new(weekday, start, end, WorkWeek.RequiredMinutes(start, end, breakMinutes));
}
