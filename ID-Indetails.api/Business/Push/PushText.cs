using System.Globalization;
using System.Text.Json;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Push;

/// <summary>What a push shows on the phone's lock screen.</summary>
public sealed record PushMessageText(string Title, string Body);

/// <summary>
/// The text of each push, written by the server. Pushes pass through Expo, Apple and Google and show on a lock screen,
/// so they are short and privacy-safe: the money kinds get a fixed neutral text and never a client's name, an amount, a
/// margin, a cost or pay (not even a project title, which can hold a client's name). Other kinds may name the project
/// and the person who acted. The in-app list keeps its own, fuller wording.
/// </summary>
public static class PushText
{
    public const int MaxPreviewLength = 100;

    private const string Someone = "Someone";
    private const string AProject = "a project";

    private static readonly IReadOnlyDictionary<string, PushMessageText> MoneyTexts = new Dictionary<string, PushMessageText>
    {
        [NotificationKinds.PaymentRecorded] = new("Payment", "Payment recorded. Open ID InDetails to see it."),
        [NotificationKinds.PocketRequested] = new("Pocket money", "A site asks for pocket money. Open ID InDetails to decide."),
        [NotificationKinds.InvoiceOverdue] = new("Invoices", "An invoice needs your attention."),
        [NotificationKinds.InvoiceDraftWaiting] = new("Invoices", "An invoice needs your attention."),
        [NotificationKinds.InvoiceReminderDue] = new("Invoices", "An invoice needs your attention."),
        [NotificationKinds.InvoiceSendDue] = new("Invoices", "An invoice needs your attention."),
        [NotificationKinds.InvoiceSendFailed] = new("Invoices", "An invoice couldn't be sent automatically."),
        [NotificationKinds.InvoiceIssued] = new("Billing", "Billing update. Open ID InDetails to see it."),
        [NotificationKinds.InvoiceSentAutomatically] = new("Billing", "Billing update. Open ID InDetails to see it."),
        [NotificationKinds.StatementSent] = new("Billing", "Billing update. Open ID InDetails to see it."),
        [NotificationKinds.ProjectClosed] = new("Project money", "A project's money needs a look."),
        [NotificationKinds.ProjectCostHigh] = new("Project money", "A project's money needs a look."),
        [NotificationKinds.ClientAdded] = new("Clients", "A new client was added."),
        [NotificationKinds.PayChanged] = new("Pay and hours", "Pay and hours update."),
        [NotificationKinds.HoursShort] = new("Pay and hours", "Pay and hours update."),
    };

