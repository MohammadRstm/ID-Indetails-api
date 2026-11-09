namespace ID_Indetails.api.Enums;

/// <summary>Where a request for time off stands (see <c>Business/TimeOff/TimeOffRules</c>).</summary>
public enum TimeOffStatus
{
    /// <summary>Waiting for a super admin.</summary>
    Pending,
    /// <summary>Granted, with or without pay.</summary>
    Approved,
    /// <summary>A super admin said no.</summary>
    Declined,
    /// <summary>The employee took it back while it was pending.</summary>
    Withdrawn,
    /// <summary>Nobody decided before its first day.</summary>
    Expired,
    /// <summary>Pending when the employee left, moved out of Design or became an admin.</summary>
    Cancelled,
    /// <summary>Approved, then taken back by a super admin: the days before the rejection day stay time off.</summary>
    Rejected,
    /// <summary>Approved, then cut short because the employee left, moved out of Design or became an admin.</summary>
    Ended,
}
