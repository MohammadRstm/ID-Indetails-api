using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;
using static ID_Indetails.api.Business.Finance.ProjectFinance;

namespace ID_Indetails.UnitTests.Business;

public class ProjectFinanceTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Mar1 = new(2026, 3, 1);

    // 89,500 LBP to the dollar from 1 January, 90,000 from 1 March.
    private static readonly ExchangeRateTable Rates = new([
        new ExchangeRateTable.Rate("LBP", Jan1, 89_500m),
        new ExchangeRateTable.Rate("LBP", Mar1, 90_000m),
    ]);

    // Monday to Friday, 9 to 5: June 2026 has 176 paid hours, July 184.
    private static readonly PersonCalendar NineToFive = new([new PersonCalendar.Policy(new(2000, 1, 1), 62, 540, 1020, 60, 1m)], []);

    private static readonly RoleHistory AnEmployee = new(RoleCodes.Employee, []);

    private static Costs CostOf(decimal labour = 0, decimal expenses = 0, decimal materials = 0, decimal ground = 0)
        => new(labour, expenses, materials, ground, []);

    /// <summary>One hour on each day, priced at 1,760 a month from 1 January 2026 (10.4762 an hour in May, 10 in June, 9.5652 in July).</summary>
    private static IEnumerable<decimal> HourOn(RoleHistory roles, params DateOnly[] days)
        => PriceLabour(
                days.Select(d => new WorkedTime(7, d, 60)),
                RatesOf([new PayRecord(7, Jan1, PayBasis.Monthly, 1760m, "USD")], NineToFive, until: new DateOnly(2026, 7, 20), roles))
            .Items.Select(i => i.Amount);

    [Fact]
    public void A_salary_costs_its_rate_of_each_month_and_hourly_pay_its_own_rate()
    {
        // Monday to Friday, 9 to 5: May 2026 has 168 paid hours, June 176, July 184.
        var calendar = new PersonCalendar([new PersonCalendar.Policy(new(2000, 1, 1), 62, 540, 1020, 60, 1m)], []);
        PayRecord[] history =
        [
            new(7, new DateOnly(2026, 7, 10), PayBasis.Hourly, 12m, "USD"),
            new(7, new DateOnly(2026, 5, 15), PayBasis.Monthly, 1760m, "USD"),
        ];

        RatesOf(history, calendar, until: new DateOnly(2026, 8, 20), AnEmployee).ShouldBe(
        [
            new PayRate(7, new DateOnly(2026, 5, 15), 10.4762m, "USD"),
            new PayRate(7, new DateOnly(2026, 6, 1), 10m, "USD"),
            new PayRate(7, new DateOnly(2026, 7, 1), 9.5652m, "USD"),
            new PayRate(7, new DateOnly(2026, 7, 10), 12m, "USD"),
        ]);
    }

    [Fact]
    public void A_salary_still_in_force_has_a_rate_for_every_month_up_to_today()
    {
        var calendar = new PersonCalendar([new PersonCalendar.Policy(new(2000, 1, 1), 62, 540, 1020, 60, 1m)], []);

        RatesOf([new PayRecord(7, new DateOnly(2026, 5, 15), PayBasis.Monthly, 1760m, "USD")], calendar, until: new DateOnly(2026, 7, 1), AnEmployee)
            .Select(r => r.EffectiveFrom)
            .ShouldBe([new DateOnly(2026, 5, 15), new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)]);
    }

    [Fact]
    public void A_promotion_makes_its_whole_month_free_and_leaves_the_months_before_alone()
    {
        var promoted = new RoleHistory(RoleCodes.Admin, [new RoleHistory.Change(new DateOnly(2026, 6, 15), RoleCodes.Employee, RoleCodes.Admin)]);

        HourOn(promoted, new DateOnly(2026, 5, 29), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15), new DateOnly(2026, 7, 1))
            .ShouldBe([10.4762m, 0m, 0m, 0m]);
        HourOn(promoted, new DateOnly(2026, 5, 29)).ShouldBe(HourOn(AnEmployee, new DateOnly(2026, 5, 29)));
    }

    [Fact]
    public void A_demoted_admin_costs_nothing_until_the_month_ends_then_their_rate()
    {
        var demoted = new RoleHistory(RoleCodes.Employee, [new RoleHistory.Change(new DateOnly(2026, 6, 15), RoleCodes.Admin, RoleCodes.Employee)]);

        HourOn(demoted, new DateOnly(2026, 5, 29), new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 1))
            .ShouldBe([0m, 0m, 0m, 9.5652m]);
    }

    [Fact]
    public void Someone_who_was_never_an_employee_costs_nothing_on_any_day()
    {
        var neverChanged = new RoleHistory(RoleCodes.SuperAdmin, []);
        var adminThenSuperAdmin = new RoleHistory(RoleCodes.SuperAdmin, [new RoleHistory.Change(new DateOnly(2026, 6, 15), RoleCodes.Admin, RoleCodes.SuperAdmin)]);
        DateOnly[] days = [new(2025, 12, 1), new(2026, 6, 12), new(2026, 6, 15), new(2026, 7, 1)];

        HourOn(neverChanged, days).ShouldAllBe(amount => amount == 0m);
        HourOn(adminThenSuperAdmin, days).ShouldAllBe(amount => amount == 0m);
    }

    [Fact]
    public void Time_before_the_first_pay_record_keeps_the_role_held_then()
    {
        // Paid by the hour from 1 March; an admin until becoming an employee on 10 February, and the other way round.
        PayRecord[] pay = [new(7, Mar1, PayBasis.Hourly, 12m, "USD")];
        WorkedTime[] work = [new(7, new DateOnly(2026, 1, 20), 60), new(7, new DateOnly(2026, 2, 2), 60), new(7, new DateOnly(2026, 3, 5), 60)];
        var demoted = new RoleHistory(RoleCodes.Employee, [new RoleHistory.Change(new DateOnly(2026, 2, 10), RoleCodes.Admin, RoleCodes.Employee)]);
        var promoted = new RoleHistory(RoleCodes.Admin, [new RoleHistory.Change(new DateOnly(2026, 2, 10), RoleCodes.Employee, RoleCodes.Admin)]);

        PriceLabour(work, RatesOf(pay, NineToFive, until: new DateOnly(2026, 3, 20), demoted)).Items.Select(i => i.Amount).ShouldBe([0m, 0m, 12m]);
        PriceLabour(work, RatesOf(pay, NineToFive, until: new DateOnly(2026, 3, 20), promoted)).Items.Select(i => i.Amount).ShouldBe([12m, 0m, 0m]);
    }

    [Fact]
    public void Amounts_convert_at_the_rate_in_force_on_their_day()
    {
        Rates.ToUsd(179_000m, "LBP", new DateOnly(2026, 2, 15)).ShouldBe(2m);
        Rates.ToUsd(180_000m, "LBP", Mar1).ShouldBe(2m);
        Rates.ToUsd(50m, "USD", new DateOnly(2020, 1, 1)).ShouldBe(50m);
        Rates.ToUsd(179_000m, "LBP", new DateOnly(2025, 12, 31)).ShouldBeNull();
    }

    [Fact]
    public void Costs_are_converted_by_day_and_unconvertible_ones_are_listed_not_added()
    {
        var costs = ToUsd([
            new CostItem(CostSource.Expenses, "USD", 100m, Mar1),
            new CostItem(CostSource.Expenses, "LBP", 900_000m, Mar1),
            new CostItem(CostSource.Materials, "LBP", 179_000m, new DateOnly(2025, 6, 1)),
            new CostItem(CostSource.GroundLabour, "USD", 240m, Mar1),
        ], Rates);

        costs.Expenses.ShouldBe(110m);
        costs.Materials.ShouldBe(0m);
        costs.GroundLabour.ShouldBe(240m);
        costs.Total.ShouldBe(350m);
        costs.Unconverted.ShouldHaveSingleItem().ShouldBe(new UnconvertedCost(CostSource.Materials, "LBP", 179_000m));
    }

    [Fact]
    public void Dated_costs_are_either_converted_or_listed_unconverted_on_their_day()
    {
        CostItem[] items =
        [
            new(CostSource.Expenses, "LBP", 179_000m, new DateOnly(2026, 2, 15)),
            new(CostSource.Materials, "lbp", 179_000m, new DateOnly(2025, 6, 1)),
        ];

        DatedUsd(items, Rates).ShouldBe([new Recognition.DatedCost(CostSource.Expenses, new DateOnly(2026, 2, 15), 2m)]);
        Unconvertible(items, Rates).ShouldBe([new DatedUnconverted(CostSource.Materials, "LBP", 179_000m, new DateOnly(2025, 6, 1))]);
    }

    [Fact]
    public void Labour_uses_the_hourly_cost_in_force_on_the_day_worked()
    {
        var (items, unpriced) = PriceLabour(
            [
                new WorkedTime(1, new DateOnly(2026, 2, 10), 120),
                new WorkedTime(1, new DateOnly(2026, 3, 10), 60),
                new WorkedTime(1, new DateOnly(2025, 12, 1), 60),
                new WorkedTime(2, Mar1, 90),
            ],
            [
                new PayRate(1, Jan1, 10m, "USD"),
                new PayRate(1, Mar1, 20m, "USD"),
            ]);

        // Time before the first pay record uses the earliest rate; someone with no pay can't be priced.
        items.Select(i => i.Amount).ShouldBe([20m, 20m, 10m]);
        unpriced.ShouldBe(90);
    }

    [Fact]
    public void Profit_and_margin_come_from_revenue_and_cost()
    {
        var summary = Summarise(new Facts(IsClientWork: true, IsDone: true, ProgressPct: 100, Revenue: 10_000m, LabourMinutes: 600), CostOf(labour: 4000m, expenses: 1000m));

        summary.Cost.ShouldBe(5000m);
        summary.Profit.ShouldBe(5000m);
        summary.Margin.ShouldBe(0.5m);
        summary.RecognisedToDate.ShouldBe(10_000m);
        summary.CostPerHour.ShouldBe(500m);
        summary.Projected.ShouldBeNull();
    }

    [Fact]
    public void Margin_is_withheld_until_the_project_is_done_5_percent_along_or_has_10_hours()
    {
        var early = new Facts(true, false, 4, 10_000m, 9 * 60);
        Summarise(early, CostOf(labour: 100m)).Margin.ShouldBeNull();
        Summarise(early with { ProgressPct = 5 }, CostOf(labour: 100m)).Margin.ShouldBe(0.99m);
        Summarise(early with { LabourMinutes = 10 * 60 }, CostOf(labour: 100m)).MarginKnown.ShouldBeTrue();
    }

    [Fact]
    public void An_open_project_projects_its_cost_from_progress_and_recognises_revenue_by_progress()
    {
        var summary = Summarise(new Facts(true, false, 25, 10_000m, 0), CostOf(materials: 2000m));

        summary.Projected.ShouldBe(new Projection(8000m, 2000m, 0.2m));
        summary.RecognisedToDate.ShouldBe(2500m);
    }

    [Fact]
    public void Internal_projects_have_no_revenue_and_no_margin()
    {
        var summary = Summarise(new Facts(false, true, 100, 5000m, 60), CostOf(labour: 300m));

        summary.Revenue.ShouldBe(0m);
        summary.Profit.ShouldBe(-300m);
        summary.Margin.ShouldBeNull();
        summary.Projected.ShouldBeNull();
    }

    [Fact]
    public void Cash_adds_up_week_by_week_from_the_first_week()
    {
        // 2 March 2026 is a Monday.
        var weeks = CashByWeek(
            [
                new CostItem(CostSource.Materials, "USD", 200m, new DateOnly(2026, 2, 27)),
                new CostItem(CostSource.GroundLabour, "USD", 400m, new DateOnly(2026, 3, 4)),
                new CostItem(CostSource.Expenses, "LBP", 900_000m, new DateOnly(2026, 3, 10)),
                new CostItem(CostSource.Expenses, "LBP", 900_000m, new DateOnly(2025, 3, 10)),
            ],
            [(new DateOnly(2026, 3, 2), 1000m), (new DateOnly(2026, 3, 9), 500m)],
            [],
            Rates,
            from: new DateOnly(2026, 3, 3),
            to: new DateOnly(2026, 3, 12));

        // The February material counts in the running total but not in the first week's own cost.
        weeks.ShouldBe([
            new CashWeek(new DateOnly(2026, 3, 2), Received: 1000m, Spent: 600m, GroundLabour: 400m, ReceivedInWeek: 1000m, GroundLabourInWeek: 400m,
                SpentInWeek: 400m, LabourInWeek: 0m, MinutesInWeek: 0),
            new CashWeek(new DateOnly(2026, 3, 9), Received: 1500m, Spent: 610m, GroundLabour: 400m, ReceivedInWeek: 500m, GroundLabourInWeek: 0m,
                SpentInWeek: 10m, LabourInWeek: 0m, MinutesInWeek: 0),
        ]);
        weeks[^1].Profit.ShouldBe(890m);
    }

    [Fact]
    public void Labour_and_hours_add_up_by_the_company_week()
    {
        // Weeks starting on Sunday: 1 March 2026 is one.
        var weeks = CashByWeek(
            [
                new CostItem(CostSource.Labour, "USD", 60m, new DateOnly(2026, 3, 2)),
                new CostItem(CostSource.Labour, "USD", 30m, new DateOnly(2026, 3, 8)),
                new CostItem(CostSource.Expenses, "USD", 25m, new DateOnly(2026, 3, 8)),
            ],
            [],
            [(new DateOnly(2026, 3, 2), 360), (new DateOnly(2026, 3, 8), 180)],
            Rates,
            from: new DateOnly(2026, 3, 2),
            to: new DateOnly(2026, 3, 9),
            DayOfWeek.Sunday);

        weeks.Select(w => (w.WeekStart, w.MinutesInWeek, w.LabourInWeek, w.SpentInWeek, w.Spent)).ShouldBe([
            (new DateOnly(2026, 3, 1), 360, 60m, 60m, 60m),
            (new DateOnly(2026, 3, 8), 180, 30m, 55m, 115m),
        ]);
    }
}
