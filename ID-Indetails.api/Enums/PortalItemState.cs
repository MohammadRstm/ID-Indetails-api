namespace ID_Indetails.api.Enums;

/// <summary>A client-relevant item's draft compared with what clients see.</summary>
public enum PortalItemState
{
    /// <summary>Chosen for the client and never published: clients don't see it yet.</summary>
    New,

    /// <summary>Its file, title or section differs from what clients see.</summary>
    Changed,

    Unchanged,

    /// <summary>Deleted, or no longer chosen for the client; clients see it until the next publish.</summary>
    Removed,
}
