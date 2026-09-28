using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Common.Implementation;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class SystemClockTests
{
    private readonly SystemClock _clock = new(Options.Create(new CompanyOptions { TimeZone = "Asia/Beirut" }));

    [Fact]
    public void A_company_day_starts_at_local_midnight()
    {
        // Beirut is UTC+3 in summer and UTC+2 in winter.
        _clock.CompanyDayStartUtc(new DateOnly(2026, 9, 28)).ShouldBe(new DateTime(2026, 9, 27, 21, 0, 0, DateTimeKind.Utc));
        _clock.CompanyDayStartUtc(new DateOnly(2026, 12, 1)).ShouldBe(new DateTime(2026, 11, 30, 22, 0, 0, DateTimeKind.Utc));
        _clock.ToCompanyTime(new DateTime(2026, 9, 28, 6, 0, 0, DateTimeKind.Utc)).Hour.ShouldBe(9);
    }

    [Fact]
    public void The_day_clocks_go_forward_at_midnight_starts_at_one()
    {
        var start = _clock.CompanyDayStartUtc(new DateOnly(2026, 3, 29));

        _clock.ToCompanyDate(start).ShouldBe(new DateOnly(2026, 3, 29));
        _clock.ToCompanyDate(start.AddMinutes(-1)).ShouldBe(new DateOnly(2026, 3, 28));
    }
}
