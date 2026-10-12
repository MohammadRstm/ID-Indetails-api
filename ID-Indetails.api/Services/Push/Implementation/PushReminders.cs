using ID_Indetails.api.Business.Labour;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Repositories.Labour.Abstraction;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.Push.Abstraction;
using ID_Indetails.api.Services.Settings.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Push.Implementation;

/// <summary>
/// The reminders are only worked out while pushes are on. A person's day comes from the same calendar as their hours
/// ledger (their own schedule or the company week), and what they logged within their hours from the same overtime
/// split as "My schedule".
/// </summary>
public sealed class PushReminders(
    INotificationSender people,
    IDomainSettingsService domainSettings,
    IPayrollRepository payroll,
    IPayrollInputs inputs,
    ILabourRepository labour,
    IPushQueue pushes,
    ReminderLog sent,
    IOptions<PushOptions> options,
    IClock clock) : IPushReminders
{
    public async Task LogHoursAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled || !(await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken)).LogReminder)
        {
            return;
        }

        var (today, minute) = Now();
        var loggers = (await people.HoldersAsync(DomainCodes.Design, PermissionCodes.WorkEntryCreate, cancellationToken)).ToHashSet();
        var ids = (await people.DomainMembersAsync(DomainCodes.Design, cancellationToken))
            .Where(id => loggers.Contains(id) && !sent.WasSent(id, PushKinds.LogReminder, null, today))
            .ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var users = await payroll.UsersAsync(ids, cancellationToken);
        foreach (var month in await inputs.ForAsync(users, [new DateOnly(today.Year, today.Month, 1)], cancellationToken))
        {
            var input = month.Input;
            var inside = Overtime.Of(input.Work, input.Calendar).Where(w => w.Day == today).Sum(w => w.Normal);
            var excused = input.Excuses.Any(e => e.Kind == ExcuseKind.WholeMonth || e.Days.Contains(today));
            if (ReminderRules.LogReminderDue(input.Calendar.HoursOn(today), minute, inside, excused)
                && sent.TryMark(month.User.Id, PushKinds.LogReminder, null, today))
            {
                pushes.Reminder(month.User.Id, PushKinds.LogReminder, PushTarget.OfPage("work"), null);
            }
        }
    }

    public async Task LabourWeeksAsync(CancellationToken cancellationToken = default)
    {
        var (today, minute) = Now();
        if (!options.Value.Enabled || !ReminderRules.LabourReminderDue(today, minute))
        {
            return;
        }

        var reporters = (await people.HoldersAsync(DomainCodes.Execution, PermissionCodes.LabourReportCreate, cancellationToken)).ToHashSet();
        var ids = (await people.DomainMembersAsync(DomainCodes.Execution, cancellationToken)).Where(reporters.Contains).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var weekStart = LabourRules.WeekStartOf(today);
        var since = clock.UtcNow.AddDays(-ReminderRules.LabourLookbackDays);
        foreach (var site in await labour.UnreportedSitesAsync(ids, since, weekStart, cancellationToken))
        {
            if (sent.TryMark(site.UserId, PushKinds.LabourWeekReminder, site.ProjectId, today))
            {
                pushes.Reminder(site.UserId, PushKinds.LabourWeekReminder, PushTarget.LabourWeek(site.ProjectId, weekStart), site.ProjectTitle);
            }
        }
    }

    private (DateOnly Today, int Minute) Now()
    {
        var local = clock.ToCompanyTime(clock.UtcNow);
        return (DateOnly.FromDateTime(local), local.Hour * 60 + local.Minute);
    }
}
