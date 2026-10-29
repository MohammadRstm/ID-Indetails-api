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

/// <summary>How a whole BOQ is adjusted from the sum of its lines.</summary>
public enum BoqAdjustmentType
{
    None,

    /// <summary>The value is a fraction of the subtotal: -0.05 takes 5% off.</summary>
    Percent,

    /// <summary>The value is the BOQ's new total.</summary>
    Total,
}
