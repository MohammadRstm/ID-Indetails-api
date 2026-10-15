namespace ID_Indetails.api.Enums;

/// <summary>Where an upload of client portal content stands.</summary>
public enum PortalUploadStatus
{
    /// <summary>Started: the browser is sending its parts to the store.</summary>
    Uploading,

    /// <summary>Joined, checked and added to the project.</summary>
    Completed,

    /// <summary>Stopped by the uploader, or its item was deleted first.</summary>
    Cancelled,

    /// <summary>The bytes weren't what the file's extension said; the object was removed.</summary>
    Rejected,

    /// <summary>Abandoned: nothing happened for a day, so the cleanup removed it.</summary>
    Expired,
}
