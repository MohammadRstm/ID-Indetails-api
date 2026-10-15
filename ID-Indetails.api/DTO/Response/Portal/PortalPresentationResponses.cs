using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Portal;

/// <summary>
/// A project's presentation as a client sees it: the published copy on the client's page, the draft in the staff
/// preview. Both are built the same way, so the preview is what the client will get. Nothing about staff, uploaders
/// or storage is in it.
/// </summary>
public sealed record PortalPresentationResponse
{
    public required long ProjectId { get; init; }

    public required string Title { get; init; }

    public required ProjectStatus Status { get; init; }

    public required string? Summary { get; init; }

    /// <summary>The client's page: whether anything was ever published. The preview: always true.</summary>
    public required bool IsPublished { get; init; }

    public required DateTime? PublishedAt { get; init; }

    public required PresentationHeroResponse? Hero { get; init; }

    /// <summary>Always the three sections, in order: presentation, 3D, drawings.</summary>
    public required IReadOnlyList<PresentationSectionResponse> Sections { get; init; }
}

public sealed record PresentationHeroResponse(string Url, DateTime UrlExpiresAt, string Name);

public sealed record PresentationSectionResponse(PortalSection Section, IReadOnlyList<PresentationItemResponse> Items);

public sealed record PresentationItemResponse
{
    public required long Id { get; init; }

    public required string Title { get; init; }

    public required string Name { get; init; }

    public required string ContentType { get; init; }

    public required PortalPreview Preview { get; init; }

    public required long SizeBytes { get; init; }

    public required string? PreviewUrl { get; init; }

    public required DateTime? PreviewUrlExpiresAt { get; init; }
}
