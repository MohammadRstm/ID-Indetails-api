using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Common.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Common.Implementation;

public sealed class SystemClock(IOptions<CompanyOptions> company) : IClock
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(company.Value.TimeZone);

    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly CompanyToday => ToCompanyDate(UtcNow);

    public DateOnly ToCompanyDate(DateTime utc) => DateOnly.FromDateTime(ToCompanyTime(utc));

    public DateTime ToCompanyTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

    public DateTime CompanyDayStartUtc(DateOnly day)
    {
        // Beirut moves its clocks at midnight, so on that day the first valid time is 01:00.
        var midnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var start = _zone.IsInvalidTime(midnight) ? midnight.AddHours(1) : midnight;
        return TimeZoneInfo.ConvertTimeToUtc(start, _zone);
    }
}
