namespace ID_Indetails.api.Enums;

/// <summary>What an uploaded file is, from its contents.</summary>
public enum FileKind
{
    Image,
    Video,
    Audio,

    /// <summary>A PDF, Word, Excel, PowerPoint or CSV file, for a work entry (24 November 2026).</summary>
    Document,
}
