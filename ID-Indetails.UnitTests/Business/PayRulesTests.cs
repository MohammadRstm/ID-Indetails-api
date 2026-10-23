using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PayRulesTests
{
    // Monday to Friday, 9 to 5 with a paid hour's break: 8 paid hours a day.
    private static readonly PersonCalendar Company = new([new PersonCalendar.Policy(new(2000, 1, 1), 62, 540, 1020, 60, 1m)], []);

    [Fact]
    public void Hourly_pay_is_its_own_rate_whatever_the_month()
    {
        PayRules.HourlyRate(PayBasis.Hourly, 18.50m, new(2026, 2, 10), Company).ShouldBe(18.50m);
        PayRules.HourlyRate(PayBasis.Hourly, 18.50m, new(2026, 7, 10), Company).ShouldBe(18.50m);
    }

    [Theory]
    [InlineData(2026, 2, 160, 18.75)]     // 20 working days
    [InlineData(2026, 8, 168, 17.8571)]   // 31 days but only 21 working days
    [InlineData(2026, 9, 176, 17.0455)]   // 22 working days
    [InlineData(2026, 7, 184, 16.3043)]   // 23 working days
    public void A_salary_is_divided_by_the_paid_hours_of_the_month(int year, int month, int paidHours, decimal rate)
    {
        var day = new DateOnly(year, month, 15);

        PayRules.PaidMinutesInMonth(day, Company).ShouldBe(paidHours * 60);
        PayRules.HourlyRate(PayBasis.Monthly, 3000m, day, Company).ShouldBe(rate);
    }

    [Fact]
    public void A_part_time_schedule_gives_its_own_rate()
    {
        // Monday, Wednesday and Friday, 9 to 5: 13 days in June 2026, 104 paid hours.
        var days = new Dictionary<int, (int, int)> { [1] = (540, 1020), [3] = (540, 1020), [5] = (540, 1020) };
        var partTime = new PersonCalendar(
            [new PersonCalendar.Policy(new(2000, 1, 1), 62, 540, 1020, 60, 1m)],
            [new PersonCalendar.Schedule(new(2026, 1, 1), false, days)]);

        PayRules.HourlyRate(PayBasis.Monthly, 1040m, new(2026, 6, 1), partTime).ShouldBe(10m);
    }

    [Theory]
    [InlineData(2026, 2)]
    [InlineData(2026, 7)]
    [InlineData(2026, 9)]
    public void Overtime_has_the_same_true_hourly_rate_every_month(int year, int month)
        // 40 paid hours a week × 52 ÷ 12 = 173.33 hours a month.
        => PayRules.StandardHourlyRate(PayBasis.Monthly, 3000m, new DateOnly(year, month, 15), Company).ShouldBe(17.3077m);

    [Fact]
    public void The_true_hourly_rate_follows_the_weekly_hours_of_the_schedule()
    {
        // Monday, Wednesday and Friday, 9 to 5: 24 paid hours a week, 104 a standard month.
        var days = new Dictionary<int, (int, int)> { [1] = (540, 1020), [3] = (540, 1020), [5] = (540, 1020) };
        var partTime = new PersonCalendar(
            [new PersonCalendar.Policy(new(2000, 1, 1), 62, 540, 1020, 60, 1m)],
            [new PersonCalendar.Schedule(new(2026, 1, 1), false, days)]);

        PayRules.StandardHourlyRate(PayBasis.Monthly, 1040m, new(2026, 7, 1), partTime).ShouldBe(10m);
        PayRules.StandardHourlyRate(PayBasis.Hourly, 12m, new(2026, 7, 1), partTime).ShouldBe(12m);
    }

    [Fact]
    public void A_month_with_no_working_days_has_no_rate()
    {
        var nothing = new PersonCalendar([new PersonCalendar.Policy(new(2000, 1, 1), 0, 540, 1020, 60, 1m)], []);

        PayRules.HourlyRate(PayBasis.Monthly, 3000m, new(2026, 6, 1), nothing).ShouldBe(0m);
    }

    [Theory]
    [InlineData(RoleCodes.SuperAdmin, PayBasis.Monthly, true)]
    [InlineData(RoleCodes.SuperAdmin, PayBasis.Hourly, false)]
    [InlineData(RoleCodes.Admin, PayBasis.Monthly, true)]
    [InlineData(RoleCodes.Admin, PayBasis.Hourly, false)]
    [InlineData(RoleCodes.Employee, PayBasis.Hourly, true)]
    [InlineData(RoleCodes.Employee, PayBasis.Monthly, true)]
    public void Admins_and_super_admins_must_be_paid_monthly(string role, PayBasis basis, bool allowed)
        => (PayRules.CheckBasis(role, basis) is null).ShouldBe(allowed);

    [Theory]
    [InlineData(RoleCodes.Admin, PayBasis.Hourly, false)]
    [InlineData(RoleCodes.SuperAdmin, PayBasis.Hourly, false)]
    [InlineData(RoleCodes.Admin, PayBasis.Monthly, true)]
    [InlineData(RoleCodes.Admin, null, true)]
    [InlineData(RoleCodes.Employee, PayBasis.Hourly, true)]
    public void Someone_paid_hourly_needs_a_monthly_salary_before_becoming_an_admin(string newRole, PayBasis? basis, bool allowed)
        => (PayRules.CheckRoleChange(newRole, basis) is null).ShouldBe(allowed);

    [Fact]
    public void A_role_counts_from_the_day_it_changed_and_the_last_change_of_a_day_wins()
    {
        // Promoted on 10 June, demoted and promoted again on 20 June.
        var roles = new RoleHistory(RoleCodes.Admin,
        [
            new RoleHistory.Change(new(2026, 6, 10), RoleCodes.Employee, RoleCodes.Admin),
            new RoleHistory.Change(new(2026, 6, 20), RoleCodes.Admin, RoleCodes.Employee),
            new RoleHistory.Change(new(2026, 6, 20), RoleCodes.Employee, RoleCodes.Admin),
        ]);

        roles.RoleOn(new(2025, 1, 1)).ShouldBe(RoleCodes.Employee);
        roles.RoleOn(new(2026, 6, 9)).ShouldBe(RoleCodes.Employee);
        roles.RoleOn(new(2026, 6, 10)).ShouldBe(RoleCodes.Admin);
        roles.RoleOn(new(2026, 6, 20)).ShouldBe(RoleCodes.Admin);
        new RoleHistory(RoleCodes.SuperAdmin, []).RoleOn(new(2025, 1, 1)).ShouldBe(RoleCodes.SuperAdmin);
    }

    [Fact]
    public void A_fixed_salary_held_on_any_day_of_a_period_counts()
    {
        var promoted = new RoleHistory(RoleCodes.Admin, [new RoleHistory.Change(new(2026, 6, 15), RoleCodes.Employee, RoleCodes.Admin)]);
        var demoted = new RoleHistory(RoleCodes.Employee, [new RoleHistory.Change(new(2026, 6, 15), RoleCodes.Admin, RoleCodes.Employee)]);

        promoted.HadFixedSalary(new(2026, 6, 1), new(2026, 6, 14)).ShouldBeFalse();
        promoted.HadFixedSalary(new(2026, 6, 1), new(2026, 6, 15)).ShouldBeTrue();
        demoted.HadFixedSalary(new(2026, 6, 1), new(2026, 6, 30)).ShouldBeTrue();
        demoted.HadFixedSalary(new(2026, 6, 15), new(2026, 6, 30)).ShouldBeFalse();
    }
}
