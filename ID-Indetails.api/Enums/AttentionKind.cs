namespace ID_Indetails.api.Enums;

/// <summary>What the Design dashboard flags as needing attention.</summary>
public enum AttentionKind
{
    /// <summary>An active project with no work for 7 days or more.</summary>
    Idle,

    /// <summary>An active project at 90% or more.</summary>
    Closeout,

    OnHold,

    /// <summary>People of the domain with no work logged this week.</summary>
    NotLogging,
}
