namespace ID_Indetails.api.Enums;

/// <summary>A step of a project's profitability bridge, from its base price to its agreed profit.</summary>
public enum BridgeStepKind
{
    /// <summary>The price first agreed: Design's payment, a site's client BOQ.</summary>
    BasePrice,
    VariationOrders,
    /// <summary>What was excused or written off at completion (negative).</summary>
    Settlement,
    /// <summary>An overpayment kept at completion.</summary>
    PaidAhead,
    Supervision,
    Permits,
    Labour,
    Expenses,
    SiteExpenses,
    GroundLabour,
    /// <summary>Pocket money paid in less what was moved to it.</summary>
    Pocket,
}
