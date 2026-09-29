using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Billing;

/// <summary>
/// Billing over many projects, in USD, with the same rules as a single project's billing (<see cref="ProjectBilling"/>):
/// <list type="bullet">
/// <item>Invoiced: issued, live invoices. Outstanding: what is left to pay on them. Drafts are counted apart.</item>
/// <item>Received: every live payment. Payments on account count as received but never reduce outstanding.</item>
/// <item>Overdue: invoices still sent or partly paid whose due date is before today (<see cref="InvoiceRules.IsOverdue"/>).</item>
/// <item>Not yet invoiced: the value of the client projects not covered by an invoice yet.</item>
/// </list>
/// </summary>
public static class BillingPosition
{
    /// <param name="Total">In USD.</param>
    /// <param name="Paid">Live payments against it, in USD.</param>
    public sealed record Invoice(long Id, long ProjectId, long ClientId, bool IsIssued, bool IsVoided, DateOnly? DueOn, decimal Total, decimal Paid)
    {
        public InvoiceStatus Status => InvoiceRules.Status(IsVoided, IsIssued, Total, Paid);

        public decimal Balance => InvoiceRules.Balance(Total, Paid);

        public bool IsOverdue(DateOnly today) => InvoiceRules.IsOverdue(Status, DueOn, today);

        public bool IsOpen => Status is InvoiceStatus.Sent or InvoiceStatus.Partial;
    }

    public sealed record Payment(long ProjectId, long ClientId, long? InvoiceId, DateOnly ReceivedOn, decimal AmountUsd, PaymentMethod Method);

    public sealed record Position(
        decimal Value,
        decimal Invoiced,
        int IssuedCount,
        decimal Received,
        decimal OnAccount,
        decimal Outstanding,
        decimal Overdue,
        int OverdueCount,
        DateOnly? OldestOverdueDue,
        decimal NotYetInvoiced,
        decimal Drafts);

    /// <summary>Days past the due date: <c>ToDays</c> 0 is "not yet due", a null <c>ToDays</c> is open-ended.</summary>
    public sealed record AgingTotal(int FromDays, int? ToDays, decimal Amount, int Count);

    private static readonly (int From, int? To)[] Buckets = [(int.MinValue, 0), (1, 30), (31, 60), (61, 90), (91, null)];

    /// <param name="value">The revenue of the client projects covered.</param>
    public static Position Of(decimal value, IReadOnlyCollection<Invoice> invoices, IReadOnlyCollection<Payment> payments, DateOnly today)
    {
        var issued = invoices.Where(i => i.IsIssued && !i.IsVoided).ToList();
        var overdue = issued.Where(i => i.IsOverdue(today)).ToList();
        var invoiced = issued.Sum(i => i.Total);

        return new Position(
            value,
            invoiced,
            issued.Count,
            payments.Sum(p => p.AmountUsd),
            payments.Where(p => p.InvoiceId is null).Sum(p => p.AmountUsd),
            issued.Sum(i => i.Balance),
            overdue.Sum(i => i.Balance),
            overdue.Count,
            overdue.Count > 0 ? overdue.Min(i => i.DueOn) : null,
            Math.Max(0, value - invoiced),
            invoices.Where(i => !i.IsIssued && !i.IsVoided).Sum(i => i.Total));
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
