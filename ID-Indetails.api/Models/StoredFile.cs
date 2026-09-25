namespace ID_Indetails.api.Models;

/// <summary>
/// Metadata for an uploaded file. The bytes live in file storage, never in MySQL.
/// Named StoredFile to avoid clashing with <see cref="System.IO.File"/>.
/// </summary>
public class StoredFile
{
    public long Id { get; set; }

    /// <summary>Server-generated storage key. Never derived from the client's filename.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public string OriginalName { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public long? UploadedById { get; set; }

    public DateTime CreatedAt { get; set; }
}
