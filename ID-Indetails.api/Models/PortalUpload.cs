using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// One file on its way to the content store. The row is written before any bytes are sent, so every object in the
/// store is known here: if the upload is abandoned, or completing it fails half way, the cleanup still finds its key.
/// </summary>
public class PortalUpload
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    /// <summary>Null for a hero image, which belongs to no section.</summary>
    public PortalSection? Section { get; set; }

    /// <summary>A hero image for the project's presentation rather than a section's file.</summary>
    public bool IsHero { get; set; }

    /// <summary>The item whose file this upload replaces; null for a new item.</summary>
    public long? ReplaceItemId { get; set; }

    /// <summary>Made by the browser for each file, so starting the same upload twice gives the same row.</summary>
    public string ClientToken { get; set; } = string.Empty;

    /// <summary>Server-generated, never derived from the file name. The finished file keeps it.</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>The store's id for the multipart upload.</summary>
    public string StoreUploadId { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    /// <summary>The size the browser declared, in bytes; completing checks the stored object against it.</summary>
    public long SizeBytes { get; set; }

    public PortalUploadStatus Status { get; set; }

    public long CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Moves on with every request for the upload; a day without any makes it abandoned.</summary>
    public DateTime LastActivityAt { get; set; }
}
