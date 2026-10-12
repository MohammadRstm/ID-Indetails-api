namespace ID_Indetails.api.Services.Common.Abstraction;

/// <summary>The current time, injectable so tests can control it.</summary>
public interface IClock
{
    DateTime UtcNow { get; }

    /// <summary>Today's date in the company time zone (Asia/Beirut by default).</summary>
    DateOnly CompanyToday { get; }

    /// <summary>The calendar day an instant falls on in the company time zone.</summary>
    DateOnly ToCompanyDate(DateTime utc);

    /// <summary>An instant as a local date and time in the company time zone.</summary>
    DateTime ToCompanyTime(DateTime utc);

    /// <summary>The instant a company-time-zone day starts, in UTC.</summary>
    DateTime CompanyDayStartUtc(DateOnly day);

    /// <summary>
    /// A time of day on a company-time-zone day, in UTC: <paramref name="minutes"/> after local midnight, as a clock
    /// on the wall shows it. On the day the clocks change, a time that doesn't exist moves forward to the first that does.
    /// </summary>
    DateTime FromCompanyTime(DateOnly day, int minutes);
}
