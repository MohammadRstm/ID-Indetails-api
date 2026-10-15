using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Portal;

/// <summary>A Design project's files, every section, as its Project files tab shows them.</summary>
public sealed record ProjectFilesResponse
{
    public required long ProjectId { get; init; }

    /// <summary>False when no content store is configured: nothing can be uploaded or opened.</summary>
    public required bool StorageReady { get; init; }

    /// <summary>False when the files can't change now (an archived project); they can still be viewed and downloaded.</summary>
    public required bool CanEdit { get; init; }

    public required string? ReadOnlyReason { get; init; }

    /// <summary>The caller holds project_files:manage: they change anyone's files, reorder sections and start comments.</summary>
    public required bool CanManage { get; init; }

    /// <summary>The caller may choose files for the client: client_portal:manage, on a client project that isn't archived.</summary>
    public required bool CanChooseForClient { get; init; }

    public required bool IsClientProject { get; init; }

    public required PortalUsageResponse Usage { get; init; }

    /// <summary>The extensions taken, lower case and without the dot.</summary>
    public required IReadOnlyList<string> AcceptedExtensions { get; init; }

    /// <summary>By section, then order. Files deleted but still published are included (<see cref="ProjectFileResponse.Removed"/>) until the next publish.</summary>
    public required IReadOnlyList<ProjectFileResponse> Items { get; init; }
}

public sealed record ProjectFileResponse
{
    public required long Id { get; init; }

    public required PortalSection Section { get; init; }

    public required string Title { get; init; }

    public required int SortOrder { get; init; }

    /// <summary>Chosen for the client: the next publish shows it to them.</summary>
    public required bool ForClient { get; init; }

    /// <summary>Deleted, but clients see it until the next publish; it can be restored until then.</summary>
    public required bool Removed { get; init; }

    /// <summary>Its file is one of the client portal's draft hero images.</summary>
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

    /// <summary>Who uploaded the current file: they may change the file.</summary>
    public required PersonRefResponse? UploadedBy { get; init; }

    /// <summary>The caller may rename, move, replace or delete it now (or restore it, once removed).</summary>
    public required bool CanEdit { get; init; }

    public required ProjectFileCommentCountsResponse Comments { get; init; }

    /// <summary>Compared with what the client sees; null when the file isn't for the client and never reached them.</summary>
    public required PortalItemState? ClientState { get; init; }
}

/// <summary>How many comments a file has, and how many are still open (not fixed).</summary>
public sealed record ProjectFileCommentCountsResponse(int Total, int Open);

public sealed record ProjectFileCommentResponse
{
    public required long Id { get; init; }

    public required long ItemId { get; init; }

    public required PersonRefResponse Author { get; init; }

    public required string Body { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime? EditedAt { get; init; }

    public required DateTime? FixedAt { get; init; }

    public required PersonRefResponse? FixedBy { get; init; }

    /// <summary>The caller wrote it, and the project isn't archived.</summary>
    public required bool CanEdit { get; init; }

    public required bool CanDelete { get; init; }

    /// <summary>The caller uploaded the file or manages project files, and the project isn't archived.</summary>
    public required bool CanFix { get; init; }
}

/// <summary><see cref="Item"/> is null for a hero image, which belongs to no section.</summary>
public sealed record ProjectFileUploadCompletedResponse(ProjectFileResponse? Item, PortalUsageResponse Usage);
