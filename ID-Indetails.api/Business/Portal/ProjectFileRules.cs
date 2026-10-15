using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Portal;

/// <summary>
/// <list type="bullet">
/// <item>Every Design project has files (its Project files), client or internal. Anyone who can open the project sees
/// them and adds more.</item>
/// <item>A file is changed (renamed, moved, replaced, deleted, restored, its comments marked fixed) by whoever uploaded
/// its current file, or by someone holding project_files:manage, who also orders the sections.</item>
/// <item>Managers start a file's comments; anyone may reply once there are some. Comments are plain text, up to 2,000
/// characters, and only their author edits or deletes them.</item>
/// <item>Only someone holding client_portal:manage chooses a file for the client, on a client project.</item>
/// <item>An archived project's files are read-only.</item>
/// </list>
/// </summary>
public static class ProjectFileRules
{
    public const int MaxCommentLength = 2000;

    public static bool CanEdit(ProjectFilesAccess caller, long uploaderId) => caller.ManagesFiles || caller.UserId == uploaderId;

    public static bool CanComment(ProjectFilesAccess caller, bool hasComments) => caller.ManagesFiles || hasComments;

    public static RuleDenial? CheckEditable(ProjectStatus status)
        => status == ProjectStatus.Archived
            ? RuleDenial.BusinessRule(ErrorCodes.PortalContentReadOnly, "This project is archived, so its files can't change. Reopen it to make changes.")
            : null;

    /// <summary>
    /// Whether the caller may decide what the client sees from the project's files (choose a file, upload a hero):
    /// client_portal:manage, on a client project that isn't archived.
    /// </summary>
    public static RuleDenial? CheckClientPortal(ProjectFilesAccess caller)
        => caller.ManagesPortal
            ? PortalContentRules.CheckEditable(caller.Project.BillingType, caller.Project.ClientId, caller.Project.Status)
            : RuleDenial.Forbidden(ErrorCodes.Forbidden, "You don't have permission to choose what the client sees.");

    /// <summary>Who hears of a new comment: the file's uploader and everyone who commented on it before, never its author.</summary>
    public static IReadOnlyList<long> CommentRecipients(long uploaderId, IEnumerable<long> earlierAuthors, long authorId)
        => earlierAuthors.Prepend(uploaderId).Where(id => id != authorId).Distinct().ToList();

    /// <summary>A comment as kept: trimmed, with one kind of line break.</summary>
    public static string Clean(string body) => body.Trim().ReplaceLineEndings("\n");
}
