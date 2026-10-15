using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Portal;

/// <summary>What the content rules need to know about a project.</summary>
public sealed record PortalContentProject(long Id, string DomainCode, string Title, ProjectStatus Status, BillingType BillingType, long? ClientId);

/// <summary>A project's live client portal files: how many, and their original and stored sizes in bytes.</summary>
public sealed record PortalUsage(int Files, long OriginalBytes, long StoredBytes);

/// <summary>A file as the content list shows it, with the uploader's name.</summary>
public sealed record PortalFileView(
    long Id, string StorageKey, string OriginalName, string ContentType, long OriginalSizeBytes, long StoredSizeBytes,
    DateTime CreatedAt, long UploadedById, string? UploadedByName);

/// <summary>An item as the content list shows it, with its file, who changed it last and its published copy.</summary>
public sealed record PortalItemView(
    long Id, PortalSection Section, string Title, int SortOrder, DateTime CreatedAt, DateTime UpdatedAt,
    long UpdatedById, string? UpdatedByName, PortalFileView File,
    long? PublishedFileId, string? PublishedTitle, PortalSection? PublishedSection, int? PublishedSortOrder, DateTime? RemovedAt)
{
    public PortalItemCopies Copies => new(
        Id, File.Id, Title, Section, SortOrder, PublishedFileId, PublishedTitle, PublishedSection, PublishedSortOrder, RemovedAt is not null);
}

/// <summary>A project's hero, introduction, revision and last publication; empty values before anything changed.</summary>
public sealed record PortalSettingsView(
    long? HeroFileId, long? PublishedHeroFileId, string? Summary, string? PublishedSummary, int Revision,
    DateTime? PublishedAt, long? PublishedById, string? PublishedByName)
{
    public static readonly PortalSettingsView Empty = new(null, null, null, null, 0, null, null, null);
}

/// <summary>One file as a presentation shows it: no uploader, no storage details beyond what a link needs.</summary>
public sealed record PresentationRow(long ItemId, PortalSection Section, string Title, PortalFileView File);
