using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Portal;

/// <summary>What the content rules need to know about a project.</summary>
public sealed record PortalContentProject(long Id, string DomainCode, string Title, ProjectStatus Status, BillingType BillingType, long? ClientId);

/// <summary>
/// A Design project's files as one staff member works on them: the project, and whether they hold
/// project_files:manage (<see cref="ManagesFiles"/>) and client_portal:manage (<see cref="ManagesPortal"/>).
/// </summary>
public sealed record ProjectFilesAccess(PortalContentProject Project, long UserId, bool ManagesFiles, bool ManagesPortal);

/// <summary>A project's live files: how many, and their original and stored sizes in bytes.</summary>
public sealed record PortalUsage(int Files, long OriginalBytes, long StoredBytes);

/// <summary>A file as the content list shows it, with the uploader's name.</summary>
public sealed record PortalFileView(
    long Id, string StorageKey, string OriginalName, string ContentType, long OriginalSizeBytes, long StoredSizeBytes,
    DateTime CreatedAt, long UploadedById, string? UploadedByName);

/// <summary>An item as the content list shows it, with its file, who changed it last, the client choice and its published copy.</summary>
public sealed record PortalItemView(
    long Id, PortalSection Section, string Title, int SortOrder, DateTime CreatedAt, DateTime UpdatedAt,
    long UpdatedById, string? UpdatedByName, PortalFileView File, bool ForClient,
    long? PublishedFileId, string? PublishedTitle, PortalSection? PublishedSection, int? PublishedSortOrder, DateTime? RemovedAt)
{
    public PortalItemCopies Copies => new(
        Id, File.Id, Title, Section, SortOrder, PublishedFileId, PublishedTitle, PublishedSection, PublishedSortOrder, RemovedAt is not null, ForClient);
}

/// <summary>A project's introduction, revision and last publication; empty values before anything changed.</summary>
public sealed record PortalSettingsView(
    string? Summary, string? PublishedSummary, int Revision, DateTime? PublishedAt, long? PublishedById, string? PublishedByName)
{
    public static readonly PortalSettingsView Empty = new(null, null, 0, null, null, null);
}

/// <summary>The files of a project's hero images: the draft list and the published one, each in order.</summary>
public sealed record PortalHeroes(IReadOnlyList<long> Draft, IReadOnlyList<long> Published);

/// <summary>One file as a presentation shows it: no uploader, no storage details beyond what a link needs.</summary>
public sealed record PresentationRow(long ItemId, PortalSection Section, string Title, PortalFileView File);

/// <summary>A comment on a project file, with its author's and fixer's names.</summary>
public sealed record ProjectFileCommentView(
    long Id, long ItemId, long AuthorId, string AuthorName, string Body, DateTime CreatedAt, DateTime? EditedAt,
    DateTime? FixedAt, long? FixedById, string? FixedByName);

/// <summary>How many comments a file has, and how many of them aren't fixed.</summary>
public sealed record ProjectFileCommentCounts(int Total, int Open)
{
    public static readonly ProjectFileCommentCounts None = new(0, 0);
}
