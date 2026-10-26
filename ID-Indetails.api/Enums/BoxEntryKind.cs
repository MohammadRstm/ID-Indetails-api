namespace ID_Indetails.api.Enums;

/// <summary>Why money went into or out of the Execution box.</summary>
public enum BoxEntryKind
{
    /// <summary>A client payment of a project in the box.</summary>
    PaymentIn,

    /// <summary>That payment voided: the box gives it back.</summary>
    PaymentVoided,

    /// <summary>Moved to a project in the red.</summary>
    Transfer,

    /// <summary>A closed project's revenue taken out of the box.</summary>
    Removed,

    /// <summary>A reopened project's earlier revenue put back.</summary>
    Readded,
}
