using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Common.Implementation;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class CompanyTimeTests
{
    private static readonly SystemClock Beirut = new(Options.Create(new CompanyOptions { TimeZone = "Asia/Beirut" }));

    [Fact]
    public void A_wall_clock_time_is_the_company_time_zones()
    {
        // winter: UTC+2, summer: UTC+3
        Beirut.FromCompanyTime(new DateOnly(2026, 1, 15), 9 * 60 + 30).ShouldBe(new DateTime(2026, 1, 15, 7, 30, 0, DateTimeKind.Utc));
        Beirut.FromCompanyTime(new DateOnly(2026, 7, 15), 9 * 60 + 30).ShouldBe(new DateTime(2026, 7, 15, 6, 30, 0, DateTimeKind.Utc));
        Beirut.FromCompanyTime(new DateOnly(2026, 7, 15), 0).ShouldBe(new DateTime(2026, 7, 14, 21, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>Beirut moves its clocks at midnight, so CompanyDayStartUtc(day).AddMinutes(m) is an hour off on those days.</summary>
    [Fact]
    public void On_the_day_the_clocks_change_the_time_still_reads_as_on_the_wall()
    {
        var spring = TransitionDay(forward: true);
        // 10:00 on that day is 10:00 summer time, 07:00 UTC
        Beirut.FromCompanyTime(spring, 10 * 60).ShouldBe(spring.ToDateTime(new TimeOnly(7, 0), DateTimeKind.Utc));
        // midnight doesn't exist that day: the first valid time is 01:00
        Beirut.FromCompanyTime(spring, 0).ShouldBe(Beirut.CompanyDayStartUtc(spring));

        var autumn = TransitionDay(forward: false);
        Beirut.FromCompanyTime(autumn, 10 * 60).ShouldBe(autumn.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc));
    }

    private static DateOnly TransitionDay(bool forward)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");
        for (var day = new DateOnly(2026, 1, 1); day.Year == 2026; day = day.AddDays(1))
        {
            var before = zone.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue).AddHours(-12));
            var after = zone.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue).AddHours(12));
            if (forward ? after > before : after < before)
            {
                return day;
            }
        }

        throw new InvalidOperationException("No clock change in 2026.");
    }
}
