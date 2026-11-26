namespace ID_Indetails.api.Enums;

/// <summary>
/// What a refused change was: one the profit floor refused (a pay change among them, <see cref="PayChange"/>), hours
/// logged by someone whose department isn't on the project (<see cref="WorkEntryDepartment"/>), kept until it is added,
/// or hours someone logged on a day of their own time off (<see cref="WorkEntryTimeOff"/>).
/// </summary>
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
    /// <summary>Hours logged by someone whose department isn't on the project: they go through once it is added.</summary>
    WorkEntryDepartment,
    /// <summary>Applying a Design variation order that lowers the price.</summary>
    DesignVo,

    /// <summary>
    /// A pay change whose re-pricing would take a running client project's profit to zero or below. Only super admins
    /// see, force or dismiss it.
    /// </summary>
    PayChange,

    /// <summary>
    /// Hours someone logged on a day of their own time off, or moved onto one: a super admin approves them as overtime or
    /// as normal hours, or turns them down. The payload is the log or edit as it was sent.
    /// </summary>
    WorkEntryTimeOff,
}

/// <summary>Where a refused change stands.</summary>
public enum BlockedChangeStatus
{
    Pending,
    Forced,
    Dismissed,
}
