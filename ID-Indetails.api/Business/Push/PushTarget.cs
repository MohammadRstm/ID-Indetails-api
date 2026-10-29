using System.Globalization;
using System.Text.Json;
using ID_Indetails.api.Constants;

namespace ID_Indetails.api.Business.Push;

/// <summary>
/// The screen a push opens, as the phone app's contract describes it (<c>PushTarget</c>): a type and the ids it needs.
/// The app checks every target against the person's permissions before opening it, and opens Alerts otherwise.
/// </summary>
public sealed record PushTarget
{
    public const string Project = "project";
    public const string Requirement = "requirement";
    public const string WorkEntry = "work_entry";
    public const string Conversation = "conversation";
    public const string LabourWeekType = "labour_week";
    public const string Person = "person";
    public const string Client = "client";
    public const string Invoice = "invoice";
    public const string Payment = "payment";
    public const string Statement = "statement";
    public const string PageType = "page";

    public required string Type { get; init; }

    public long? Id { get; init; }

    public long? ProjectId { get; init; }

    public long? RequirementId { get; init; }

    /// <summary><c>design</c> or <c>execution</c>, for projects and requirements.</summary>
    public string? Domain { get; init; }

    /// <summary>A part of a project: <c>requirements</c>, <c>meetings</c>, <c>materials</c> or <c>labour</c>.</summary>
    public string? Section { get; init; }

    public long? TeamId { get; init; }

    public long? UserId { get; init; }

    public DateOnly? WeekStart { get; init; }

    /// <summary><c>notifications</c>, <c>today</c>, <c>work</c>, <c>meetings</c> or <c>company</c>.</summary>
    public string? Page { get; init; }

    public static PushTarget Notifications { get; } = OfPage("notifications");

    public static PushTarget OfPage(string page) => new() { Type = PageType, Page = page };

    /// <summary>A team chat, or the direct chat with <paramref name="userId"/> (whoever wrote to the reader).</summary>
    public static PushTarget Chat(long? teamId, long? userId) => new() { Type = Conversation, TeamId = teamId, UserId = teamId is null ? userId : null };

    public static PushTarget LabourWeek(long projectId, DateOnly weekStart) => new() { Type = LabourWeekType, ProjectId = projectId, WeekStart = weekStart };

    /// <summary>
    /// Where a notification leads, from its kind, project (with the project's domain), subject and payload. Anything
    /// without its own screen, or missing what its screen needs, opens the notifications list.
    /// </summary>
    public static PushTarget ForNotification(
        string kind,
        long? projectId,
        string? projectDomain,
        string? subjectType,
        long? subjectId,
        JsonElement? payload)
    {
        PushTarget? target = kind switch
        {
            NotificationKinds.RequirementAssigned or NotificationKinds.RequirementDone
                or NotificationKinds.RequirementDueSoon or NotificationKinds.RequirementOverdue
                => projectId is { } p && subjectType == "requirement" && subjectId is { } r && projectDomain is not null
                    ? new() { Type = Requirement, ProjectId = p, RequirementId = r, Domain = projectDomain }
                    : null,
            NotificationKinds.RequirementsAdded => ProjectPart(projectId, projectDomain, "requirements"),
            NotificationKinds.MeetingScheduled => ProjectPart(projectId, projectDomain, "meetings"),
            NotificationKinds.MaterialsAdded => ProjectPart(projectId, projectDomain, "materials"),
            NotificationKinds.LabourWeekSubmitted => projectId is { } site && WeekStartOf(payload) is { } week
                ? LabourWeek(site, week)
                : ProjectPart(projectId, projectDomain, "labour"),

            NotificationKinds.ProjectCreated or NotificationKinds.ProjectStatusChanged or NotificationKinds.ProjectOwnerSet
                or NotificationKinds.ProjectNearlyDone or NotificationKinds.ProjectIdle
                or NotificationKinds.ProjectClosed or NotificationKinds.ProjectCostHigh or NotificationKinds.QuoteDue
                => ProjectPart(projectId, projectDomain, null),

            NotificationKinds.WorkLogged or NotificationKinds.EntryComment => Subject(WorkEntry, "work_entry", subjectType, subjectId),
            NotificationKinds.WorkDigest or NotificationKinds.WeeklySummary or NotificationKinds.HoursMissed => OfPage("work"),
            NotificationKinds.HoursMissedList => OfPage("today"),

            NotificationKinds.InvitationAccepted => Subject(Person, "user", subjectType, subjectId),
            NotificationKinds.ClientAdded => Subject(Client, "client", subjectType, subjectId),
            NotificationKinds.PaymentRecorded => Subject(Payment, "payment", subjectType, subjectId),
            NotificationKinds.StatementSent => Subject(Statement, "statement", subjectType, subjectId),
            NotificationKinds.InvoiceIssued or NotificationKinds.InvoiceSentAutomatically or NotificationKinds.InvoiceSendDue
                or NotificationKinds.InvoiceSendFailed or NotificationKinds.InvoiceReminderDue or NotificationKinds.InvoiceOverdue
                or NotificationKinds.InvoiceDraftWaiting
                => Subject(Invoice, "invoice", subjectType, subjectId),

            _ => null,
        };

        return target ?? Notifications;
    }

    private static PushTarget? ProjectPart(long? projectId, string? domain, string? section)
        => projectId is { } id && domain is not null ? new() { Type = Project, Id = id, Domain = domain, Section = section } : null;

    private static PushTarget? Subject(string type, string expectedSubject, string? subjectType, long? subjectId)
        => subjectType == expectedSubject && subjectId is { } id ? new() { Type = type, Id = id } : null;

    private static DateOnly? WeekStartOf(JsonElement? payload)
        => payload is { ValueKind: JsonValueKind.Object } p
            && p.TryGetProperty("weekStart", out var value)
            && value.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : null;
}
