namespace ID_Indetails.api.Enums;

/// <summary>What a bill of quantities is for.</summary>
public enum BoqKind
{
    /// <summary>An admin's private working draft. Never sent, never numbered.</summary>
    Internal,

    /// <summary>The BOQ the client is sent and agrees to: the price of the work.</summary>
    Client,

    /// <summary>A variation order: a later change of price, after the client BOQ is agreed. It can lower the price.</summary>
    Vo,
}

/// <summary>Where a bill of quantities stands.</summary>
public enum BoqStatus
{
    Draft,
    Agreed,
}

/// <summary>How a whole BOQ's work is adjusted from the sum of its work lines. Permits are never adjusted.</summary>
public enum BoqAdjustmentType
{
    None,

    /// <summary>The value is a fraction of the work subtotal: -0.05 takes 5% off.</summary>
    Percent,

    /// <summary>The value is the BOQ's new work total.</summary>
    Total,
}

/// <summary>What a BOQ line charges the client for.</summary>
public enum BoqLineNature
{
    /// <summary>Work the company does and supervises: supervision and TVA are added on it.</summary>
    Work,

    /// <summary>A permit, charged at its line price with no supervision and no TVA. Its real cost goes in the expenses.</summary>
    Permit,
}
