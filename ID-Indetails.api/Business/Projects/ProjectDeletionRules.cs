using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Projects;

/// <summary>
/// Deleting a project created by mistake (a flake), with everything on it. Only an archived project, and only one that
/// never held anything real: money that moved (issued invoices, voided ones included since their numbers must stay,
/// payments received, pocket money moves, supervision taken) or hours people logged. Those keep a project archived for
/// good. Old real projects are never deleted: they stay archived (docs/projects.md, "Deleting a project").
/// </summary>
public static class ProjectDeletionRules
{
    /// <summary>What keeps a project from being deleted, counted.</summary>
    /// <param name="Hours">Live hours entries on it.</param>
    /// <param name="HoursInClosedMonths">Of those, the ones in months already closed, which never change.</param>
    public sealed record Blocking(int IssuedInvoices, int Payments, int PocketMoves, int SupervisionTaken, int Hours, int HoursInClosedMonths);

    /// <summary>Why the project can't be deleted, in words, one line each; empty when it can.</summary>
    public static IReadOnlyList<string> Blockers(ProjectStatus status, Blocking b)
    {
        var said = new List<string>();
        if (status != ProjectStatus.Archived)
        {
            said.Add("It isn't archived. Archive it first.");
        }

        if (b.IssuedInvoices > 0)
        {
            said.Add($"{Count(b.IssuedInvoices, "issued invoice")}, voided ones included: their numbers must stay.");
        }

        if (b.Payments > 0)
        {
            said.Add($"{Count(b.Payments, "payment")} received from the client.");
        }

        if (b.PocketMoves > 0)
        {
            said.Add($"{Count(b.PocketMoves, "pocket money move")}.");
        }

        if (b.SupervisionTaken > 0)
        {
            said.Add($"{Count(b.SupervisionTaken, "supervision withdrawal")}.");
        }

        if (b.HoursInClosedMonths > 0)
        {
            said.Add($"{Count(b.Hours, "hours entry", "hours entries")} logged on it, {b.HoursInClosedMonths} in months already closed, which never change.");
        }
        else if (b.Hours > 0)
        {
            said.Add($"{Count(b.Hours, "hours entry", "hours entries")} logged this month: reopen the project and move them to the right one first.");
        }

        return said;
    }

    /// <summary>
    /// The typed name confirms the delete: the project's title exactly, spaces around it aside. Null when it does.
    /// </summary>
    public static RuleDenial? CheckConfirmed(string title, string? typed)
        => string.Equals(title.Trim(), typed?.Trim(), StringComparison.Ordinal)
            ? null
            : RuleDenial.Invalid(ErrorCodes.ValidationFailed, "title", "Type the project's name exactly as it is to delete it.");

    /// <summary>"3 site expenses", "1 site expense".</summary>
    public static string Count(int n, string one, string? many = null) => $"{n:#,0} {(n == 1 ? one : many ?? one + "s")}";
}
