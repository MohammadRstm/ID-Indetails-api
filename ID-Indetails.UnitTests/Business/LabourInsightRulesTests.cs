using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Finance.Intelligence;
using ID_Indetails.api.Enums;
using Shouldly;
using Row = ID_Indetails.api.Business.Finance.Intelligence.LabourInsightRules.Row;

namespace ID_Indetails.UnitTests.Business;

public class LabourInsightRulesTests
{
    private static DateOnly Month(int year, int month) => new(year, month, 1);

    private static readonly DateOnly[] Spring = [Month(2026, 3), Month(2026, 4), Month(2026, 5)];

    /// <summary>A person's labour on a project in a department in a month, all within the scope unless said.</summary>
    private static Row Worked(
        long projectId, long? teamId, DateOnly month, int minutes, decimal cost,
        int outsideMinutes = 0, decimal outsideCost = 0m, params ProjectFinance.UnconvertedCost[] unconverted)
        => new(projectId, teamId, month, minutes, cost, outsideMinutes, outsideCost, unconverted);

    [Fact]
    public void Months_outside_the_window_are_left_out()
    {
        Row[] rows =
        [
            Worked(1, 10, Month(2026, 2), 600, 100m),
            Worked(1, 10, Month(2026, 4), 120, 20m),
            Worked(2, 10, Month(2026, 6), 300, 50m),
        ];

        var result = LabourInsightRules.Of(rows, Spring);

        result.Total.Minutes.ShouldBe(120);
        result.Total.Cost.ShouldBe(20m);
        result.Projects.Select(p => p.Id).ShouldBe([1L]);
        result.Departments.ShouldHaveSingleItem().Totals.Cost.ShouldBe(20m);
    }

    [Fact]
    public void Every_month_of_the_window_is_there_oldest_first_empty_ones_at_zero()
    {
        Row[] rows = [Worked(1, 10, Month(2026, 4), 120, 20m), Worked(2, 10, Month(2026, 4), 60, 10m)];

        var result = LabourInsightRules.Of(rows, Spring);

        result.Months.Select(m => m.Start).ShouldBe(Spring);
        result.Months[0].Totals.ShouldBe(new LabourInsightRules.Totals(0m, 0, 0m, 0, null));
        result.Months[1].Totals.Minutes.ShouldBe(180);
        result.Months[1].Totals.Cost.ShouldBe(30m);
        result.Months[2].Totals.ShouldBe(new LabourInsightRules.Totals(0m, 0, 0m, 0, null));
    }

    [Fact]
    public void Nothing_logged_leaves_every_figure_at_zero_and_no_shares()
    {
        var result = LabourInsightRules.Of([], Spring);

        result.Total.ShouldBe(new LabourInsightRules.Totals(0m, 0, 0m, 0, null));
        result.Months.Count.ShouldBe(3);
        result.Projects.ShouldBeEmpty();
        result.Departments.ShouldBeEmpty();
        result.Unconverted.ShouldBeEmpty();
    }

    [Fact]
    public void Projects_add_up_their_rows_across_people_and_months()
    {
        Row[] rows =
        [
            Worked(1, 10, Month(2026, 3), 120, 20m, outsideMinutes: 30, outsideCost: 5m),
            Worked(1, 11, Month(2026, 4), 60, 10m),
            Worked(2, 10, Month(2026, 5), 240, 40m, outsideMinutes: 60, outsideCost: 10m),
        ];

        var result = LabourInsightRules.Of(rows, Spring);

        var one = result.Projects.Single(p => p.Id == 1);
        one.Totals.ShouldBe(new LabourInsightRules.Totals(30m, 180, 5m, 30, 0.1667m));
        var two = result.Projects.Single(p => p.Id == 2);
        two.Totals.ShouldBe(new LabourInsightRules.Totals(40m, 240, 10m, 60, 0.25m));
    }

    [Fact]
    public void Projects_come_costliest_first_then_most_minutes_then_by_id()
    {
        Row[] rows =
        [
            Worked(5, 10, Month(2026, 4), 60, 10m),
            Worked(4, 10, Month(2026, 4), 60, 10m),
            Worked(3, 10, Month(2026, 4), 90, 10m),
            Worked(2, 10, Month(2026, 4), 30, 50m),
        ];

        var result = LabourInsightRules.Of(rows, Spring);

        result.Projects.Select(p => p.Id).ShouldBe([2L, 3L, 4L, 5L]);
    }

    [Fact]
    public void Hours_logged_in_no_department_come_last_among_equals()
    {
        Row[] rows =
        [
            Worked(1, null, Month(2026, 4), 60, 10m),
            Worked(1, 12, Month(2026, 4), 60, 10m),
            Worked(1, 11, Month(2026, 4), 60, 10m),
            Worked(1, 13, Month(2026, 4), 60, 30m),
        ];

        var result = LabourInsightRules.Of(rows, Spring);

        result.Departments.Select(d => d.Id).ShouldBe([13L, 11L, 12L, null]);
        result.Departments.Last().Totals.Cost.ShouldBe(10m);
    }

