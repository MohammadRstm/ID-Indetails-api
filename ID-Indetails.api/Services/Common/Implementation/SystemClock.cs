using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Common.Abstraction;
using Microsoft.Extensions.Options;

namespace ID_Indetails.api.Services.Common.Implementation;

public sealed class SystemClock(IOptions<CompanyOptions> company) : IClock
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(company.Value.TimeZone);

    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly CompanyToday => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _zone));
}
