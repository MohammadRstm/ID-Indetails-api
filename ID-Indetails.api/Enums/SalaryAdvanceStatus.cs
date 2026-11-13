namespace ID_Indetails.api.Enums;

/// <summary>Where a salary advance stands (see <c>Business/Payroll/AdvanceRules</c>).</summary>
public enum SalaryAdvanceStatus
{
    /// <summary>Asked for by the person, waiting for a super admin.</summary>
    Pending,
    /// <summary>Given: approved, or recorded directly by a super admin. It is paid back from the months set for it.</summary>
    Approved,
    /// <summary>A super admin said no.</summary>
    Declined,
    /// <summary>The person took it back while it was pending.</summary>
    Withdrawn,
    /// <summary>Given by mistake and cancelled while its month was still open: nothing is paid back.</summary>
    Cancelled,
}
