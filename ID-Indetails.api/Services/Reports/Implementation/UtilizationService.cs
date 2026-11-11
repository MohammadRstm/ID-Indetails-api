using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.Reports;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Reports;
using ID_Indetails.api.DTO.Response.Insights;
using ID_Indetails.api.DTO.Response.Reports;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;
using ID_Indetails.api.Services.Payroll.Implementation;
using ID_Indetails.api.Services.Reports.Abstraction;

namespace ID_Indetails.api.Services.Reports.Implementation;

/// <summary>
/// Utilization from the same hours ledger Salaries and a person's hours page use (<see cref="SalaryCalculator.LedgerOf"/>),
/// for the people it applies to (Design employees), keeping only hours: never the shortfall, the made-up hours or the
/// overtime paid, which are pay information (salary:view).
/// </summary>
public sealed class UtilizationService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IPayrollRepository payroll,
    IPayrollInputs inputs,
    IWorkEntryRepository entries,
    IClock clock) : IUtilizationService
{
    public async Task<UtilizationResponse> MonthAsync(UtilizationQuery query, CancellationToken cancellationToken = default)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.ReportView);
        if (!actor.CanSee(DomainCodes.Design))
        {
            throw new NotFoundException("Domain not found.");
        }

        var today = clock.CompanyToday;
        var month = Recognition.MonthOf(query.Month ?? today);
        if (month > Recognition.MonthOf(today))
        {
            throw new RequestValidationException(ErrorCodes.ValidationFailed, "month", "Utilization can't be shown for a future month.");
        }

        var people = (await payroll.PeopleAsync([DomainCodes.Design], false, DomainCodes.Design, null, month, cancellationToken))
            .Where(PayrollInputs.LedgerApplies)
            .ToList();
        var months = await inputs.ForAsync(people, [month], cancellationToken);

        // which project each entry of the month was logged on, for each person's mix
        var rows = await entries.RowsAsync(
            new WorkEntrySearch(From: clock.CompanyDayStartUtc(month), To: clock.CompanyDayStartUtc(month.AddMonths(1))), cancellationToken);
        var projectOf = rows.ToDictionary(r => r.Id, r => r.ProjectId);
        var titles = await entries.ProjectTitlesAsync(rows.Select(r => r.ProjectId).Distinct().ToList(), cancellationToken);

        var listed = new List<(UtilizationPersonResponse Person, long? TeamId, UtilizationRules.Hours Hours)>();
        foreach (var personMonth in months.Where(m => m.Input.LedgerApplies))
        {
            var work = Overtime.Of(personMonth.Input.Work, personMonth.Input.Calendar);
            var ledger = SalaryCalculator.LedgerOf(personMonth.Input, work);
            var hours = new UtilizationRules.Hours(work.Sum(w => w.Minutes), ledger.Expected, ledger.Excused, ledger.Overtime);
            if (hours.Logged == 0 && hours.Expected == 0)
            {
                // not employed in the month
                continue;
            }

            var mix = UtilizationRules.Mix(work
                .Where(w => projectOf.ContainsKey(w.Id))
                .Select(w => (projectOf[w.Id], w.Minutes)));
            var user = personMonth.User;
            listed.Add((
                new UtilizationPersonResponse(
                    user.Id,
                    NameOf(user),
                    user.Team?.Name,
                    TotalsOf(UtilizationRules.Of(hours)),
                    mix.Select(p => new UtilizationProjectResponse(p.ProjectId, titles.GetValueOrDefault(p.ProjectId, $"Project #{p.ProjectId}"), p.Minutes, p.Share)).ToList()),
                user.TeamId,
                hours));
        }

        var departments = listed
            .GroupBy(p => (p.TeamId, p.Person.Department))
            .Select(g => new UtilizationDepartmentResponse(
                g.Key.TeamId, g.Key.Department ?? "No department", g.Count(), TotalsOf(UtilizationRules.Of(g.Select(p => p.Hours)))))
            .OrderBy(d => d.TeamId is null)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new UtilizationResponse(
            month,
            today < month.AddMonths(1),
            TotalsOf(UtilizationRules.Of(listed.Select(p => p.Hours))),
            departments,
            listed.Select(p => p.Person).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            new InsightBasisResponse(
                "Each Design employee's month from the hours ledger: expected hours are the required hours of their working days " +
                "(from joining, up to leaving), excused hours and days come off them, and utilization is logged ÷ what was left.",
                [
                    "Overtime is the time past each day's required hours, as employees see it.",
                    "A department is the person's department today.",
                    "This month runs to today.",
                ],
                [
                    "Admins, super admins and site staff: on fixed salaries, they log no hours.",
                    "Pay information: the shortfall, which overtime made up missing hours and what is paid as overtime (Salaries).",
                ]));
    }

    private static string NameOf(Models.User user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return name.Length > 0 ? name : user.Email;
    }

    private static UtilizationTotalsResponse TotalsOf(UtilizationRules.Totals t)
        => new(t.Logged, t.Expected, t.Excused, t.Available, t.Overtime, t.Utilization);
}
