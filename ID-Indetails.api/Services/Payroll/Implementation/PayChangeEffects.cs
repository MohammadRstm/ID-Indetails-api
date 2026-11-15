using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.Payroll.Implementation;

/// <summary>Reads what someone's pay moves from a day on, with the same calculations Salaries and project money use.</summary>
public sealed class PayChangeEffects(
    IPayrollRepository payroll,
    IPayrollInputs inputs,
    ISalaryAdvanceRepository advances,
    ISalaryAdvanceSchedules schedules,
    IProjectFinanceService finance,
    IClock clock) : IPayChangeEffects
{
    public async Task<PayEffects> MeasureAsync(long userId, DateOnly from, CancellationToken cancellationToken = default)
    {
        var person = (await payroll.UsersAsync([userId], cancellationToken)).Single();
        var thisMonth = AdvanceRules.MonthOf(clock.CompanyToday);
        var months = new List<DateOnly>();
        for (var month = AdvanceRules.MonthOf(from); month <= thisMonth; month = month.AddMonths(1))
        {
            months.Add(month);
        }

        var paid = (await inputs.ForAsync([person], months, cancellationToken))
            .Select(m => (m.Input, Pay: SalaryCalculator.Calculate(m.Input)))
            .ToList();
        var (taken, advanceCurrency) = await AdvanceRepaymentsAsync(person.Id, paid, thisMonth, cancellationToken);

        // a day either side covers the time-zone offset; a project with no work from the day simply doesn't move
        var projects = await payroll.ProjectsWorkedOnAsync(person.Id, from.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), cancellationToken);
        IReadOnlyDictionary<long, ProjectFinancials> money = projects.Count == 0
            ? new Dictionary<long, ProjectFinancials>()
            : await finance.GetAsync(projects, cancellationToken);

        return new PayEffects(
            paid.Select(p => new PayEffects.Month(p.Input.Month, p.Pay?.Total, p.Pay?.Terms.Currency, taken.GetValueOrDefault(p.Input.Month), advanceCurrency))
                .ToList(),
            projects.Select(p => new PayEffects.ProjectMoney(p.Id, p.Title, p.DomainCode, money[p.Id].Costs.Labour, money[p.Id].Summary.Profit, HeldToFloor(p)))
                .ToList());
    }

    /// <summary>What the person's advances take from each month's pay, worked out from these months' pay as it now stands.</summary>
    private async Task<(IReadOnlyDictionary<DateOnly, decimal> Taken, string? Currency)> AdvanceRepaymentsAsync(
        long userId,
        IReadOnlyList<(SalaryCalculator.Input Input, SalaryCalculator.Result? Pay)> paid,
        DateOnly through,
        CancellationToken cancellationToken)
    {
        var given = await advances.GivenToAsync([userId], cancellationToken);
        if (given.Count == 0)
        {
            return (new Dictionary<DateOnly, decimal>(), null);
        }

        var known = paid.ToDictionary(
            p => (userId, p.Input.Month),
            p => p.Pay is { } pay ? schedules.PayOf(pay, p.Input) : new MonthPay(string.Empty, 0));
        var scheduled = await schedules.ScheduleAsync(given, through, known, cancellationToken);
        var taken = paid.ToDictionary(p => p.Input.Month, p => scheduled.Values.Sum(g => g.Schedule.TakenIn(p.Input.Month)));
        return (taken, given[^1].Currency);
    }

    private static bool HeldToFloor(Project project)
        => project.Status is ProjectStatus.Active or ProjectStatus.Hold
            && ProfitFloorRules.Applies(project.BillingType, project.DomainCode, project.Revenue, project.ProjectFee);
}
