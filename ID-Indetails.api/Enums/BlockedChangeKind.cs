namespace ID_Indetails.api.Enums;

/// <summary>What a change refused by the profit floor was.</summary>
public enum BlockedChangeKind
{
    Expense,

    /// <summary>Adding site expenses; the stored value is still <c>material_add</c>.</summary>
    MaterialAdd,

    /// <summary>Changing a site expense; the stored value is still <c>material_update</c>.</summary>
    MaterialUpdate,

    LabourWeek,
    WorkEntryCreate,
    WorkEntryUpdate,
    Payment,
    Pricing,
    BoqAgree,
    BoqWithdraw,
}

/// <summary>Where a refused change stands.</summary>
public enum BlockedChangeStatus
{
    Pending,
    Forced,
    Dismissed,
}
