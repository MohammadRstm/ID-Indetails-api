namespace ID_Indetails.api.Business.Billing;

/// <summary>
/// A project's cash position, in USD:
/// <list type="bullet">
/// <item>Invoiced: issued, live invoices. Outstanding: what is left to pay on them.</item>
/// <item>Received: every live payment, on account or not. On account: live payments not tied to an invoice.</item>
/// <item>Not yet invoiced: revenue (the agreed amount in Execution) minus invoiced, never below zero.</item>
/// <item>Owed and paid ahead: revenue against everything received, whether or not it was invoiced.</item>
/// </list>
/// </summary>
public static class ProjectBilling
{
    public sealed record IssuedInvoice(decimal Total, decimal Paid);

    public sealed record Summary(
        decimal Invoiced,
        decimal Outstanding,
        decimal Received,
        decimal OnAccount,
        decimal NotYetInvoiced,
        decimal Owed,
        decimal PaidAhead);

    public static Summary Summarise(decimal revenue, IReadOnlyCollection<IssuedInvoice> invoices, decimal received, decimal onAccount)
    {
        var invoiced = invoices.Sum(i => i.Total);
        return new Summary(
            invoiced,
            invoices.Sum(i => InvoiceRules.Balance(i.Total, i.Paid)),
            received,
            onAccount,
            Math.Max(0, revenue - invoiced),
            Math.Max(0, revenue - received),
            Math.Max(0, received - revenue));
    }
}
