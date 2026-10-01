namespace ID_Indetails.api.Business.Finance;

/// <summary>
/// What a domain expects to earn, on the day it was agreed, apart from what it has received (<see cref="PeriodMoney.Received"/>):
/// <list type="bullet">
/// <item>a client project's payment (Design) or fee (Execution) counts on the day it was set, and a change counts its
/// difference on the day it changed, so a period shows what was agreed in it;</item>
/// <item>an Execution material counts at its client price on the day a statement listing it was first sent to the client.</item>
/// </list>
/// Completing a project changes none of it: it only stops the amounts changing.
/// </summary>
public static class ExpectedRevenue
{
    public sealed record Agreed(DateOnly Day, decimal Usd);

    /// <summary>A payment or fee set to <paramref name="amount"/> from <paramref name="previous"/> (none when first set).</summary>
    public static Agreed Change(DateOnly day, decimal amount, decimal? previous) => new(day, amount - (previous ?? 0));

    public static decimal In(IEnumerable<Agreed> agreed, DateOnly from, DateOnly to)
        => PeriodMoney.Round(agreed.Where(a => a.Day >= from && a.Day <= to).Sum(a => a.Usd));
}
