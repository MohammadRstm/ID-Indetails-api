using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Finance;

/// <summary>
/// A domain dashboard's money follow-ups, as in the prototype, 6 at most: overdue invoices (oldest due first), then
/// completed projects still owed on their invoices with nothing overdue, then active client projects with no payment
/// agreed or whose cost has passed 85% of it. The payment is the project's revenue (Execution: the agreed amount).
/// </summary>
public static class MoneyAttention
{
    public const decimal CostWarning = 0.85m;
    public const int MaxItems = 6;

    /// <param name="Outstanding">Left to pay on its issued invoices.</param>
    /// <param name="Overdue">Left to pay on its overdue invoices.</param>
    public sealed record Project(long Id, ProjectStatus Status, bool IsClientWork, decimal Revenue, decimal Cost, decimal Outstanding, decimal Overdue);

    /// <param name="Amount">Overdue invoice: its balance. Finished but owed: what is outstanding. Cost warning: the cost.</param>
    /// <param name="Revenue">Cost warning: the payment.</param>
    /// <param name="UsedPct">Cost warning: the cost as a whole percentage of the payment.</param>
    public sealed record Item(MoneyAttentionKind Kind, long ProjectId, long? InvoiceId, int? DaysOverdue, decimal? Amount, decimal? Revenue, int? UsedPct);

    /// <summary>Whether the cost has passed 85% of the payment.</summary>
    public static bool CostIsHigh(decimal revenue, decimal cost) => revenue > 0 && cost / revenue > CostWarning;

    public static int UsedPct(decimal revenue, decimal cost) => (int)Math.Round(cost / revenue * 100, MidpointRounding.AwayFromZero);

    /// <param name="projects">In the order their items should appear.</param>
    public static IReadOnlyList<Item> Of(IReadOnlyCollection<Project> projects, IEnumerable<ProjectBilling.Invoice> invoices, DateOnly today)
    {
        var items = invoices
            .Where(i => i.IsOverdue(today))
            .OrderBy(i => i.DueOn)
            .ThenBy(i => i.Id)
            .Select(i => new Item(MoneyAttentionKind.OverdueInvoice, i.ProjectId, i.Id, today.DayNumber - i.DueOn!.Value.DayNumber, i.Balance, null, null))
            .ToList();

        items.AddRange(projects
            .Where(p => p.Status == ProjectStatus.Completed && p.Outstanding > 0 && p.Overdue == 0)
            .Select(p => new Item(MoneyAttentionKind.FinishedOwed, p.Id, null, null, p.Outstanding, null, null)));

        foreach (var p in projects.Where(p => p.Status == ProjectStatus.Active && p.IsClientWork))
        {
            if (p.Revenue <= 0)
            {
                items.Add(new Item(MoneyAttentionKind.NoPayment, p.Id, null, null, null, null, null));
            }
            else if (CostIsHigh(p.Revenue, p.Cost))
            {
                items.Add(new Item(MoneyAttentionKind.CostHigh, p.Id, null, null, p.Cost, p.Revenue, UsedPct(p.Revenue, p.Cost)));
            }
        }

        return items.Take(MaxItems).ToList();
    }
}
