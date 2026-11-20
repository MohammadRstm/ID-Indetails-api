namespace ID_Indetails.IntegrationTests.Api;

/// <summary>
/// Salaries for tests that need an hour of pay worth a known amount. An hour of a salary is the salary ÷ the month's
/// paid hours, 8 a working day from Monday to Friday on the default working time.
/// </summary>
public static class TestPay
{
    private static readonly TimeZoneInfo Beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");

    public static int PaidHours(DateOnly month)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        return 8 * Enumerable.Range(0, DateTime.DaysInMonth(month.Year, month.Month))
            .Count(d => first.AddDays(d).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));
    }

    /// <summary>The salary an hour of which is worth <paramref name="hourly"/> in <paramref name="month"/>.</summary>
    public static decimal SalaryAt(decimal hourly, DateOnly month) => hourly * PaidHours(month);

    /// <summary>An hour of <paramref name="salary"/> in <paramref name="month"/>, to 4 decimal places as the API keeps it.</summary>
    public static decimal HourOf(decimal salary, DateOnly month)
        => Math.Round(salary / PaidHours(month), 4, MidpointRounding.AwayFromZero);

    /// <summary>The day in the company time zone of a moment in UTC.</summary>
    public static DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Beirut));
}
