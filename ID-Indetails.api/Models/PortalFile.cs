namespace ID_Indetails.api.Models;

/// <summary>
/// A file in the content store, as one project's client portal content. Kept apart from <see cref="StoredFile"/>:
/// these live in the content store, not on the server's disk, and are never served through <c>/api/files</c>.
/// </summary>
public class PortalFile
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public string OriginalName { get; set; } = string.Empty;

    /// <summary>Decided by the server from the extension and checked against the first bytes; never the browser's.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>The size of the file the person picked, in bytes.</summary>
    public long OriginalSizeBytes { get; set; }

    /// <summary>The size the store reports for the object, measured when the upload completed. Equal to the original while nothing is compressed.</summary>
    public long StoredSizeBytes { get; set; }

    public long UploadedById { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Set when the file is no longer used; the cleanup then deletes the object, and the row once it's gone.</summary>
    public DateTime? DeletedAt { get; set; }
}
