namespace ID_Indetails.api.Enums;

/// <summary>What a change refused by the profit floor was.</summary>
public enum BlockedChangeKind
{
    Expense,
    MaterialAdd,
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
