using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.WorkingTime;
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
    public void The_break_is_shared_across_the_days_projects_by_in_hours_minutes()
    {
        var shares = PaidBreaks.Allocate(
            [new PaidBreaks.Work(1, 10, Tuesday, 540, 180), new PaidBreaks.Work(2, 20, Tuesday, 720, 60)],
            Calendar);

        shares.ShouldBe([new PaidBreaks.Share(10, Tuesday, 45m), new PaidBreaks.Share(20, Tuesday, 15m)], ignoreOrder: true);
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
    public void Only_days_paid_by_the_hour_cost_a_break()
    {
        var shares = new[] { new PaidBreaks.Share(10, new(2026, 1, 20), 60m), new PaidBreaks.Share(10, new(2026, 3, 10), 30m) };
        var history = new[]
        {
            new ProjectFinance.PayRate(1, new(2026, 1, 1), 20m, "USD", IsHourly: true),
            new ProjectFinance.PayRate(1, new(2026, 3, 1), 12m, "USD", IsHourly: false),
        };

        var costs = PaidBreaks.Price(shares, history);

        costs.ShouldBe([(10L, new ProjectFinance.CostItem(CostSource.Labour, "USD", 20m, new(2026, 1, 20)))]);
        PaidBreaks.Price(shares, []).ShouldBeEmpty();
    }
}
