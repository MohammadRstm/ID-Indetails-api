using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Billing.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Notifications.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Repositories.Requirements.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;
using ID_Indetails.api.Services.Reports.Implementation;
using ID_Indetails.api.Services.Settings.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Implementation;

namespace ID_Indetails.api.Services.Notifications.Implementation;

/// <summary>
/// <list type="bullet">
/// <item>Weekly summary: from the first day of a week, what each Design admin and employee who logs work logged the
/// week before (zero included). While Design's <c>weeklySummary</c> is on.</item>
/// <item>Idle projects: an active Design project with no work for 7 days, to its owner and whoever closes projects.
/// Again after each new quiet stretch. While Design's <c>notifyIdle</c> is on.</item>
/// <item>Deadlines: the day before an open requirement of an active project is due, to its assignees; once it is
/// overdue, to them and whoever manages requirements. Deadlines passed more than a week ago are left alone, so turning
/// this on doesn't flood anyone with old ones.</item>
/// <item>Overdue invoices: once per invoice, to whoever sees the domain's money.</item>
/// <item>Short hours: from the first of a month, who finished the month before short of their hours, to whoever
/// sees salaries.</item>
/// <item>Cost warnings: once per project, when an active client project's cost passes 85% of its payment, to whoever
/// sees the domain's money.</item>
/// <item>Draft invoices: once per draft still unissued 3 days after it was created, to whoever sees the domain's money.</item>
/// <item>Auto-archive: see <see cref="IProjectAutoArchive"/>.</item>
/// </list>
/// </summary>
public sealed class ScheduledNotifications(
    INotificationSender sender,
    INotificationRepository notifications,
    IDomainSettingsService domainSettings,
    ICompanyRepository company,
    IWorkEntryRepository entries,
    IProjectRepository projects,
    IRequirementRepository requirements,
    IInvoiceRepository invoices,
    ISalaryService salaries,
    IProjectFinanceService finance,
    IProjectAutoArchive autoArchive,
    IUnitOfWork unitOfWork,
    ILiveUpdates live,
    IClock clock) : IScheduledNotifications
{
    public const int OverdueLookbackDays = 7;
    public const int ShortPeopleShown = 5;
    public const int DraftReminderDays = 3;
    private const int PageSize = 200;

    public async Task RunAsync(ScheduledCheck check, CancellationToken cancellationToken = default)
    {
        var today = clock.CompanyToday;
        await (check switch
        {
            ScheduledCheck.WeeklySummary => WeeklySummaryAsync(today, cancellationToken),
            ScheduledCheck.IdleProjects => IdleProjectsAsync(today, cancellationToken),
            ScheduledCheck.RequirementDeadlines => RequirementDeadlinesAsync(today, cancellationToken),
            ScheduledCheck.OverdueInvoices => OverdueInvoicesAsync(today, cancellationToken),
            ScheduledCheck.ShortHours => ShortHoursAsync(today, cancellationToken),
            ScheduledCheck.CostWarnings => CostWarningsAsync(cancellationToken),
            ScheduledCheck.DraftInvoices => DraftInvoicesAsync(today, cancellationToken),
            ScheduledCheck.AutoArchive => autoArchive.RunAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(check)),
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await live.FlushAsync(cancellationToken);
    }

    private async Task WeeklySummaryAsync(DateOnly today, CancellationToken cancellationToken)
    {
        if (!(await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken)).WeeklySummary)
        {
            return;
        }

        var calendar = CompanyCalendar.Of(await company.GetSettingsAsync(cancellationToken: cancellationToken));
        var weekStart = WorkStats.WeekStartOf(today, calendar.FirstDayOfWeek);
        var lastWeek = weekStart.AddDays(-7);
        var worked = (await entries.RowsAsync(
                new WorkEntrySearch(From: clock.CompanyDayStartUtc(lastWeek), To: clock.CompanyDayStartUtc(weekStart)),
                cancellationToken))
            .ToLookup(r => r.UserId);
        var loggers = (await sender.HoldersAsync(DomainCodes.Design, PermissionCodes.WorkEntryCreate, cancellationToken)).ToHashSet();

        foreach (var personId in (await sender.DomainMembersAsync(DomainCodes.Design, cancellationToken)).Where(loggers.Contains))
        {
            var rows = worked[personId].ToList();
            await sender.SendOnceAsync(
                new NotificationDraft(NotificationKinds.WeeklySummary, null, DomainCodes.Design, null, null, null,
                    new
                    {
                        weekStart = lastWeek,
                        minutes = rows.Sum(r => r.Minutes),
                        entries = rows.Count,
                        projects = rows.Select(r => r.ProjectId).Distinct().Count(),
                    }),
                $"weekly-summary-{lastWeek:yyyy-MM-dd}",
                [personId],
                cancellationToken);
        }
    }

    private async Task IdleProjectsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        if (!(await domainSettings.CurrentAsync(DomainCodes.Design, cancellationToken)).NotifyIdle)
        {
            return;
        }

        var active = await ActiveProjectsAsync(DomainCodes.Design, cancellationToken);
        if (active.Count == 0)
        {
            return;
        }

        var lastWork = (await entries.ContributionsAsync(active.Select(p => p.Id).ToList(), cancellationToken))
            .GroupBy(c => c.ProjectId)
            .ToDictionary(g => g.Key, g => clock.ToCompanyDate(g.Max(c => c.LastStartedAt)));
        var managers = await sender.HoldersAsync(DomainCodes.Design, PermissionCodes.ProjectChangeStatus, cancellationToken);

        foreach (var project in active)
        {
            DateOnly? lastWorkOn = lastWork.TryGetValue(project.Id, out var day) ? day : null;
            var quietSince = lastWorkOn ?? project.StartDate;
            var days = today.DayNumber - quietSince.DayNumber;
            if (days < ReportService.IdleDays)
            {
                continue;
            }

            await sender.SendOnceAsync(
                new NotificationDraft(NotificationKinds.ProjectIdle, null, project.DomainCode, project.Id, "project", project.Id,
                    new { projectTitle = project.Title, days, lastWorkOn }),
                $"project-idle-{project.Id}-{quietSince:yyyy-MM-dd}",
                managers.Append(project.OwnerId),
                cancellationToken);
        }
    }

    private async Task RequirementDeadlinesAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var tomorrow = today.AddDays(1);
        foreach (var r in await requirements.DueBetweenAsync(tomorrow, tomorrow, cancellationToken))
        {
            await sender.SendOnceAsync(
                Deadline(NotificationKinds.RequirementDueSoon, r),
                $"requirement-due-{r.Id}-{r.Deadline:yyyy-MM-dd}",
                r.AssigneeIds,
                cancellationToken);
        }

        var managers = new Dictionary<string, IReadOnlyList<long>>();
        foreach (var r in await requirements.DueBetweenAsync(today.AddDays(-OverdueLookbackDays), today.AddDays(-1), cancellationToken))
        {
            if (!managers.TryGetValue(r.DomainCode, out var domainManagers))
            {
                domainManagers = await sender.HoldersAsync(r.DomainCode, PermissionCodes.RequirementManage, cancellationToken);
                managers[r.DomainCode] = domainManagers;
            }

            await sender.SendOnceAsync(
                Deadline(NotificationKinds.RequirementOverdue, r),
                $"requirement-overdue-{r.Id}-{r.Deadline:yyyy-MM-dd}",
                r.AssigneeIds.Concat(domainManagers),
                cancellationToken);
        }
    }

    private async Task OverdueInvoicesAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var holders = new Dictionary<string, IReadOnlyList<long>>();
        for (var page = 1; ; page++)
        {
            var (items, total) = await invoices.SearchAsync(
                new InvoiceSearch(DomainCodes.All, null, null, null, Overdue: true, null, today, page, PageSize), cancellationToken);
            foreach (var invoice in items.Select(r => r.Invoice))
            {
                var domain = invoice.Project.DomainCode;
                if (!holders.TryGetValue(domain, out var moneyPeople))
                {
                    moneyPeople = await sender.HoldersAsync(domain, PermissionCodes.FinancialView, cancellationToken);
                    holders[domain] = moneyPeople;
                }

                await sender.SendOnceAsync(
                    new NotificationDraft(NotificationKinds.InvoiceOverdue, null, domain, invoice.ProjectId, "invoice", invoice.Id,
                        new { number = invoice.Number, dueOn = DateOnly.FromDateTime(invoice.DueAt!.Value) }),
                    $"invoice-overdue-{invoice.Id}",
                    moneyPeople,
                    cancellationToken);
            }

            if (page * PageSize >= total)
            {
                return;
            }
        }
    }

    private async Task ShortHoursAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var month = WorkStats.MonthStartOf(today).AddMonths(-1);
        var key = $"hours-short-{month:yyyy-MM}";
        var recipients = await sender.HoldersAsync(DomainCodes.Design, PermissionCodes.SalaryView, cancellationToken);

        // The salaries behind this are calculated, so skip them once everyone has been told.
        if (recipients.Count == 0 || (await notifications.RecipientsWithKeyAsync(key, recipients, cancellationToken)).Count == recipients.Count)
        {
            return;
        }

        var flagged = await salaries.FlaggedAsync(month, cancellationToken);
        if (flagged.Count == 0)
        {
            return;
        }

        await sender.SendOnceAsync(
            new NotificationDraft(NotificationKinds.HoursShort, null, DomainCodes.Design, null, null, null,
                new
                {
                    month = month.ToString("yyyy-MM"),
                    count = flagged.Count,
                    people = flagged.Take(ShortPeopleShown).Select(p => new { id = p.UserId, name = p.Name, shortMinutes = p.ShortMinutes }),
                }),
            key,
            recipients,
            cancellationToken);
    }

    private async Task CostWarningsAsync(CancellationToken cancellationToken)
    {
        foreach (var domain in DomainCodes.All.Where(MoneyAttention.WarnsOnCost))
        {
            var clientWork = (await ActiveProjectsAsync(domain, cancellationToken)).Where(p => p.BillingType == BillingType.Client).ToList();
            if (clientWork.Count == 0)
            {
                continue;
            }

            var money = await finance.GetAsync(clientWork, cancellationToken);
            var moneyPeople = await sender.HoldersAsync(domain, PermissionCodes.FinancialView, cancellationToken);
            foreach (var project in clientWork)
            {
                var summary = money[project.Id].Summary;
                if (!MoneyAttention.CostIsHigh(summary.Revenue, summary.Cost))
                {
                    continue;
                }

                await sender.SendOnceAsync(
                    new NotificationDraft(NotificationKinds.ProjectCostHigh, null, domain, project.Id, "project", project.Id,
                        new { projectTitle = project.Title, usedPct = MoneyAttention.UsedPct(summary.Revenue, summary.Cost) }),
                    $"project-cost-{project.Id}",
                    moneyPeople,
                    cancellationToken);
            }
        }
    }

    private async Task DraftInvoicesAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var createdBy = clock.UtcNow.AddDays(-DraftReminderDays);
        var holders = new Dictionary<string, IReadOnlyList<long>>();
        for (var page = 1; ; page++)
        {
            var (items, total) = await invoices.SearchAsync(
                new InvoiceSearch(DomainCodes.All, null, null, InvoiceStatus.Draft, null, null, today, page, PageSize), cancellationToken);
            foreach (var invoice in items.Select(r => r.Invoice).Where(i => i.CreatedAt <= createdBy))
            {
                var domain = invoice.Project.DomainCode;
                if (!holders.TryGetValue(domain, out var moneyPeople))
                {
                    moneyPeople = await sender.HoldersAsync(domain, PermissionCodes.FinancialView, cancellationToken);
                    holders[domain] = moneyPeople;
                }

                await sender.SendOnceAsync(
                    new NotificationDraft(NotificationKinds.InvoiceDraftWaiting, null, domain, invoice.ProjectId, "invoice", invoice.Id,
                        new { projectTitle = invoice.Project.Title, createdOn = clock.ToCompanyDate(invoice.CreatedAt) }),
                    $"invoice-draft-{invoice.Id}",
                    moneyPeople,
                    cancellationToken);
            }

            if (page * PageSize >= total)
            {
                return;
            }
        }
    }

    private async Task<IReadOnlyList<Project>> ActiveProjectsAsync(string domain, CancellationToken cancellationToken)
    {
        var all = new List<Project>();
        for (var page = 1; ; page++)
        {
            var (items, total) = await projects.SearchAsync(
                new ProjectSearch([domain], null, ProjectStatus.Active, null, null, domain, page, PageSize), cancellationToken);
            all.AddRange(items);
            if (page * PageSize >= total)
            {
                return all;
            }
        }
    }

    private static NotificationDraft Deadline(string kind, RequirementDeadline r)
        => new(kind, null, r.DomainCode, r.ProjectId, "requirement", r.Id, new { projectTitle = r.ProjectTitle, text = r.Text, deadline = r.Deadline });
}
