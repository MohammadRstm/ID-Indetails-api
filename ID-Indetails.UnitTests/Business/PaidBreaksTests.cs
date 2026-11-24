using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PaidBreaksTests
{
    // Monday to Friday, 09:00 to 17:00, with a paid 60-minute break.
    private static readonly PersonCalendar Calendar = new([new PersonCalendar.Policy(new(2000, 1, 1), 62, 540, 1020, 60, 1m)], []);
    private static readonly DateOnly Tuesday = new(2026, 2, 10);
    private static readonly DateOnly Saturday = new(2026, 2, 14);

    [Fact]
    public void A_full_day_shares_the_whole_break_across_its_projects_by_in_hours_minutes_in_whole_minutes()
    {
        // 5 hours on one, 2 on the other: the 7 required hours, so the whole hour of break; 42.86 and 17.14 minutes
        // round down to 42 and 17, and the minute left goes to the project with the most of the day
        var shares = PaidBreaks.Allocate(
            [new PaidBreaks.Work(1, 10, Tuesday, 540, 300), new PaidBreaks.Work(2, 20, Tuesday, 840, 120)],
            Calendar);

        shares.ShouldBe([new PaidBreaks.Share(10, Tuesday, 43m), new PaidBreaks.Share(20, Tuesday, 17m)], ignoreOrder: true);
        PaidBreaks.Unshared([new PaidBreaks.Work(1, 10, Tuesday, 540, 300), new PaidBreaks.Work(2, 20, Tuesday, 840, 120)], Calendar).ShouldBeEmpty();
    }

    [Fact]
    public void A_day_logged_in_part_shares_that_part_of_the_break_and_the_rest_reaches_no_project()
    {
        // 4 of the 7 required hours: 4/7 of the hour, 34 minutes, shared 3 to 1 (25.5 and 8.5: 26 and 8)
        var work = new[] { new PaidBreaks.Work(1, 10, Tuesday, 540, 180), new PaidBreaks.Work(2, 20, Tuesday, 720, 60) };

        PaidBreaks.Allocate(work, Calendar).ShouldBe([new PaidBreaks.Share(10, Tuesday, 26m), new PaidBreaks.Share(20, Tuesday, 8m)], ignoreOrder: true);
        PaidBreaks.Unshared(work, Calendar).ShouldBe([(Tuesday, 26)]);
        var day = new WorkHours(540, 1020, 60);
        day.BreakTaken(420).ShouldBe(60);
        day.BreakTaken(210).ShouldBe(30);
        day.BreakUnshared(210).ShouldBe(30);
    }

    [Fact]
    public void Days_off_and_work_only_outside_hours_earn_no_break()
    {
        PaidBreaks.Allocate([new PaidBreaks.Work(1, 10, Saturday, 540, 240)], Calendar).ShouldBeEmpty();
        PaidBreaks.Allocate([new PaidBreaks.Work(1, 10, Tuesday, 1080, 120)], Calendar).ShouldBeEmpty();
    }

    [Fact]
    public void Short_days_have_no_paid_break()
    {
        var shortDay = new PersonCalendar([new PersonCalendar.Policy(new(2000, 1, 1), 62, 540, 840, 60, 1m)], []);

        PaidBreaks.Allocate([new PaidBreaks.Work(1, 10, Tuesday, 540, 120)], shortDay).ShouldBeEmpty();
    }

    [Fact]
    public void Every_priced_day_costs_its_break_at_that_days_rate()
    {
        var shares = new[] { new PaidBreaks.Share(10, new(2026, 1, 20), 60m), new PaidBreaks.Share(10, new(2026, 3, 10), 30m) };
        var history = new[]
        {
            // One rate, then a new salary from March: each break at its own day's rate.
            new ProjectFinance.PayRate(1, new(2026, 1, 1), 20m, "USD", 20m),
            new ProjectFinance.PayRate(1, new(2026, 3, 1), 12m, "USD", 11.5m),
        };

        var costs = PaidBreaks.Price(shares, history);

        costs.ShouldBe([
            (10L, new ProjectFinance.CostItem(CostSource.Labour, "USD", 20m, new(2026, 1, 20))),
            (10L, new ProjectFinance.CostItem(CostSource.Labour, "USD", 6m, new(2026, 3, 10))),
        ]);
        PaidBreaks.Price(shares, []).ShouldBeEmpty();
    }

    [Fact]
    public void A_break_in_a_month_as_an_admin_costs_nothing()
    {
        // 3,200 a month is 20 an hour in February 2026 (160 paid hours); promoted to admin on Wednesday 11 March, so all
        // of March is a fixed salary.
        var roles = new RoleHistory(RoleCodes.Admin, [new RoleHistory.Change(new(2026, 3, 11), RoleCodes.Employee, RoleCodes.Admin)]);
        var history = ProjectFinance.RatesOf(
            [new ProjectFinance.PayRecord(1, new(2026, 1, 1), 3200m, "USD", 17.39m)], Calendar, until: new(2026, 3, 20), roles);
        var shares = new[] { new PaidBreaks.Share(10, Tuesday, 60m), new PaidBreaks.Share(10, new(2026, 3, 3), 60m) };

        PaidBreaks.Price(shares, history).Select(p => p.Cost.Amount).ShouldBe([20m, 0m]);
    }
}
