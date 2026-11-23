using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.WorkEntries;

/// <summary>
/// Reopening a closed month for logging (23 November 2026). Past months never change (D11), except that a super admin may
/// open any closed month again until a day of their choosing, today or later: people then log, change and remove their
/// own work in it as in the current month, and it locks again by itself after that day.
/// </summary>
public static class ReopenRules
{
    public static RuleDenial? Check(DateOnly month, DateOnly openUntil, DateOnly today)
    {
        if (month.Day != 1)
        {
            return RuleDenial.Invalid(ErrorCodes.ValidationFailed, "month", "Give the month as its first day.");
        }

        if (month >= WorkEntryRules.OpenFrom(today))
        {
            return RuleDenial.Invalid(ErrorCodes.MonthNotClosed, "month", "That month is still open: only a closed month can be reopened.");
        }

        return openUntil < today
            ? RuleDenial.Invalid(ErrorCodes.ReopenUntilInPast, "openUntil", "Keep it open until today or a later day.")
            : null;
    }

    /// <summary>Whether a reopened month still takes work today.</summary>
    public static bool IsOpen(DateOnly openUntil, DateOnly today) => openUntil >= today;
}
