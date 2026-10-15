using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Portal;

/// <summary>A project's client portal content, every section, as staff manage it. Everything is a draft for now.</summary>
public sealed record PortalContentResponse
{
    public required long ProjectId { get; init; }

    /// <summary>False when no content store is configured: nothing can be uploaded or opened.</summary>
    public required bool StorageReady { get; init; }

    /// <summary>False when the content can't change now (an archived project); it can still be viewed and downloaded.</summary>
    public required bool CanEdit { get; init; }

    public required string? ReadOnlyReason { get; init; }

    public required PortalUsageResponse Usage { get; init; }

    /// <summary>The extensions the client portal takes, lower case and without the dot.</summary>
    public required IReadOnlyList<string> AcceptedExtensions { get; init; }

    /// <summary>By section, then order.</summary>
    public required IReadOnlyList<PortalContentItemResponse> Items { get; init; }
}

/// <summary>Live files only: deleted files and unfinished uploads aren't counted. Stored bytes are what the store reports.</summary>
public sealed record PortalUsageResponse(int Files, long OriginalBytes, long StoredBytes);

public sealed record PortalContentItemResponse
{
    public required long Id { get; init; }

    public required PortalSection Section { get; init; }

    public required string Title { get; init; }

    public required int SortOrder { get; init; }

    /// <summary>Always true until publishing exists: no client can see the item.</summary>
    public required bool IsDraft { get; init; }

    public required PortalContentFileResponse File { get; init; }

    /// <summary>A short-lived inline link for images and videos; null for anything else.</summary>
    public required string? PreviewUrl { get; init; }

    public required DateTime? PreviewUrlExpiresAt { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime UpdatedAt { get; init; }

    public required PersonRefResponse? UpdatedBy { get; init; }
}

public sealed record PortalContentFileResponse
{
    public required long Id { get; init; }

    public required string Name { get; init; }

    public required string ContentType { get; init; }

    public required PortalPreview Preview { get; init; }

    public required long OriginalBytes { get; init; }

    public required long StoredBytes { get; init; }

    public required DateTime UploadedAt { get; init; }

    public required PersonRefResponse? UploadedBy { get; init; }
}

/// <summary>An upload is ready: send <see cref="PartCount"/> parts of <see cref="PartSize"/> bytes (the last holds the rest).</summary>
public sealed record PortalUploadStartedResponse(long UploadId, long PartSize, int PartCount);

public sealed record PortalPartLinksResponse(IReadOnlyList<PortalPartLinkResponse> Parts, DateTime ExpiresAt);

/// <summary>PUT the part's raw bytes to <see cref="Url"/>, with no other header.</summary>
public sealed record PortalPartLinkResponse(int PartNumber, string Url);

public sealed record PortalUploadCompletedResponse(PortalContentItemResponse Item, PortalUsageResponse Usage);

public sealed record PortalUsageChangedResponse(PortalUsageResponse Usage);

/// <summary>A short-lived link to open or download one file.</summary>
public sealed record PortalContentLinkResponse(string Url, DateTime ExpiresAt);
