using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Push;

/// <summary>
/// The category of each push kind (what a person can mute) and the Android channel it goes on. The phone app creates
/// the channels; their names are part of the app's contract.
/// </summary>
public static class PushCategories
{
    /// <summary>The log-your-hours reminder: it belongs to no category, so it can't be muted.</summary>
    public const string RemindersChannel = "reminders";

    /// <summary>Everything sent during the person's quiet hours: low importance, no sound.</summary>
    public const string QuietChannel = "quiet";

    private static readonly IReadOnlyDictionary<string, NotificationCategory> ByKind = new Dictionary<string, NotificationCategory>
    {
        [NotificationKinds.WorkLogged] = NotificationCategory.Work,
        [NotificationKinds.WorkDigest] = NotificationCategory.Work,
        [NotificationKinds.EntryComment] = NotificationCategory.Work,
        [NotificationKinds.WeeklySummary] = NotificationCategory.Work,
        [NotificationKinds.HoursExcused] = NotificationCategory.Work,
        [NotificationKinds.ScheduleChanged] = NotificationCategory.Work,
        [NotificationKinds.HoursMissed] = NotificationCategory.Work,
        [NotificationKinds.HoursMissedList] = NotificationCategory.Work,

        [NotificationKinds.RequirementAssigned] = NotificationCategory.Requirements,
        [NotificationKinds.RequirementsAdded] = NotificationCategory.Requirements,
        [NotificationKinds.RequirementDone] = NotificationCategory.Requirements,
        [NotificationKinds.RequirementDueSoon] = NotificationCategory.Requirements,
        [NotificationKinds.RequirementOverdue] = NotificationCategory.Requirements,

        [NotificationKinds.ProjectCreated] = NotificationCategory.Projects,
        [NotificationKinds.ProjectStatusChanged] = NotificationCategory.Projects,
        [NotificationKinds.ProjectOwnerSet] = NotificationCategory.Projects,
        [NotificationKinds.ProjectNearlyDone] = NotificationCategory.Projects,
        [NotificationKinds.ProjectIdle] = NotificationCategory.Projects,

        [NotificationKinds.MeetingScheduled] = NotificationCategory.Meetings,

        [NotificationKinds.MaterialsAdded] = NotificationCategory.Sites,
        [NotificationKinds.LabourWeekSubmitted] = NotificationCategory.Sites,
        [PushKinds.LabourWeekReminder] = NotificationCategory.Sites,

        [NotificationKinds.InvoiceIssued] = NotificationCategory.Money,
        [NotificationKinds.InvoiceSentAutomatically] = NotificationCategory.Money,
        [NotificationKinds.InvoiceSendDue] = NotificationCategory.Money,
        [NotificationKinds.InvoiceSendFailed] = NotificationCategory.Money,
        [NotificationKinds.InvoiceReminderDue] = NotificationCategory.Money,
        [NotificationKinds.InvoiceOverdue] = NotificationCategory.Money,
        [NotificationKinds.InvoiceDraftWaiting] = NotificationCategory.Money,
        [NotificationKinds.PaymentRecorded] = NotificationCategory.Money,
        [NotificationKinds.PocketRequested] = NotificationCategory.Money,
        [NotificationKinds.QuoteDue] = NotificationCategory.Money,
        [NotificationKinds.ChangeBlocked] = NotificationCategory.Money,
        [NotificationKinds.ProfitLow] = NotificationCategory.Money,
        [NotificationKinds.ProfitAtZero] = NotificationCategory.Money,
        [NotificationKinds.ProjectClosed] = NotificationCategory.Money,
        [NotificationKinds.ProjectCostHigh] = NotificationCategory.Money,
        [NotificationKinds.ClientAdded] = NotificationCategory.Money,
        [NotificationKinds.PayChanged] = NotificationCategory.Money,
        [NotificationKinds.HoursShort] = NotificationCategory.Money,

        [NotificationKinds.InvitationAccepted] = NotificationCategory.People,
        [NotificationKinds.RoleChanged] = NotificationCategory.People,
        [NotificationKinds.DomainChanged] = NotificationCategory.People,
        [NotificationKinds.TeamChanged] = NotificationCategory.People,
        [NotificationKinds.PermissionsChanged] = NotificationCategory.People,

        [PushKinds.Message] = NotificationCategory.Messages,
        [NotificationKinds.PortalClientMessage] = NotificationCategory.Messages,
        [NotificationKinds.PortalClientComment] = NotificationCategory.Messages,
        [NotificationKinds.PortalMention] = NotificationCategory.Messages,
        [NotificationKinds.ProjectFileComment] = NotificationCategory.Messages,
    };

    /// <summary>Null for the log-your-hours reminder, and for a kind this API doesn't know.</summary>
    public static NotificationCategory? Of(string kind) => ByKind.TryGetValue(kind, out var category) ? category : null;

    /// <summary>The Android channel outside quiet hours: the category's, or <see cref="RemindersChannel"/>.</summary>
    public static string ChannelOf(string kind) => Of(kind) switch
    {
        NotificationCategory.Work => "work",
        NotificationCategory.Requirements => "requirements",
        NotificationCategory.Projects => "projects",
        NotificationCategory.Meetings => "meetings",
        NotificationCategory.Sites => "sites",
        NotificationCategory.Money => "money",
        NotificationCategory.People => "people",
        NotificationCategory.Messages => "messages",
        _ => RemindersChannel,
    };
}