    [Fact]
    public void Departments_add_up_their_rows_across_projects()
    {
        Row[] rows =
        [
            Worked(1, 10, Month(2026, 3), 120, 20m),
            Worked(2, 10, Month(2026, 4), 60, 15m, outsideMinutes: 60, outsideCost: 15m),
            Worked(3, 11, Month(2026, 4), 30, 5m),
        ];

        var result = LabourInsightRules.Of(rows, Spring);

        result.Departments.Select(d => d.Id).ShouldBe([10L, 11L]);
        result.Departments[0].Totals.ShouldBe(new LabourInsightRules.Totals(35m, 180, 15m, 60, 0.4286m));
        result.Departments[1].Totals.ShouldBe(new LabourInsightRules.Totals(5m, 30, 0m, 0, 0m));
    }

    [Fact]
    public void Labour_share_is_each_parts_fraction_of_the_windows_cost_to_4_places()
    {
        Row[] rows =
        [
            Worked(1, 10, Month(2026, 4), 60, 10m),
            Worked(2, 10, Month(2026, 4), 60, 10m),
            Worked(3, 11, Month(2026, 4), 60, 10m),
        ];

        var result = LabourInsightRules.Of(rows, Spring);

        result.Projects.Select(p => p.LabourShare).ShouldBe([0.3333m, 0.3333m, 0.3333m]);
        result.Departments.Select(d => d.LabourShare).ShouldBe([0.6667m, 0.3333m]);
    }

    [Fact]
    public void With_no_cost_the_shares_are_null_but_the_hours_still_count()
    {
        // hours logged by people with no pay record cost nothing
        Row[] rows = [Worked(1, 10, Month(2026, 4), 90, 0m, outsideMinutes: 30)];

        var result = LabourInsightRules.Of(rows, Spring);

        result.Total.ShouldBe(new LabourInsightRules.Totals(0m, 90, 0m, 30, null));
        var project = result.Projects.ShouldHaveSingleItem();
        project.Totals.Minutes.ShouldBe(90);
        project.LabourShare.ShouldBeNull();
        result.Departments.ShouldHaveSingleItem().LabourShare.ShouldBeNull();
    }

    [Fact]
    public void Outside_scope_share_is_outside_cost_over_cost_to_4_places()
    {
        Row[] rows = [Worked(1, 10, Month(2026, 4), 300, 30m, outsideMinutes: 100, outsideCost: 10m)];

        var result = LabourInsightRules.Of(rows, Spring);

        result.Total.OutsideScopeShare.ShouldBe(0.3333m);
        result.Total.OutsideScopeMinutes.ShouldBe(100);
        result.Total.OutsideScopeCost.ShouldBe(10m);
        result.Months[1].Totals.OutsideScopeShare.ShouldBe(0.3333m);
    }

    [Fact]
    public void Totals_are_rounded_from_the_unrounded_parts()
    {
        Row[] rows =
        [
            Worked(1, 10, Month(2026, 4), 60, 10.005m, outsideMinutes: 60, outsideCost: 10.005m),
            Worked(2, 11, Month(2026, 4), 60, 10.005m, outsideMinutes: 60, outsideCost: 10.005m),
        ];

        var result = LabourInsightRules.Of(rows, Spring);

        result.Projects.Select(p => p.Totals.Cost).ShouldBe([10.01m, 10.01m]);
        // 20.01, not the 20.02 of the rounded parts
        result.Total.Cost.ShouldBe(20.01m);
        result.Total.OutsideScopeCost.ShouldBe(20.01m);
        result.Months[1].Totals.Cost.ShouldBe(20.01m);
        result.Total.OutsideScopeShare.ShouldBe(1m);
    }

    [Fact]
    public void Labour_with_no_exchange_rate_is_grouped_per_currency_and_only_labour()
    {
        Row[] rows =
        [
            Worked(1, 10, Month(2026, 4), 60, 0m, 0, 0m,
                new ProjectFinance.UnconvertedCost(CostSource.Labour, "LBP", 900_000m),
                new ProjectFinance.UnconvertedCost(CostSource.Expenses, "LBP", 5_000_000m)),
            Worked(2, 11, Month(2026, 5), 60, 0m, 0, 0m,
                new ProjectFinance.UnconvertedCost(CostSource.Labour, "lbp", 100_000m),
                new ProjectFinance.UnconvertedCost(CostSource.Labour, "EUR", 40m)),
            // outside the window
            Worked(3, 11, Month(2026, 1), 60, 0m, 0, 0m,
                new ProjectFinance.UnconvertedCost(CostSource.Labour, "EUR", 1_000m)),
        ];

        var result = LabourInsightRules.Of(rows, Spring);

        result.Unconverted.ShouldBe([("EUR", 40m), ("LBP", 1_000_000m)]);
        result.Total.Cost.ShouldBe(0m);
    }
}
