using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.SiteExpenses;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Execution;

/// <summary>
/// An Execution site's account: what the client owes and has paid, what was spent, and the cash that should be left in
/// the project account, split by account label, category and payee. Costs, profit and the price come from the shared
/// finance calculator and are only arranged here.
/// <list type="bullet">
/// <item>Cash in account = received − cost − supervision taken − moved to the pocket money + paid by it. Received is
/// what the client paid, TVA included: the account holds the money, whatever it is for.</item>
/// <item>Each label's row has what it received (payments filed under it), what it spent (site expenses under it) and the
/// rest. The unlabelled row also carries every cost no label can hold (ground labour, staff, other, expenses with no
/// label) and, as <c>Other</c>, supervision taken and the pocket money's net move. It is worked out as the remainder, so
/// the rows always add up to cash in account.</item>
/// <item>The categories and payees break down site expenses only, the ones with a rate on their day.</item>
/// </list>
/// </summary>
public static class AccountFiguresRules
{
    /// <summary>A live payment, by the label it is filed under (none when null).</summary>
    public sealed record PaymentRow(long? LabelId, decimal AmountUsd);

    /// <param name="Payee">The supplier as typed; null or blank for none.</param>
    /// <param name="Usd">Its cost in USD at the rate of the day it was spent; null when there was no rate that day.</param>
    public sealed record ExpenseRow(long? LabelId, SiteExpenseCategory Category, string? Payee, decimal? Usd);

    /// <param name="ClientTotal">What the client pays in all: work, supervision and TVA.</param>
    /// <param name="SupervisionDue">The work price times the supervision rate.</param>
    /// <param name="Costs">The project's own cost figures, so they match its money.</param>
    /// <param name="LabelOrder">Every label (archived ones too) in the company's order.</param>
    /// <param name="Expenses">Newest recorded first: a payee is shown as it was last typed.</param>
    public sealed record Inputs(
        decimal ClientTotal,
        decimal SupervisionDue,
        ProjectFinance.Costs Costs,
        decimal SupervisionTaken,
        decimal ToPocket,
        decimal FromPocket,
        IReadOnlyList<long> LabelOrder,
        IReadOnlyList<PaymentRow> Payments,
        IReadOnlyList<ExpenseRow> Expenses);

    /// <param name="Other">Supervision taken plus what was moved to the pocket money, less what it paid back; only on the unlabelled row.</param>
    public sealed record LabelRow(long? LabelId, decimal Received, decimal Spent, decimal Other)
    {
        public decimal Rest => Received - Spent - Other;
    }

    public sealed record CategoryRow(SiteExpenseCategory Category, decimal Spent);

    public sealed record PayeeRow(string? Payee, decimal Spent);

    /// <param name="RestToPay">The client's total less what was received; below zero when they paid ahead.</param>
    /// <param name="SupervisionLeft">Supervision due less what was taken; below zero when more was taken.</param>
    /// <param name="ByLabel">One row per label with payments or site expenses, in the labels' order, then the unlabelled row.</param>
    public sealed record Figures(
        decimal Received,
        decimal RestToPay,
        decimal SupervisionLeft,
        decimal CashInAccount,
        IReadOnlyList<LabelRow> ByLabel,
        IReadOnlyList<CategoryRow> ByCategory,
        IReadOnlyList<PayeeRow> ByPayee);

    public static Figures Build(Inputs input)
    {
        var received = input.Payments.Sum(p => p.AmountUsd);
        var cashInAccount = received - input.Costs.Total - input.SupervisionTaken - input.ToPocket + input.FromPocket;
        var priced = input.Expenses.Where(e => e.Usd is not null).ToList();

        return new Figures(
            received,
            input.ClientTotal - received,
            SupervisionRules.Left(input.SupervisionDue, input.SupervisionTaken),
            cashInAccount,
            Labels(input, received, priced),
            priced.GroupBy(e => e.Category)
                .Select(g => new CategoryRow(g.Key, g.Sum(e => e.Usd!.Value)))
                .OrderByDescending(c => c.Spent).ThenBy(c => c.Category)
                .ToList(),
            priced.GroupBy(e => PayeeKey(e.Payee))
                .Select(g => new PayeeRow(g.Key is null ? null : g.First().Payee!.Trim(), g.Sum(e => e.Usd!.Value)))
                .OrderByDescending(p => p.Spent).ThenBy(p => p.Payee is null).ThenBy(p => p.Payee, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private static IReadOnlyList<LabelRow> Labels(Inputs input, decimal received, IReadOnlyList<ExpenseRow> priced)
    {
        var order = input.LabelOrder.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        var used = input.Payments.Select(p => p.LabelId)
            .Concat(input.Expenses.Select(e => e.LabelId))
            .OfType<long>()
            .Distinct()
            .OrderBy(id => order.GetValueOrDefault(id, int.MaxValue)).ThenBy(id => id)
            .ToList();

        var rows = used.Select(id => new LabelRow(
            id,
            input.Payments.Where(p => p.LabelId == id).Sum(p => p.AmountUsd),
            priced.Where(e => e.LabelId == id).Sum(e => e.Usd!.Value),
            0)).ToList();

        // what no label holds is the rest of the received money and of the cost
        rows.Add(new LabelRow(
            null,
            received - rows.Sum(r => r.Received),
            input.Costs.Total - rows.Sum(r => r.Spent),
            input.SupervisionTaken + input.ToPocket - input.FromPocket));
        return rows;
    }

    /// <summary>Payees are grouped by their text trimmed, with single spaces, in lower case; none for a blank one.</summary>
    private static string? PayeeKey(string? payee) => string.IsNullOrWhiteSpace(payee) ? null : SiteExpenseRules.GroupKey(payee);
}
