namespace ID_Indetails.api.Enums;

/// <summary>The span the Design dashboard covers: quarter or month to date, the last 30 days, or days the caller picks.</summary>
public enum OverviewPeriod
{
    Quarter,
    Month,
    Last30,

    /// <summary>The days the caller picks (from and to).</summary>
    Custom,
}
