using ID_Indetails.api.Business.Reports;
using Shouldly;
using Hours = ID_Indetails.api.Business.Reports.UtilizationRules.Hours;

namespace ID_Indetails.UnitTests.Business;

public class UtilizationRulesTests
{
    private const int Day = 8 * 60;

    [Fact]
    public void Available_is_expected_less_excused()
    {
        var totals = UtilizationRules.Of(new Hours(Logged: 17 * Day, Expected: 20 * Day, Excused: 2 * Day, Overtime: 0));

        totals.Available.ShouldBe(18 * Day);
        totals.Expected.ShouldBe(20 * Day);
        totals.Excused.ShouldBe(2 * Day);
        totals.Logged.ShouldBe(17 * Day);
    }

    [Fact]
    public void Available_is_never_below_zero()
    {
        var totals = UtilizationRules.Of(new Hours(Logged: 60, Expected: Day, Excused: 2 * Day, Overtime: 60));

        totals.Available.ShouldBe(0);
        totals.Utilization.ShouldBeNull();
    }

    [Fact]
    public void Utilization_is_logged_over_available_to_4_places()
    {
        UtilizationRules.Of(new Hours(Logged: 18 * Day, Expected: 20 * Day, Excused: 2 * Day, Overtime: 0))
            .Utilization.ShouldBe(1m);
        UtilizationRules.Of(new Hours(Logged: 100, Expected: 300, Excused: 0, Overtime: 0))
            .Utilization.ShouldBe(0.3333m);
        UtilizationRules.Of(new Hours(Logged: 200, Expected: 300, Excused: 0, Overtime: 0))
            .Utilization.ShouldBe(0.6667m);
    }

    [Fact]
    public void Nothing_available_has_no_utilization()
    {
        // a month fully excused
        UtilizationRules.Of(new Hours(Logged: 0, Expected: 20 * Day, Excused: 20 * Day, Overtime: 0)).Utilization.ShouldBeNull();
        UtilizationRules.Of(new Hours(Logged: 0, Expected: 0, Excused: 0, Overtime: 0)).Utilization.ShouldBeNull();
    }

    [Fact]
    public void Overtime_takes_utilization_past_1()
    {
        var totals = UtilizationRules.Of(new Hours(Logged: 22 * Day, Expected: 20 * Day, Excused: 0, Overtime: 2 * Day));

        totals.Utilization.ShouldBe(1.1m);
        totals.Overtime.ShouldBe(2 * Day);
    }

    [Fact]
    public void Several_people_add_up_and_the_utilization_is_that_of_the_sum()
    {
        Hours[] people =
        [
            new(Logged: 10 * Day, Expected: 20 * Day, Excused: 0, Overtime: 0),
            new(Logged: 20 * Day, Expected: 20 * Day, Excused: 5 * Day, Overtime: 5 * Day),
        ];

        var totals = UtilizationRules.Of(people);

        totals.ShouldBe(new UtilizationRules.Totals(
            Logged: 30 * Day, Expected: 40 * Day, Excused: 5 * Day, Available: 35 * Day, Overtime: 5 * Day, Utilization: 0.8571m));
    }

    [Fact]
    public void No_people_add_up_to_zero()
    {
        UtilizationRules.Of([]).ShouldBe(new UtilizationRules.Totals(0, 0, 0, 0, 0, null));
    }

    [Fact]
    public void The_mix_adds_up_each_projects_minutes_the_most_first_then_by_id()
    {
        var mix = UtilizationRules.Mix([(3, 60), (1, 120), (2, 60), (3, 60), (4, 0)]);

        mix.Select(m => (m.ProjectId, m.Minutes)).ShouldBe([(1L, 120), (3L, 120), (2L, 60)]);
    }

    [Fact]
    public void Each_projects_share_is_its_part_of_the_logged_minutes_to_4_places()
    {
        var mix = UtilizationRules.Mix([(1, 100), (2, 100), (3, 100)]);

        mix.Select(m => m.Share).ShouldBe([0.3333m, 0.3333m, 0.3333m]);

        UtilizationRules.Mix([(1, 200), (2, 100)]).Select(m => m.Share).ShouldBe([0.6667m, 0.3333m]);
        UtilizationRules.Mix([(9, 45)]).ShouldHaveSingleItem().Share.ShouldBe(1m);
    }

    [Fact]
    public void Nothing_logged_has_no_mix()
    {
        UtilizationRules.Mix([]).ShouldBeEmpty();
        UtilizationRules.Mix([(1, 0)]).ShouldBeEmpty();
    }
}
