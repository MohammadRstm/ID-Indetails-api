using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Finance.Intelligence;

/// <summary>
/// Where a project's agreed profit comes from, step by step, net of TVA: the base price, what changed it (variation
/// orders, a settlement, an overpayment kept), a site's supervision and permits, then each cost source and the pocket
/// money. The steps always add up to the agreed profit: the base price is what is left of the revenue once the rest is
/// accounted for.
/// </summary>
public static class ProjectBridgeRules
{
    public sealed record Step(BridgeStepKind Kind, decimal Amount);

    /// <param name="Revenue">The agreed revenue, net of TVA (0 for internal work).</param>
    /// <param name="VariationOrders">Applied (Design) or agreed (a site) VOs, settlement and closing VOs left out.</param>
    /// <param name="Settlement">What a settlement took off the price, as a negative amount (0 when none).</param>
    /// <param name="PaidAhead">What keeping an overpayment added to the price (0 when none).</param>
    /// <param name="Supervision">A site's supervision (0 on Design).</param>
    /// <param name="Permits">A site's permits (0 on Design).</param>
    /// <param name="PocketNet">What the pocket money paid in less what was moved to it.</param>
    public sealed record Facts(
        string Domain,
        bool IsClientWork,
        decimal Revenue,
        decimal VariationOrders,
        decimal Settlement,
        decimal PaidAhead,
        decimal Supervision,
        decimal Permits,
        ProjectFinance.Costs Costs,
        decimal PocketNet);

    public static IReadOnlyList<Step> Of(Facts facts)
    {
        var steps = new List<Step>();
        void Add(BridgeStepKind kind, decimal amount)
        {
            if (amount != 0)
            {
                steps.Add(new Step(kind, amount));
            }
        }

        if (facts.IsClientWork)
        {
            var site = facts.Domain == DomainCodes.Execution;
            var supervision = site ? facts.Supervision : 0;
            var permits = site ? facts.Permits : 0;
            var basePrice = facts.Revenue - facts.VariationOrders - facts.Settlement - facts.PaidAhead - supervision - permits;
            steps.Add(new Step(BridgeStepKind.BasePrice, basePrice));
            Add(BridgeStepKind.VariationOrders, facts.VariationOrders);
            Add(BridgeStepKind.Settlement, facts.Settlement);
            Add(BridgeStepKind.PaidAhead, facts.PaidAhead);
            Add(BridgeStepKind.Supervision, supervision);
            Add(BridgeStepKind.Permits, permits);
        }

        Add(BridgeStepKind.Labour, -facts.Costs.Labour);
        Add(BridgeStepKind.Expenses, -facts.Costs.Expenses);
        Add(BridgeStepKind.SiteExpenses, -facts.Costs.SiteExpenses);
        Add(BridgeStepKind.GroundLabour, -facts.Costs.GroundLabour);
        Add(BridgeStepKind.Pocket, facts.PocketNet);
        return steps;
    }
}