    /// <summary>A notification's push, from its kind, who acted, its project, its payload and its digest count.</summary>
    public static PushMessageText ForNotification(string kind, string? actorName, string? projectTitle, JsonElement? payload, int count)
    {
        // The money kinds never look at the payload or the project, so nothing from them can reach the text.
        if (MoneyTexts.TryGetValue(kind, out var neutral) || PushCategories.Of(kind) == NotificationCategory.Money)
        {
            return neutral ?? new("ID InDetails", "You have a new update. Open ID InDetails to see it.");
        }

        var actor = Named(actorName, Someone);
        var project = Named(projectTitle, AProject);
        return kind switch
        {
            NotificationKinds.RequirementAssigned => new("New requirement", $"{actor} assigned you a requirement on {project}."),
            NotificationKinds.RequirementsAdded => new("New requirements", count > 1
                ? $"{count} requirements were added to {project} today."
                : $"{actor} added a requirement to {project}."),
            NotificationKinds.RequirementDone => new("Requirement done", $"{actor} completed a requirement on {project}."),
            NotificationKinds.RequirementDueSoon => new("Due tomorrow", $"A requirement on {project} is due tomorrow."),
            NotificationKinds.RequirementOverdue => new("Requirement overdue", $"A requirement on {project} is past its deadline."),

            NotificationKinds.WorkLogged => new("Work logged", $"{actor} logged work on {project}."),
            NotificationKinds.WorkDigest => WorkDigest(payload, actor, project, count),
            NotificationKinds.EntryComment => new("New comment", $"{actor} commented on your work on {project}."),
            NotificationKinds.WeeklySummary => new("Your week", "Last week's summary is ready."),
            NotificationKinds.HoursExcused => new("Hours excused", "Some of your hours were excused."),
            NotificationKinds.HoursMissed => new("Hours to log", Date(payload, "day") is { } day
                ? $"You logged less than {Day(day)} asked for. You can still log it this month."
                : "You logged less than a working day asked for. You can still log it this month."),
            NotificationKinds.HoursMissedList => new("Hours to log", Date(payload, "day") is { } listed
                ? $"Some people logged less than {Day(listed)} asked for."
                : "Some people logged less than a working day asked for."),
            NotificationKinds.ScheduleChanged => new("Schedule changed", Date(payload, "effectiveFrom") is { } from
                ? $"Your working schedule changes from {Day(from)}."
                : "Your working schedule changed."),

            NotificationKinds.ProjectCreated => new("New project", $"{actor} added {project}."),
            NotificationKinds.ProjectStatusChanged => new("Project update", StatusChange(Text(payload, "change"), project)),
            NotificationKinds.ProjectOwnerSet => new("Project owner", $"You're now the owner of {project}."),
            NotificationKinds.ProjectNearlyDone => new("Nearly done", Int(payload, "progressPct") is { } pct
                ? $"{project} is {pct}% done."
                : $"{project} is nearly done."),
            NotificationKinds.ProjectIdle => new("No recent work", Int(payload, "days") is { } days
                ? $"{project} has had no work for {days} days."
                : $"{project} has had no work for a while."),

            NotificationKinds.MeetingScheduled => new("Meeting set", $"{actor} set a meeting on {project}."),
            NotificationKinds.MeetingInvited => new("Meeting", $"{actor} invited you to a meeting."),
            NotificationKinds.MeetingMoved => new("Meeting moved", $"{actor} moved a meeting you're in."),
            NotificationKinds.MeetingCancelled => new("Meeting cancelled", $"{actor} cancelled a meeting you were in."),

            NotificationKinds.MaterialsAdded => new("Materials added", Int(payload, "count") is > 1 and var materials
                ? $"{actor} added {materials} materials on {project}."
                : $"{actor} added a material on {project}."),
            NotificationKinds.LabourWeekSubmitted => new("Labour week", LabourWeek(payload, actor, project)),

            NotificationKinds.InvitationAccepted => new("New colleague", $"{actor} accepted the invitation and joined."),
            NotificationKinds.RoleChanged => new("Your role changed", Role(Text(payload, "to")) is { } role
                ? $"You're now {role}."
                : "Your role changed."),
            NotificationKinds.DomainChanged => new("You moved", Domain(Text(payload, "to")) is { } domain
                ? $"You're now in {domain}."
                : "You moved to the other domain."),
            NotificationKinds.TeamChanged => new("Your team changed", HasValue(payload, "teamId")
                ? "You were moved to another team."
                : "You were taken out of your team."),
            NotificationKinds.PermissionsChanged => new("Permissions changed", "Your permissions changed."),

            // the client portal: who and where, never what they wrote
            NotificationKinds.PortalClientMessage => new("Client message", count > 1
                ? $"{count} new client messages in {project}."
                : $"{Named(Text(payload, "authorName"), Someone)} wrote in {project}'s client chat."),
            NotificationKinds.PortalClientComment => new("Client comment", count > 1
                ? $"{count} new client comments on {Text(payload, "itemTitle") ?? "a file"} in {project}."
                : $"{Named(Text(payload, "authorName"), Someone)} commented on {Text(payload, "itemTitle") ?? "a file"} in {project}."),
            NotificationKinds.PortalMention => new("Mentioned", $"{Named(Text(payload, "authorName"), Someone)} mentioned you in {project}'s client portal."),

            // the team's comments on project files: who and which file, never the text
            NotificationKinds.ProjectFileComment => new("File comment",
                $"{Named(Text(payload, "authorName"), actor)} commented on {Named(Text(payload, "fileTitle"), "a file")} in {project}."),

            _ => new("ID InDetails", "You have a new notification."),
        };
    }

