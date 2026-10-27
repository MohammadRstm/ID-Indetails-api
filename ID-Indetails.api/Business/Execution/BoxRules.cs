using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Execution;

/// <summary>
/// The Execution box: what its projects' clients paid, pooled. Money moves out only to projects in the red, never
/// beyond the balance; a closed project's revenue is taken out, or kept, by someone who sees money.
/// </summary>
public static class BoxRules
{
    public const int MaxNoteLength = 500;

    /// <summary>A new client payment goes in while the project is in the box or waiting for a decision.</summary>
    public static bool TakesPayments(BoxState? state) => state is BoxState.In or BoxState.Pending;

    /// <summary>
    /// Where a closing project ends up. With no choice it waits on the Box page; only someone who sees money chooses,
    /// and only to remove or keep.
    /// </summary>
    public static (BoxState? State, RuleDenial? Denial) OnClose(BoxChoice? choice, bool seesMoney)
    {
        if (choice is null)
        {
            return (BoxState.Pending, null);
        }

        if (!seesMoney)
        {
            return (null, NoMoney);
        }

        return choice switch
        {
            BoxChoice.Remove => (BoxState.Removed, null),
            BoxChoice.Keep => (BoxState.In, null),
            _ => (null, RuleDenial.Invalid(ErrorCodes.ValidationFailed, "box", "A closing project's money is removed from the box or kept in it.")),
        };
    }

    /// <summary>
    /// Whether a reopening project puts its earlier revenue back. Only a removed project has any to put back, and only
    /// someone who sees money chooses to; otherwise it counts afresh from today.
    /// </summary>
    public static (bool Readd, RuleDenial? Denial) OnReopen(BoxState? state, BoxChoice? choice, bool seesMoney)
    {
        if (choice is BoxChoice.Remove or BoxChoice.Keep)
        {
            return (false, RuleDenial.Invalid(ErrorCodes.ValidationFailed, "box", "A reopening project's earlier revenue is put back or left out."));
        }

        if (choice == BoxChoice.Readd && !seesMoney)
        {
            return (false, NoMoney);
        }

        return (state == BoxState.Removed && choice == BoxChoice.Readd, null);
    }

    /// <summary>What taking a project out removes: what it put in, as far as the box still holds it.</summary>
    public static decimal Removal(decimal contributed, decimal balance)
        => -Math.Max(0, Math.Min(contributed, balance));

    public static RuleDenial? CheckDecision(BoxState? state)
        => state == BoxState.Pending
            ? null
            : RuleDenial.BusinessRule(ErrorCodes.BoxNoDecision, "This project isn't waiting for a box decision.");

    public static RuleDenial? CheckDecisionChoice(BoxChoice choice)
        => choice is BoxChoice.Remove or BoxChoice.Keep
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "decision", "Remove the project's money from the box, or keep it.");

    /// <summary>Money goes only to a project in the red, at most what brings it back to zero and what the box holds.</summary>
    public static RuleDenial? CheckTransfer(decimal amount, decimal balance, decimal cashProfit)
    {
        if (cashProfit >= 0)
        {
            return RuleDenial.BusinessRule(ErrorCodes.BoxProjectNotInRed, "This project isn't in the red, so the box can't send it money.");
        }

        if (amount > -cashProfit)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "amount", $"The project needs at most {-cashProfit:0.##} USD to get back to zero.");
        }

        return amount > balance
            ? RuleDenial.BusinessRule(ErrorCodes.BoxShort, $"The box holds {balance:0.##} USD, less than that.")
            : null;
    }

    private static RuleDenial NoMoney
        => RuleDenial.Forbidden(ErrorCodes.Forbidden, "Only someone who sees money decides what happens to a project's money in the box.");
}
