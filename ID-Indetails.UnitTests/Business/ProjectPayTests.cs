using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class ProjectPayTests
{
    // 1,760 a month is 10 an hour in June 2026 (22 days of 8 paid hours); overtime from 9.57 (1,760 ÷ 184), at 1.5x.
    private static readonly SalaryCalculator.PayTerms Monthly = new(new(2026, 1, 1), PayBasis.Monthly, 1760m, "USD", 9.57m);
    private static readonly PersonCalendar Calendar = new([new PersonCalendar.Policy(new(2000, 1, 1), 62, 540, 1020, 60, 1.5m)], []);

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

        var pay = ProjectPay.Of(work, madeUp: 60, Monthly, Calendar);

        pay.Normal.ShouldBe(480);
        pay.Overtime.ShouldBe(120);
        pay.HourlyRate.ShouldBe(10m);
        pay.Pay.ShouldBe(80m + 28.71m); // 8h at June's 10, 2h at the overtime rate 9.57 x 1.5
    }

    [Fact]
    public void A_salary_hour_is_priced_at_its_month_rate()
    {
        // July 2026 has 184 paid hours: 1,760 / 184.
        var pay = ProjectPay.Of(new Overtime.Split(1, new DateOnly(2026, 7, 1), 60, 0), 0, Monthly, Calendar);

        pay.HourlyRate.ShouldBe(9.5652m);
    }

    [Fact]
    public void Time_of_someone_without_pay_is_counted_but_not_priced()
    {
        var pay = ProjectPay.Of(new Overtime.Split(1, new DateOnly(2026, 6, 1), 60, 0), 0, null, Calendar);

        pay.Priced.ShouldBeFalse();
        pay.Pay.ShouldBe(0m);
        pay.Normal.ShouldBe(60);
    }
}
