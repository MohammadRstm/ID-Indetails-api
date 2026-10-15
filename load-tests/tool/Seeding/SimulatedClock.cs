using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Common.Implementation;
using Microsoft.Extensions.Options;

namespace ID_Indetails.LoadTests.Tool.Seeding;

/// <summary>
/// The API's clock while seeding. It shows whatever moment the seeder has reached, so the simulated months happen at
/// their own dates. It only moves forward, and every request moves it on a few seconds.
/// </summary>
public sealed class SimulatedClock : IClock
{
    public const string TimeZone = "Asia/Beirut";

    private static readonly TimeSpan PerRequest = TimeSpan.FromSeconds(3);

    // The API's own time-zone arithmetic, Beirut's midnight clock changes included.
    private readonly SystemClock _zone = new(Options.Create(new CompanyOptions { TimeZone = TimeZone }));

    /// <summary>Starts at 07:00 on the first simulated day, company time.</summary>
    public SimulatedClock(DateOnly firstDay)
    {
        UtcNow = At(firstDay, 7);
    }

    public DateTime UtcNow { get; private set; }

    public DateOnly CompanyToday => ToCompanyDate(UtcNow);

    public DateOnly ToCompanyDate(DateTime utc) => _zone.ToCompanyDate(utc);

    public DateTime ToCompanyTime(DateTime utc) => _zone.ToCompanyTime(utc);

    public DateTime CompanyDayStartUtc(DateOnly day) => _zone.CompanyDayStartUtc(day);

    public DateTime FromCompanyTime(DateOnly day, int minutes) => _zone.FromCompanyTime(day, minutes);

    /// <summary>The instant of a company-time-zone day and time, in UTC.</summary>
    public DateTime At(DateOnly day, int hour, int minute = 0)
        => CompanyDayStartUtc(day).AddHours(hour).AddMinutes(minute);

    /// <summary>Moves to that moment, or stays put when the requests so far have already passed it.</summary>
    public void MoveTo(DateTime utc)
    {
        if (utc > UtcNow)
        {
            UtcNow = utc;
        }
    }

    public void MoveTo(DateOnly day, int hour, int minute = 0) => MoveTo(At(day, hour, minute));

    public void Tick() => UtcNow += PerRequest;
}
