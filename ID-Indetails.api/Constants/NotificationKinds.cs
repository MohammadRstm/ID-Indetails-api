namespace ID_Indetails.api.Constants;

/// <summary>Values of <c>notifications.kind</c> written by the API.</summary>
public static class NotificationKinds
{
    /// <summary>Someone was assigned a requirement (subject: the requirement).</summary>
    public const string RequirementAssigned = "requirement_assigned";

    /// <summary>Someone logged work (subject: the work entry). Sent to the domain's admins per its <c>notify_entry</c> setting.</summary>
    public const string WorkLogged = "work_logged";

    /// <summary>One rolling notification per admin per day summing the work logged (digest key <c>work-digest-YYYY-MM-DD</c>).</summary>
    public const string WorkDigest = "work_digest";

    /// <summary>Someone commented on your work entry (subject: the work entry).</summary>
    public const string EntryComment = "entry_comment";

    /// <summary>Someone who doesn't manage meetings set one (subject: the meeting). Sent to the domain's meeting managers.</summary>
    public const string MeetingScheduled = "meeting_scheduled";

    /// <summary>Someone recorded materials on a site (subject: the project; the count in the payload).</summary>
    public const string MaterialsAdded = "materials_added";

    /// <summary>Someone submitted or updated a labour week (subject: the project; the week and whether it was an update in the payload).</summary>
    public const string LabourWeekSubmitted = "labour_week_submitted";
}
