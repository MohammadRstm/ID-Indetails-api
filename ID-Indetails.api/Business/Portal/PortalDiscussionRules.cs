using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Portal;

/// <summary>Who wrote or is mentioned: a staff member or a client.</summary>
public enum DiscussionSide
{
    Staff,
    Client,
}

/// <summary>Someone in a project's discussion. The key ("u12" for staff, "c7" for clients) is what mentions name.</summary>
public sealed record DiscussionPerson(DiscussionSide Side, long Id, string Name)
{
    public string Key => PortalDiscussionRules.KeyOf(Side, Id);
}

/// <summary>What the discussion needs to know about a project.</summary>
public sealed record DiscussionProject(
    long Id, string DomainCode, string Title, ProjectStatus Status, BillingType BillingType, long? ClientId, long OwnerId);

/// <summary>
/// <list type="bullet">
/// <item>A Design client project's discussion: a chat for the whole project and comment threads on its published files.</item>
/// <item>Staff take part when they are active and either a super admin, or the project's manager, the uploader of one
/// of its files chosen for the client or published, or someone who logged work on it, while they can see its domain
/// (someone moved to the other domain is out). Clients take part when their account is active with live access.
/// Worked out on every request.</item>
/// <item>Plain text only: a chat message up to 4,000 characters, a comment up to 2,000.</item>
/// <item>An archived project's discussion can be read, not written.</item>
/// <item>Only people taking part can be mentioned; mentioning yourself does nothing.</item>
/// </list>
/// </summary>
public static class PortalDiscussionRules
{
    public const int MaxMessageLength = 4000;
    public const int MaxCommentLength = 2000;
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 50;

    public static string KeyOf(DiscussionSide side, long id) => (side == DiscussionSide.Staff ? "u" : "c") + id;

    /// <summary>Whether the project has a discussion at all: a Design project for a client.</summary>
    public static bool HasDiscussion(DiscussionProject project)
        => project.DomainCode == DomainCodes.Design && project.BillingType == BillingType.Client && project.ClientId is not null;

    public static RuleDenial? CheckWritable(ProjectStatus status)
        => status == ProjectStatus.Archived
            ? RuleDenial.BusinessRule(
                ErrorCodes.PortalDiscussionClosed, "This project is archived: its discussion can be read, but nothing new can be written.")
            : null;

    /// <summary>The text as kept: trimmed, with one kind of line break.</summary>
    public static string Clean(string body) => body.Trim().ReplaceLineEndings("\n");

    /// <summary>The page size asked for, within bounds.</summary>
    public static int PageSize(int? limit) => Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);

    /// <summary>
    /// The people the keys name, each once, without the writer. Null when a key isn't someone taking part, so the
    /// request can be refused rather than mention a stranger.
    /// </summary>
    public static IReadOnlyList<DiscussionPerson>? Mentioned(
        IEnumerable<string> keys, IReadOnlyCollection<DiscussionPerson> participants, DiscussionPerson writer)
    {
        var byKey = participants.ToDictionary(p => p.Key);
        var mentioned = new List<DiscussionPerson>();
        foreach (var key in keys.Distinct())
        {
            if (!byKey.TryGetValue(key, out var person))
            {
                return null;
            }

            if (person.Key != writer.Key)
            {
                mentioned.Add(person);
            }
        }

        return mentioned;
    }

    /// <summary>A read marker only moves forward, and never past what exists.</summary>
    public static long? MoveMarker(long? current, long? asked, long? newest)
    {
        if (asked is null || newest is null)
        {
            return current;
        }

        var next = Math.Min(asked.Value, newest.Value);
        return current is { } at && at >= next ? current : next;
    }
}
