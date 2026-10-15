using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Business.Portal;

/// <summary>What the content rules need to know about a project.</summary>
public sealed record PortalContentProject(long Id, string DomainCode, ProjectStatus Status, BillingType BillingType, long? ClientId);

/// <summary>A project's live client portal files: how many, and their original and stored sizes in bytes.</summary>
public sealed record PortalUsage(int Files, long OriginalBytes, long StoredBytes);

/// <summary>A file as the content list shows it, with the uploader's name.</summary>
public sealed record PortalFileView(
    long Id, string StorageKey, string OriginalName, string ContentType, long OriginalSizeBytes, long StoredSizeBytes,
    DateTime CreatedAt, long UploadedById, string? UploadedByName);

/// <summary>An item as the content list shows it, with its file and who changed it last.</summary>
public sealed record PortalItemView(
    long Id, PortalSection Section, string Title, int SortOrder, DateTime CreatedAt, DateTime UpdatedAt,
    long UpdatedById, string? UpdatedByName, PortalFileView File);
