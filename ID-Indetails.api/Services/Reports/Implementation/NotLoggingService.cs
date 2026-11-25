using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Reports;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Reports;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Notifications.Abstraction;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.Reports.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Implementation;

namespace ID_Indetails.api.Services.Reports.Implementation;

/// <summary>
/// The people the dashboard's "not logging" item counts (<see cref="NotLoggingRules"/>), with their hours this week from
/// the same calculators as the hours ledger, and the reminders sent to them. The once-a-day rule is a notification sent
/// key per person and day (<c>hours-reminder-YYYY-MM-DD</c>), so it holds whoever sends the reminder.
/// </summary>
public sealed class NotLoggingService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IWorkEntryRepository entries,
    ICompanyRepository company,
    IPayrollRepository payroll,
    IPayrollInputs payrollInputs,
    IUserRepository users,
    INotificationRepository notifications,
    INotificationSender sender,
    IUnitOfWork unitOfWork,
    IClock clock) : INotLoggingService
{
    /// <summary>The week so far, everyone in Design, and who of them has logged nothing.</summary>
    private sealed record Roster(
        UserAccess Actor,
        DateOnly WeekStart,
        DateOnly Today,
        IReadOnlyList<DesignPerson> Everyone,
        IReadOnlyList<DesignPerson> NotLogging);

    public async Task<NotLoggingResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        var roster = await RosterAsync(cancellationToken);
        var ids = roster.NotLogging.Select(p => p.Id).ToList();
        var reminded = await notifications.RecipientsWithKeyAsync(KeyOf(roster.Today), ids, cancellationToken);
        // colleagues' hours are only for those who may see everyone's work
        var hours = roster.Actor.Has(PermissionCodes.WorkEntryViewAll) ? await HoursAsync(roster, cancellationToken) : null;

        return new NotLoggingResponse(
            roster.WeekStart,
            roster.Today,
            roster.NotLogging
                .Select(p =>
                {
                    var week = hours is null ? null : hours.GetValueOrDefault(p.Id) ?? new NotLoggingRules.WeekHours(0, 0, 0);
                    return new NotLoggingPersonResponse(
                        p.Id, p.Name, p.Email, p.PhotoFileId, week?.Logged, week?.Expected, week?.Overtime, reminded.Contains(p.Id));
                })
                .ToList());
    }

    public async Task<HoursRemindersResponse> RemindAsync(long userId, CancellationToken cancellationToken = default)
    {
        var roster = await RosterAsync(cancellationToken);
        // only someone the list could ever hold is a person to remind
        var person = roster.Everyone.FirstOrDefault(p => p.Id == userId && p.Status == UserStatus.Active && p.Role == RoleCodes.Employee)
            ?? throw new NotFoundException("Employee not found.");
        if (roster.NotLogging.All(p => p.Id != userId))
        {
            throw new ConflictException("logged_this_week", $"{person.Name} has already logged work this week.");
        }

        if ((await notifications.RecipientsWithKeyAsync(KeyOf(roster.Today), [userId], cancellationToken)).Count > 0)
        {
            throw new ConflictException("already_reminded", $"{person.Name} has already been reminded today.");
        }

        await SendAsync(roster, [userId], cancellationToken);
        return new HoursRemindersResponse(1, 0);
    }

    public async Task<HoursRemindersResponse> RemindAllAsync(CancellationToken cancellationToken = default)
    {
        var roster = await RosterAsync(cancellationToken);
        var ids = roster.NotLogging.Select(p => p.Id).ToList();
        var reminded = await notifications.RecipientsWithKeyAsync(KeyOf(roster.Today), ids, cancellationToken);
        var fresh = ids.Where(id => !reminded.Contains(id)).ToList();
        if (fresh.Count > 0)
        {
            await SendAsync(roster, fresh, cancellationToken);
        }

        return new HoursRemindersResponse(fresh.Count, ids.Count - fresh.Count);
    }

    /// <summary>The key that makes a reminder go to someone once a day, from anyone.</summary>
    private static string KeyOf(DateOnly today) => $"hours-reminder-{today:yyyy-MM-dd}";

    /// <summary>The caller's access once they may see the dashboard (report:view, and Design: otherwise not found), and today's list.</summary>
    private async Task<Roster> RosterAsync(CancellationToken cancellationToken)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.ReportView);
        if (!actor.CanSee(DomainCodes.Design))
        {
            throw new NotFoundException("Domain not found.");
        }

        var calendar = CompanyCalendar.Of(await company.GetSettingsAsync(cancellationToken: cancellationToken));
        var today = clock.CompanyToday;
        var weekStart = WorkStats.WeekStartOf(today, calendar.FirstDayOfWeek);

        // the dashboard's own reading: anyone with work from the start of the week to today is logging
        var rows = await entries.RowsAsync(new WorkEntrySearch(From: clock.CompanyDayStartUtc(weekStart)), cancellationToken);
        var logging = rows.Where(r => clock.ToCompanyDate(r.StartedAt) <= today).Select(r => r.UserId).ToHashSet();
        var everyone = await entries.DesignPeopleAsync(cancellationToken);

        return new Roster(actor, weekStart, today, everyone, NotLoggingRules.Of(everyone, logging));
    }

    /// <summary>Each listed person's hours this week, by the hours ledger's calculators, over the months the week touches.</summary>
    private async Task<IReadOnlyDictionary<long, NotLoggingRules.WeekHours>> HoursAsync(Roster roster, CancellationToken cancellationToken)
    {
        var ids = roster.NotLogging.Select(p => p.Id).ToHashSet();
        var months = new[] { WorkStats.MonthStartOf(roster.WeekStart), WorkStats.MonthStartOf(roster.Today) }.Distinct().ToList();
        var people = (await payroll.PeopleAsync([DomainCodes.Design], false, DomainCodes.Design, null, months[0], cancellationToken))
            .Where(u => ids.Contains(u.Id))
            .ToList();
        var inputs = (await payrollInputs.ForAsync(people, months, cancellationToken)).ToLookup(m => m.User.Id, m => m.Input);

        return people.ToDictionary(u => u.Id, u => NotLoggingRules.HoursOf(roster.WeekStart, roster.Today, inputs[u.Id]));
    }

    /// <summary>Sends the reminder to these people. If someone else reminded one of them a moment ago, nothing is sent.</summary>
    private async Task SendAsync(Roster roster, IReadOnlyList<long> recipientIds, CancellationToken cancellationToken)
    {
        var me = await users.GetByIdAsync(roster.Actor.UserId, cancellationToken);
        var senderName = me is null ? currentUser.FullName : PersonName.Of(me.FirstName, me.LastName, me.Email);

        await sender.SendOnceAsync(
            new NotificationDraft(
                NotificationKinds.HoursReminder,
                roster.Actor.UserId,
                DomainCodes.Design,
                null,
                null,
                null,
                new { senderName, weekStart = roster.WeekStart }),
            KeyOf(roster.Today),
            recipientIds,
            cancellationToken);

        // the sent key is unique per person and day: a second reminder at the same moment is refused here
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            throw new ConflictException("already_reminded", "Someone has just reminded them. Reload the list.");
        }
    }
}
