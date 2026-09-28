namespace ID_Indetails.api.Services.Common.Abstraction;

/// <summary>The current time, injectable so tests can control it.</summary>
public interface IClock
{
    DateTime UtcNow { get; }

    /// <summary>Today's date in the company time zone (Asia/Beirut by default).</summary>
    DateOnly CompanyToday { get; }

    /// <summary>The calendar day an instant falls on in the company time zone.</summary>
    DateOnly ToCompanyDate(DateTime utc);
}
