using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Portal;

/// <summary>Starts sending one file to a section, or to replace an item's file.</summary>
public sealed record StartPortalUploadRequest
{
    /// <summary>Made by the browser for each file (a UUID), so a repeated request gives the same upload.</summary>
    public string ClientToken { get; init; } = string.Empty;

    public PortalSection? Section { get; init; }

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

/// <summary>Renames an item, or moves it to another section (at the end there).</summary>
public sealed record UpdatePortalItemRequest
{
    public string? Title { get; init; }

    public PortalSection? Section { get; init; }
}
