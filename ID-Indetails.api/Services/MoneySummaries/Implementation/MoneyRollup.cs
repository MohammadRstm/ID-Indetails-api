using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.MoneySummaries.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;

namespace ID_Indetails.api.Services.MoneySummaries.Implementation;

/// <summary>
/// The money of a set of projects, loaded once: the shared calculator's figures and dated costs, and billing in USD
/// (<see cref="ProjectBilling"/>). Invoices with no exchange rate on their day are left out of the billing figures and
/// listed by <see cref="UnconvertedInvoices"/>.
/// </summary>
public sealed class MoneyRollup(
    IReadOnlyDictionary<long, ProjectCostHistory> history,
    IReadOnlyList<ProjectBilling.Invoice> invoices,
    IReadOnlyDictionary<long, InvoiceMoneyRow> invoiceRows,
    IReadOnlyList<InvoiceMoneyRow> unconvertedInvoices,
    IReadOnlyList<ProjectBilling.Payment> payments,
    DateOnly today)
{
    public DateOnly Today { get; } = today;

    public IReadOnlyList<ProjectBilling.Invoice> Invoices { get; } = invoices;

    public IReadOnlyList<ProjectBilling.Payment> Payments { get; } = payments;

    public ProjectFinancials Money(long projectId) => history[projectId].Money;

    public IReadOnlyList<Recognition.DatedCost> Costs(long projectId) => history[projectId].Costs;

    /// <summary>The project's costs with no exchange rate on their day.</summary>
    public IReadOnlyList<ProjectFinance.DatedUnconverted> Unconverted(long projectId) => history[projectId].Unconverted;

    public InvoiceMoneyRow InvoiceRow(long invoiceId) => invoiceRows[invoiceId];

    public ProjectBilling.Summary Billing(IEnumerable<Project> projects)
    {
        var list = projects.ToList();
        var ids = list.Select(p => p.Id).ToHashSet();
        return ProjectBilling.Summarise(
            // the client owes the agreed amount with TVA on it
            list.Where(p => p.BillingType == BillingType.Client).Sum(p => Money(p.Id).RevenueWithTva),
            Invoices.Where(i => ids.Contains(i.ProjectId)).ToList(),
            Payments.Where(p => ids.Contains(p.ProjectId)).ToList(),
            Today);
    }

    /// <summary>Live invoices of these projects (all of them when not given) that have no exchange rate on their day.</summary>
    public IReadOnlyList<UnconvertedInvoicesResponse> UnconvertedInvoices(IEnumerable<Project>? projects = null)
    {
        var ids = projects?.Select(p => p.Id).ToHashSet();
        return unconvertedInvoices
            .Where(r => !r.IsVoided && (ids is null || ids.Contains(r.ProjectId)))
            .GroupBy(r => r.Currency.ToUpperInvariant())
            .Select(g => new UnconvertedInvoicesResponse(g.Key, g.Sum(r => r.Total), g.Count()))
            .ToList();
    }

    /// <summary>The billing block of a project's page.</summary>
    public ProjectBillingResponse ProjectBillingOf(Project project)
    {
        var b = Billing([project]);
        return new ProjectBillingResponse(
            b.Invoiced,
            b.Outstanding,
            b.Received,
            b.OnAccount,
            b.NotYetInvoiced,
            b.LeftToInvoice,
            b.Owed,
            b.PaidAhead,
            b.IssuedCount,
            b.PaymentCount,
            b.Overdue,
            b.OverdueCount,
            DaysSince(b.OldestOverdueDue),
            b.Drafts,
            UnconvertedInvoices([project]));
    }

    public ProjectListMoneyResponse ListMoney(Project project)
    {
        var money = Money(project.Id);
        var s = money.Summary;
        var billing = Billing([project]);
        return new ProjectListMoneyResponse(
            s.Revenue, s.RecognisedToDate, s.Cost, s.Profit, s.Margin, s.MarginKnown,
            billing.Invoiced, billing.Outstanding, HasUnconverted([project]), money.UnpricedLabourMinutes);
    }

    /// <summary>
    /// A client's projects as one: the project margin rule applies to the whole, with the client's progress being
    /// the share of their revenue recognised so far, and their work done once every project is.
    /// </summary>
    public ClientMoneyResponse ClientMoney(IReadOnlyCollection<Project> projects)
    {
        var all = Combined(projects);
        var completed = projects.Where(IsDone).ToList();
        var completedSummary = completed.Count > 0 ? Combined(completed).Summary : null;

        return new ClientMoneyResponse(
            projects.Count,
            projects.Count(p => p.Status is ProjectStatus.Active or ProjectStatus.Hold),
            completed.Count,
            Hours(projects.Sum(p => Money(p.Id).LabourMinutes)),
            all.Summary.Revenue,
            all.Summary.RecognisedToDate,
            CostOf(all.Costs, projects.Sum(p => Money(p.Id).UnpricedLabourMinutes)),
            all.Summary.Profit,
            all.Summary.Margin,
            all.Summary.MarginKnown,
            projects.Where(p => !IsDone(p)).Sum(p => Money(p.Id).Summary.Revenue),
            completed.Sum(p => Money(p.Id).Summary.Revenue),
            completedSummary?.Margin,
            BillingTotals(Billing(projects)),
            UnconvertedInvoices(projects));
    }

    public ClientProjectMoneyResponse ClientProject(Project project)
    {
        var money = Money(project.Id);
        return new ClientProjectMoneyResponse(
            project.Id,
            project.Title,
            project.Status,
            Hours(money.LabourMinutes),
            money.Summary.Revenue,
            money.Summary.Cost,
            money.Summary.Profit,
            money.Summary.Margin,
            money.Summary.MarginKnown,
            BillingTotals(Billing([project])),
            project.StartDate,
            HasUnconverted([project]),
            money.UnpricedLabourMinutes);
    }

    public static ProjectCostResponse CostOf(ProjectFinance.Costs costs, int unpricedMinutes) => new(
        costs.Labour,
        costs.Expenses,
        costs.SiteExpenses,
        costs.GroundLabour,
        costs.Total,
        costs.Unconverted.Select(u => new UnconvertedCostResponse(u.Source, u.Currency, u.Amount)).ToList(),
        unpricedMinutes);

    public static BillingTotalsResponse BillingTotals(ProjectBilling.Summary b)
        => new(b.Invoiced, b.Received, b.Outstanding, b.Overdue, b.NotYetInvoiced, b.Drafts);

    public BillingPositionResponse Position(ProjectBilling.Summary b) => new(
        b.Revenue, b.Invoiced, b.IssuedCount, b.Received, b.OnAccount, b.Outstanding, b.Overdue, b.OverdueCount,
        DaysSince(b.OldestOverdueDue), b.NotYetInvoiced, b.Drafts, b.PaymentCount);

    public static decimal Hours(int minutes) => Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);

    /// <summary>Whether any of the projects' costs or invoices is left out of the USD figures for want of a rate.</summary>
    public bool HasUnconverted(IReadOnlyCollection<Project> projects)
        => projects.Any(p => Unconverted(p.Id).Count > 0) || UnconvertedInvoices(projects).Count > 0;

    private int? DaysSince(DateOnly? day) => day is { } d ? Today.DayNumber - d.DayNumber : null;

    private static bool IsDone(Project p) => p.Status is ProjectStatus.Completed or ProjectStatus.Archived;

    private (ProjectFinance.Summary Summary, ProjectFinance.Costs Costs) Combined(IReadOnlyCollection<Project> projects)
    {
        var moneys = projects.Select(p => Money(p.Id)).ToList();
        var costs = new ProjectFinance.Costs(
            moneys.Sum(m => m.Costs.Labour),
            moneys.Sum(m => m.Costs.Expenses),
            moneys.Sum(m => m.Costs.SiteExpenses),
            moneys.Sum(m => m.Costs.GroundLabour),
            moneys.SelectMany(m => m.Costs.Unconverted)
                .GroupBy(u => (u.Source, u.Currency))
                .Select(g => new ProjectFinance.UnconvertedCost(g.Key.Source, g.Key.Currency, g.Sum(u => u.Amount)))
                .ToList());
        var revenue = moneys.Sum(m => m.Summary.Revenue);
        var recognised = moneys.Sum(m => m.Summary.RecognisedToDate);
        var facts = new ProjectFinance.Facts(
            projects.Any(p => p.BillingType == BillingType.Client),
            projects.Count > 0 && projects.All(IsDone),
            revenue > 0 ? (int)Math.Round(recognised * 100 / revenue, MidpointRounding.AwayFromZero) : 0,
            revenue,
            moneys.Sum(m => m.LabourMinutes),
            moneys.Sum(m => m.FromPocket - m.ToPocket));

        return (ProjectFinance.Summarise(facts, costs), costs);
    }
}
