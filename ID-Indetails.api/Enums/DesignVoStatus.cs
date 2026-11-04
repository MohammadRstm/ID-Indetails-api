namespace ID_Indetails.api.Enums;

/// <summary>Where a Design variation order stands.</summary>
public enum DesignVoStatus
{
    /// <summary>Sent to the client for approval: the price changes once it is confirmed.</summary>
    Pending,
    /// <summary>Part of the price.</summary>
    Applied,
    /// <summary>Taken back before it was confirmed; its number isn't used again.</summary>
    Cancelled,
}
