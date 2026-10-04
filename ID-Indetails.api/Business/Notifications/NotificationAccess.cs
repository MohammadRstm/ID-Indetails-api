using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Notifications;

/// <summary>
/// The permission a notification was sent under because of what it says (someone's hours, money, salaries). It is
/// checked again whenever the notification is read, so someone who has lost it no longer sees what it told them.
/// </summary>
public static class NotificationAccess
{
    private static readonly IReadOnlyDictionary<string, string> SentUnder = new Dictionary<string, string>
    {
        [NotificationKinds.WorkLogged] = PermissionCodes.WorkEntryViewAll,
        [NotificationKinds.WorkDigest] = PermissionCodes.WorkEntryViewAll,
        [NotificationKinds.HoursShort] = PermissionCodes.SalaryView,
        [NotificationKinds.ProjectClosed] = PermissionCodes.FinancialView,
        [NotificationKinds.ProjectCostHigh] = PermissionCodes.FinancialView,
        [NotificationKinds.InvoiceIssued] = PermissionCodes.FinancialView,
        [NotificationKinds.InvoiceOverdue] = PermissionCodes.FinancialView,
        [NotificationKinds.InvoiceDraftWaiting] = PermissionCodes.FinancialView,
        [NotificationKinds.PaymentRecorded] = PermissionCodes.FinancialView,
        [NotificationKinds.StatementSent] = PermissionCodes.FinancialView,
    };

    /// <summary>The kinds <paramref name="reader"/> may no longer read.</summary>
    public static IReadOnlyList<string> HiddenKinds(UserAccess reader)
        => SentUnder.Where(k => !reader.Has(k.Value)).Select(k => k.Key).ToList();
}
