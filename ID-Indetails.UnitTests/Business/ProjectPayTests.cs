using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ProjectPayTests
{
    private static readonly SalaryCalculator.PayTerms Monthly = new(new(2026, 1, 1), PayBasis.Monthly, 1733.30m, "USD", 10m);

    [Fact]
    public void Periods_follow_the_company_week_start_and_calendar_months_and_quarters()
    {
        var wednesday = new DateOnly(2026, 6, 10);

        ProjectPay.Period(ProjectPayUnit.Week, wednesday, DayOfWeek.Monday).ShouldBe((new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 15)));
        ProjectPay.Period(ProjectPayUnit.Week, wednesday, DayOfWeek.Sunday).ShouldBe((new DateOnly(2026, 6, 7), new DateOnly(2026, 6, 14)));
        ProjectPay.Period(ProjectPayUnit.Month, wednesday, DayOfWeek.Monday).ShouldBe((new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1)));
        ProjectPay.Period(ProjectPayUnit.Quarter, wednesday, DayOfWeek.Monday).ShouldBe((new DateOnly(2026, 4, 1), new DateOnly(2026, 7, 1)));
    }

    [Fact]
    public void Made_up_overtime_is_paid_at_the_plain_rate_and_the_rest_at_the_multiplier()
    {
        var work = new Overtime.Split(1, new DateOnly(2026, 6, 1), Minutes: 600, Overtime: 180);

        var pay = ProjectPay.Of(work, madeUp: 60, Monthly, overtimeMultiplier: 1.5m);

        pay.Normal.ShouldBe(480);
        pay.Overtime.ShouldBe(120);
        pay.Pay.ShouldBe(80m + 30m); // 8h at 10, 2h at 10 x 1.5
    }

    [Fact]
    public void Time_of_someone_without_pay_is_counted_but_not_priced()
    {
        var pay = ProjectPay.Of(new Overtime.Split(1, new DateOnly(2026, 6, 1), 60, 0), 0, null, 1m);

        pay.Priced.ShouldBeFalse();
        pay.Pay.ShouldBe(0m);
        pay.Normal.ShouldBe(60);
    }
}
