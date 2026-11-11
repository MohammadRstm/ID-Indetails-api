namespace ID_Indetails.api.Enums;

/// <summary>Where a client project's profit stands against the profit floor.</summary>
public enum ProfitStanding
{
    Fine,
    /// <summary>A margin of 10% or less.</summary>
    Close,
    /// <summary>Profit at zero or below.</summary>
    AtZero,
}
