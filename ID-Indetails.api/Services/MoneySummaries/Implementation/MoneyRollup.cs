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
/// The money of a set of projects, loaded once: the shared calculator's figures and dated costs, and billing in USD.
/// Invoices with no exchange rate on their day are left out of the billing figures and listed in <see cref="UnconvertedInvoices"/>.
/// </summary>
public sealed class MoneyRollup(
    IReadOnlyDictionary<long, ProjectCostHistory> history,
    IReadOnlyList<BillingPosition.Invoice> invoices,
    IReadOnlyDictionary<long, InvoiceMoneyRow> invoiceRows,
    IReadOnlyList<BillingPosition.Payment> payments,
    IReadOnlyList<UnconvertedInvoicesResponse> unconvertedInvoices,
    DateOnly today)
{
    public DateOnly Today { get; } = today;

    public IReadOnlyList<BillingPosition.Invoice> Invoices { get; } = invoices;

    public IReadOnlyList<BillingPosition.Payment> Payments { get; } = payments;

    public IReadOnlyList<UnconvertedInvoicesResponse> UnconvertedInvoices { get; } = unconvertedInvoices;

    public ProjectFinancials Money(long projectId) => history[projectId].Money;

    public IReadOnlyList<Recognition.DatedCost> Costs(long projectId) => history[projectId].Costs;

    public InvoiceMoneyRow InvoiceRow(long invoiceId) => invoiceRows[invoiceId];

    public BillingPosition.Position Billing(IEnumerable<Project> projects)
    {
        var list = projects.ToList();
        var ids = list.Select(p => p.Id).ToHashSet();
        return BillingPosition.Of(
            list.Where(p => p.BillingType == BillingType.Client).Sum(p => Money(p.Id).Summary.Revenue),
            Invoices.Where(i => ids.Contains(i.ProjectId)).ToList(),
            Payments.Where(p => ids.Contains(p.ProjectId)).ToList(),
            Today);
    }

    public ProjectListMoneyResponse ListMoney(Project project)
    {
        var summary = Money(project.Id).Summary;
        var billing = Billing([project]);
        return new ProjectListMoneyResponse(
            summary.Revenue, summary.RecognisedToDate, summary.Cost, summary.Profit, summary.Margin, summary.MarginKnown,
            billing.Invoiced, billing.Outstanding);
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
            BillingTotals(Billing(projects)));
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
            project.StartDate);
    }

    public static ProjectCostResponse CostOf(ProjectFinance.Costs costs, int unpricedMinutes) => new(
        costs.Labour,
        costs.Expenses,
        costs.Materials,
        costs.GroundLabour,
        costs.Total,
        costs.Unconverted.Select(u => new UnconvertedCostResponse(u.Source, u.Currency, u.Amount)).ToList(),
        unpricedMinutes);

    public static BillingTotalsResponse BillingTotals(BillingPosition.Position b)
        => new(b.Invoiced, b.Received, b.Outstanding, b.Overdue, b.NotYetInvoiced, b.Drafts);

    public BillingPositionResponse Position(BillingPosition.Position b) => new(
        b.Value, b.Invoiced, b.IssuedCount, b.Received, b.OnAccount, b.Outstanding, b.Overdue, b.OverdueCount,
        b.OldestOverdueDue is { } due ? Today.DayNumber - due.DayNumber : null,
        b.NotYetInvoiced, b.Drafts);

    public static decimal Hours(int minutes) => Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);

    private static bool IsDone(Project p) => p.Status is ProjectStatus.Completed or ProjectStatus.Archived;

    private (ProjectFinance.Summary Summary, ProjectFinance.Costs Costs) Combined(IReadOnlyCollection<Project> projects)
    {
        var moneys = projects.Select(p => Money(p.Id)).ToList();
        var costs = new ProjectFinance.Costs(
            moneys.Sum(m => m.Costs.Labour),
            moneys.Sum(m => m.Costs.Expenses),
            moneys.Sum(m => m.Costs.Materials),
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
            moneys.Sum(m => m.LabourMinutes));

        return (ProjectFinance.Summarise(facts, costs), costs);
    }
}
