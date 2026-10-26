namespace ID_Indetails.api.Enums;

/// <summary>Where an Execution project stands with the box. Design projects have none.</summary>
public enum BoxState
{
    /// <summary>Its payments go into the box: open, or closed and kept.</summary>
    In,

    /// <summary>Closed by someone who can't see money: its payments still go in until a decision on the Box page.</summary>
    Pending,

    /// <summary>Closed and taken out: its later payments stay out.</summary>
    Removed,
}
