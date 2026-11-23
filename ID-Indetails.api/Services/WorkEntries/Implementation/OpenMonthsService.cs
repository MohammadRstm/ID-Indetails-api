using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

/// <summary>Read once per request: a reopened month opens or closes for the next one.</summary>
public sealed class OpenMonthsService(IReopenedMonthRepository months, IClock clock) : IOpenMonths
{
    private OpenMonths? _current;

    public async Task<OpenMonths> CurrentAsync(CancellationToken cancellationToken = default)
    {
        if (_current is not null)
        {
            return _current;
        }

        var today = clock.CompanyToday;
        var reopened = await months.OpenAsync(today, cancellationToken);
        return _current = new OpenMonths(
            clock.CompanyDayStartUtc(WorkEntryRules.OpenFrom(today)),
            reopened.Select(m => new OpenMonths.Window(clock.CompanyDayStartUtc(m.Month), clock.CompanyDayStartUtc(m.Month.AddMonths(1)))).ToList());
    }
}
