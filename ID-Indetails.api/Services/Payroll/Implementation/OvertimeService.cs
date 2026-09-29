using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.Payroll.Implementation;

public sealed class OvertimeService(IPayrollRepository payroll, IPayrollInputs inputs, IClock clock) : IOvertimeService
{
    public async Task<IReadOnlyDictionary<long, int>> ByEntryAsync(IReadOnlyCollection<LoggedWork> work, CancellationToken cancellationToken = default)
    {
        if (work.Count == 0)
        {
            return new Dictionary<long, int>();
        }

        var userIds = work.Select(w => w.UserId).Distinct().ToList();
        var days = work.Select(w => clock.ToCompanyDate(w.StartedAt)).ToList();
        var wholeDays = await payroll.WorkAsync(
            userIds,
            clock.CompanyDayStartUtc(days.Min()),
            clock.CompanyDayStartUtc(days.Max().AddDays(1)),
            cancellationToken);
        var calendars = await inputs.CalendarsAsync(userIds, cancellationToken);

        return wholeDays
            .GroupBy(e => e.UserId)
            .SelectMany(g => Overtime.Of(g.Select(ToWork), calendars[g.Key]))
            .Where(s => s.Overtime > 0)
            .ToDictionary(s => s.Id, s => s.Overtime);
    }

    private Overtime.Work ToWork(WorkEntryTime entry)
    {
        var local = clock.ToCompanyTime(entry.StartedAt);
        return new Overtime.Work(entry.Id, DateOnly.FromDateTime(local), (int)local.TimeOfDay.TotalMinutes, entry.Minutes);
    }
}
