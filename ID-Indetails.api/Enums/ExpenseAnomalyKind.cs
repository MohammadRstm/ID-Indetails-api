namespace ID_Indetails.api.Enums;

/// <summary>Why spending was called unusual: only these rules fired.</summary>
public enum ExpenseAnomalyKind
{
    /// <summary>A category's month far above its average of the months before.</summary>
    CategoryHigh,

    /// <summary>An item bought at a unit cost far above its last purchases.</summary>
    UnitCostJump,

    /// <summary>Expenses on one project with the same item, amount and currency, close together: maybe one typed twice.</summary>
    SameAmountTwice,
}
