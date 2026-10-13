namespace ID_Indetails.api.Enums;

/// <summary>What the browser can show of a client portal file without downloading it.</summary>
public enum PortalPreview
{
    Image,
    Video,

    /// <summary>Opened in the browser's own PDF viewer, in a new tab.</summary>
    Pdf,

    None,
}
