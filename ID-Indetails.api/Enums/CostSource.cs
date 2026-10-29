namespace ID_Indetails.api.Enums;

/// <summary>Where a project cost comes from.</summary>
public enum CostSource
{
    Labour,
    Expenses,
    /// <summary>A cost recorded on an Execution site: materials, a contractor, equipment, fees.</summary>
    SiteExpense,
    GroundLabour,
}