    /// <summary>A chat message: who sent it and the start of what they wrote.</summary>
    /// <param name="teamName">Null for a direct message.</param>
    public static PushMessageText ForMessage(string senderName, string? teamName, string? body, string messageKind, bool hasAttachments, bool mentioned)
    {
        var sender = Named(senderName, Someone);
        var preview = Preview(body) ?? (hasAttachments ? "Sent a photo or video." : "Sent a message.");
        if (teamName is null)
        {
            return new(sender, preview);
        }

        var title = messageKind == MessageKinds.TaskComment
            ? $"{sender} commented on a task in {teamName}"
            : mentioned ? $"{sender} mentioned you in {teamName}" : $"{sender} in {teamName}";
        return new(title, preview);
    }

    public static PushMessageText LogReminder()
        => new("Log your hours", "Your working day ends soon and some of today's hours aren't logged yet.");

    public static PushMessageText LabourWeekReminder(string? projectTitle)
        => new("Labour week", $"This week's labour on {Named(projectTitle, AProject)} isn't reported yet.");

    /// <summary>One line of at most <see cref="MaxPreviewLength"/> characters, or null when there is no text.</summary>
    public static string? Preview(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var line = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= MaxPreviewLength ? line : line[..(MaxPreviewLength - 1)].TrimEnd() + "…";
    }

    private static PushMessageText WorkDigest(JsonElement? payload, string actor, string project, int count)
    {
        var latest = Named(Text(payload, "latestName"), actor);
        var on = Named(Text(payload, "projectTitle"), project);
        return new("Work logged today", count > 1
            ? $"{count} entries so far today, the latest by {latest} on {on}."
            : $"{latest} logged work on {on}.");
    }

    private static string LabourWeek(JsonElement? payload, string actor, string project)
    {
        var verb = Bool(payload, "updated") == true ? "updated" : "submitted";
        return Date(payload, "weekStart") is { } week
            ? $"{actor} {verb} the labour week of {Day(week)} on {project}."
            : $"{actor} {verb} a labour week on {project}.";
    }

    private static string StatusChange(string? change, string project) => change switch
    {
        "hold" => $"{project} was put on hold.",
        "resume" => $"{project} was resumed.",
        "complete" => $"{project} was completed.",
        "reopen" => $"{project} was reopened.",
        _ => $"{project} changed status.",
    };

    private static string? Role(string? code) => code switch
    {
        RoleCodes.SuperAdmin => "a super admin",
        RoleCodes.Admin => "an admin",
        RoleCodes.Employee => "an employee",
        _ => null,
    };

    private static string? Domain(string? code) => code switch
    {
        DomainCodes.Design => "Design",
        DomainCodes.Execution => "Execution",
        DomainCodes.Website => "Website",
        _ => null,
    };

    private static string Named(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Day(DateOnly day) => day.ToString("d MMM", CultureInfo.InvariantCulture);

    private static JsonElement? Property(JsonElement? payload, string name)
        => payload is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(name, out var value) ? value : null;

    private static string? Text(JsonElement? payload, string name)
        => Property(payload, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static int? Int(JsonElement? payload, string name)
        => Property(payload, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDecimal(out var number)
            ? (int)Math.Round(number, MidpointRounding.AwayFromZero)
            : null;

    private static bool? Bool(JsonElement? payload, string name) => Property(payload, name)?.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static bool HasValue(JsonElement? payload, string name)
        => Property(payload, name) is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };

    private static DateOnly? Date(JsonElement? payload, string name)
        => DateOnly.TryParseExact(Text(payload, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}
