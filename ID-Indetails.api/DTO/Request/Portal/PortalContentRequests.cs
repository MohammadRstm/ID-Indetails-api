using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Portal;

/// <summary>Starts sending one file to a section, or to replace an item's file.</summary>
public sealed record StartPortalUploadRequest
{
    /// <summary>Made by the browser for each file (a UUID), so a repeated request gives the same upload.</summary>
    public string ClientToken { get; init; } = string.Empty;

    /// <summary>Required, except for a hero image.</summary>
    public PortalSection? Section { get; init; }

    /// <summary>A hero image for the presentation: no section, and JPEG, PNG or WebP only.</summary>
    public bool Hero { get; init; }

    public string FileName { get; init; } = string.Empty;

    /// <summary>The file's size in bytes.</summary>
    public long Size { get; init; }

    public long? ReplaceItemId { get; init; }
}

/// <summary>Asks for the links to send these parts to.</summary>
public sealed record PortalPartLinksRequest
{
    public IReadOnlyList<int> PartNumbers { get; init; } = [];
}

/// <summary>A section's new order: every one of its current (not removed) items, once.</summary>
public sealed record PortalOrderRequest
{
    public IReadOnlyList<long> ItemIds { get; init; } = [];
}

/// <summary>Adds an image item's file to the end of the draft hero images.</summary>
public sealed record PortalHeroRequest
{
    public long ItemId { get; init; }
}

/// <summary>The draft hero images' new order: every one of their files, once.</summary>
public sealed record PortalHeroOrderRequest
{
    public IReadOnlyList<long> FileIds { get; init; } = [];
}

/// <summary>The draft introduction for the client; empty clears it.</summary>
public sealed record PortalSummaryRequest
{
    public string? Summary { get; init; }
}

/// <summary>Publishing or discarding what the person saw: the revision their page showed.</summary>
public sealed record PortalRevisionRequest
{
    public int ExpectedRevision { get; init; }
}

/// <summary>Renames an item, or moves it to another section (at the end there).</summary>
public sealed record UpdatePortalItemRequest
{
    public string? Title { get; init; }

    public PortalSection? Section { get; init; }
}
