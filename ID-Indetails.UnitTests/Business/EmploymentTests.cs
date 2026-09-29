using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class EmploymentTests
{
    private static readonly DateOnly Today = new(2026, 9, 18);

    [Fact]
    public void Periods_give_the_joining_day_the_last_day_and_the_time_away_between_them()
    {
        var employment = new Employment([
            new Employment.Period(new(2026, 1, 5), new(2026, 3, 15)),
            new Employment.Period(new(2026, 5, 1), new(2026, 6, 30)),
            new Employment.Period(new(2026, 7, 1), null),
        ]);

        employment.JoinedOn.ShouldBe(new DateOnly(2026, 1, 5));
        employment.LeftOn.ShouldBeNull();
        // Back the day after leaving is no time away.
        employment.Gaps.ShouldBe([new Employment.Gap(new(2026, 3, 16), new(2026, 4, 30))]);
        Employment.InGap(employment.Gaps, new DateOnly(2026, 4, 1)).ShouldBeTrue();
        Employment.InGap(employment.Gaps, new DateOnly(2026, 5, 1)).ShouldBeFalse();
        Employment.InGap(null, new DateOnly(2026, 4, 1)).ShouldBeFalse();

        new Employment([new Employment.Period(new(2026, 1, 5), new(2026, 3, 15))]).LeftOn.ShouldBe(new DateOnly(2026, 3, 15));
    }

    [Fact]
    public void The_last_working_day_is_not_in_the_future_nor_before_the_start_and_leaves_closed_months_alone()
    {
        EmploymentRules.CheckLastWorkingDay(Today, new(2026, 1, 5), Today).ShouldBeNull();
        EmploymentRules.CheckLastWorkingDay(new(2026, 8, 31), new(2026, 1, 5), Today).ShouldBeNull();

        EmploymentRules.CheckLastWorkingDay(Today.AddDays(1), new(2026, 1, 5), Today)!.Code.ShouldBe(ErrorCodes.LastWorkingDayInvalid);
        EmploymentRules.CheckLastWorkingDay(new(2026, 9, 1), new(2026, 9, 10), Today)!.Kind.ShouldBe(DenialKind.Validation);

        // Leaving on 30 August would stop paying 31 August, in a closed month.
        EmploymentRules.CheckLastWorkingDay(new(2026, 8, 30), new(2026, 1, 5), Today)!.Code.ShouldBe(ErrorCodes.MonthClosed);
        // On the 1st, the month just ended is still open.
        EmploymentRules.CheckLastWorkingDay(new(2026, 9, 20), new(2026, 1, 5), new(2026, 10, 1)).ShouldBeNull();
    }

    [Fact]
    public void Coming_back_asks_about_the_time_away_only_when_it_began_this_month()
    {
        EmploymentRules.AsksAboutGap(new(2026, 9, 10), Today).ShouldBeTrue();
        EmploymentRules.GapDays(new(2026, 9, 10), Today).ShouldBe([new(2026, 9, 11), new(2026, 9, 12), new(2026, 9, 13),
            new(2026, 9, 14), new(2026, 9, 15), new(2026, 9, 16), new(2026, 9, 17)]);

        // Left yesterday or today: no time away.
        EmploymentRules.AsksAboutGap(Today.AddDays(-1), Today).ShouldBeFalse();
        EmploymentRules.AsksAboutGap(Today, Today).ShouldBeFalse();
        // Left last month: never paid, so nothing to ask.
        EmploymentRules.AsksAboutGap(new(2026, 8, 28), Today).ShouldBeFalse();
    }
}
