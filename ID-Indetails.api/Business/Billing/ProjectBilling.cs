using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Billing;

/// <summary>
/// The cash position of one project or many, in USD. The one billing rule: the project page, the lists and the
/// Financials page all use it.
/// <list type="bullet">
/// <item>Invoiced: issued, live invoices. Outstanding: what is left to pay on them. Drafts are counted apart.</item>
/// <item>Received: every live payment, on account or not. On account: payments not tied to an invoice; they count as
/// received but never reduce outstanding.</item>
/// <item>Overdue: invoices still sent or partly paid whose due date is before today (<see cref="InvoiceRules.IsOverdue"/>).</item>
/// <item>Not yet invoiced: the part of the revenue (the agreed amount in Execution) neither invoiced nor already
/// received on account, never below zero. Money paid up front leaves nothing to ask for.</item>
/// <item>Left to invoice: revenue minus invoiced, never below zero; what a new invoice can still be for (including
/// money received on account, which an invoice can take on).</item>
/// <item>Owed and paid ahead: revenue against everything received, whether or not it was invoiced.</item>
/// </list>
/// </summary>
public static class ProjectBilling
{
    /// <param name="Total">In USD.</param>
    /// <param name="Paid">Live payments against it, in USD.</param>
    public sealed record Invoice(long Id, long ProjectId, long ClientId, bool IsIssued, bool IsVoided, DateOnly? DueOn, decimal Total, decimal Paid)
    {
        public InvoiceStatus Status => InvoiceRules.Status(IsVoided, IsIssued, Total, Paid);

        public decimal Balance => InvoiceRules.Balance(Total, Paid);

        public bool IsOpen => Status is InvoiceStatus.Sent or InvoiceStatus.Partial;

        public bool IsOverdue(DateOnly today) => InvoiceRules.IsOverdue(Status, DueOn, today);
    }

    public sealed record Payment(long ProjectId, long ClientId, long? InvoiceId, DateOnly ReceivedOn, decimal AmountUsd, PaymentMethod Method);

    public sealed record Summary(
        decimal Revenue,
        decimal Invoiced,
        int IssuedCount,
        decimal Outstanding,
        decimal Received,
        int PaymentCount,
        decimal OnAccount,
        decimal Overdue,
        int OverdueCount,
        DateOnly? OldestOverdueDue,
        decimal NotYetInvoiced,
        decimal LeftToInvoice,
        decimal Drafts,
        decimal Owed,
        decimal PaidAhead);

    /// <summary>Days past the due date: <c>ToDays</c> 0 is "not yet due", a null <c>ToDays</c> is open-ended.</summary>
    public sealed record AgingTotal(int FromDays, int? ToDays, decimal Amount, int Count);

    private static readonly (int From, int? To)[] Buckets = [(int.MinValue, 0), (1, 30), (31, 60), (61, 90), (91, null)];

    /// <param name="revenue">The revenue of the client projects covered; internal projects add nothing.</param>
    /// <param name="payments">Live payments only.</param>
    public static Summary Summarise(decimal revenue, IReadOnlyCollection<Invoice> invoices, IReadOnlyCollection<Payment> payments, DateOnly today)
    {
        var issued = invoices.Where(i => i.IsIssued && !i.IsVoided).ToList();
        var overdue = issued.Where(i => i.IsOverdue(today)).ToList();
        var invoiced = issued.Sum(i => i.Total);
        var received = payments.Sum(p => p.AmountUsd);
        var onAccount = payments.Where(p => p.InvoiceId is null).Sum(p => p.AmountUsd);

        return new Summary(
            revenue,
            invoiced,
            issued.Count,
            issued.Sum(i => i.Balance),
            received,
            payments.Count,
            onAccount,
            overdue.Sum(i => i.Balance),
            overdue.Count,
            overdue.Count > 0 ? overdue.Min(i => i.DueOn) : null,
            Math.Max(0, revenue - invoiced - onAccount),
            Math.Max(0, revenue - invoiced),
            invoices.Where(i => !i.IsIssued && !i.IsVoided).Sum(i => i.Total),
            Math.Max(0, revenue - received),
            Math.Max(0, received - revenue));
    }

    /// <summary>Open invoices by how many days past their due date they are; every bucket is listed, empty or not.</summary>
    public static IReadOnlyList<AgingTotal> Aging(IEnumerable<Invoice> invoices, DateOnly today)
    {
        var late = invoices.Where(i => i.IsOpen).Select(i => (Invoice: i, Days: i.DueOn is { } due ? today.DayNumber - due.DayNumber : 0)).ToList();
        return Buckets
            .Select(b =>
            {
                var inside = late.Where(x => x.Days >= b.From && (b.To is null || x.Days <= b.To)).ToList();
                return new AgingTotal(Math.Max(0, b.From), b.To, inside.Sum(x => x.Invoice.Balance), inside.Count);
            })
            .ToList();
    }
}
