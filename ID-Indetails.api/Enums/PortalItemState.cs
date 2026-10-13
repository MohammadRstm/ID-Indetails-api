namespace ID_Indetails.api.Enums;

/// <summary>An item's draft compared with what clients see.</summary>
public enum PortalItemState
{
    /// <summary>Never published: clients don't see it yet.</summary>
    New,

    /// <summary>Its file, title or section differs from what clients see.</summary>
    Changed,

    Unchanged,

    /// <summary>Deleted in the draft; clients see it until the next publish.</summary>
    Removed,
}
