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

    /// <summary>By section, then order. Removed items are included until the next publish.</summary>
    public required IReadOnlyList<PortalContentItemResponse> Items { get; init; }

    /// <summary>Moves on with every draft change; publishing and discarding send it back.</summary>
    public required int Revision { get; init; }

    public required PortalPublicationResponse Publication { get; init; }

    public required PortalChangesResponse Changes { get; init; }

    public required bool HasUnpublishedChanges { get; init; }

    public required bool CanPublish { get; init; }

    /// <summary>Only once something has been published: before that there is nothing to go back to.</summary>
    public required bool CanDiscard { get; init; }

    /// <summary>The draft hero image.</summary>
    public required PortalHeroResponse? Hero { get; init; }

    /// <summary>The draft introduction for the client.</summary>
    public required string? Summary { get; init; }
}

public sealed record PortalPublicationResponse(DateTime? PublishedAt, PersonRefResponse? PublishedBy);

/// <summary>What publishing now would change for clients.</summary>
public sealed record PortalChangesResponse(
    int Added, int Updated, int Removed, IReadOnlyList<PortalSection> Reordered, bool HeroChanged, bool SummaryChanged);

public sealed record PortalHeroResponse(long FileId, string Name, string? Url, DateTime? UrlExpiresAt);

/// <summary>Live files only: deleted files and unfinished uploads aren't counted. Stored bytes are what the store reports.</summary>
public sealed record PortalUsageResponse(int Files, long OriginalBytes, long StoredBytes);

public sealed record PortalContentItemResponse
{
    public required long Id { get; init; }

    public required PortalSection Section { get; init; }

    public required string Title { get; init; }

    public required int SortOrder { get; init; }

    /// <summary>True unless clients see exactly this (<see cref="State"/> unchanged).</summary>
    public required bool IsDraft { get; init; }

    public required PortalItemState State { get; init; }

    /// <summary>Its file is the draft hero.</summary>
    public required bool IsHero { get; init; }

    /// <summary>A JPEG, PNG or WebP image, which can be the hero.</summary>
    public required bool CanBeHero { get; init; }

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

/// <summary><see cref="Item"/> is null for a hero image, which belongs to no section.</summary>
public sealed record PortalUploadCompletedResponse(PortalContentItemResponse? Item, PortalUsageResponse Usage);

public sealed record PortalUsageChangedResponse(PortalUsageResponse Usage);

/// <summary>A short-lived link to open or download one file.</summary>
public sealed record PortalContentLinkResponse(string Url, DateTime ExpiresAt);
