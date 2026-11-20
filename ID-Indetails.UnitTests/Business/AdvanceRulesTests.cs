using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;
using Advance = ID_Indetails.api.Business.Payroll.AdvanceRules.Advance;
using PlannedMonth = ID_Indetails.api.Business.Payroll.AdvanceRules.PlannedMonth;

namespace ID_Indetails.UnitTests.Business;

/// <summary>Salary advances: who may ask and give, the months they come off and how each month takes its share.</summary>
public class AdvanceRulesTests
{
    private static readonly DateOnly Nov = new(2026, 11, 1);
    private static readonly DateOnly Dec = new(2026, 12, 1);
    private static readonly DateOnly Jan = new(2027, 1, 1);
    private static readonly DateOnly Feb = new(2027, 2, 1);

    private static readonly IReadOnlySet<DateOnly> Nobody = new HashSet<DateOnly>();

    private static Advance Given(long id, decimal amount, DateOnly? settledOn = null, params (DateOnly Month, decimal Amount)[] planned)
        => new(id, amount, planned.ToDictionary(p => p.Month, p => p.Amount), settledOn);

    private static IReadOnlyDictionary<DateOnly, decimal> Pay(params (DateOnly Month, decimal Amount)[] months)
        => months.ToDictionary(m => m.Month, m => m.Amount);

    /// <summary>One advance on its own, worked out up to <paramref name="through"/>; <paramref name="leavingMonths"/> are the months the person's employment ended in.</summary>
    private static AdvanceRules.Schedule Only(Advance advance, IReadOnlyDictionary<DateOnly, decimal> pay, DateOnly through, params DateOnly[] leavingMonths)
        => AdvanceRules.Repayments([advance], pay, leavingMonths.ToHashSet(), through)[advance.Id];

    private static PlannedMonth Plan(DateOnly month, decimal amount) => new(month, amount);

    // ----- repayments -----

    [Fact]
    public void An_advance_planned_in_one_month_comes_off_that_month()
    {
        var schedule = Only(Given(1, 500, null, (Nov, 500)), Pay((Nov, 2000)), Nov);

        schedule.Months.ShouldHaveSingleItem().ShouldBe(new AdvanceRules.MonthTaken(Nov, 500, 500, 500));
        schedule.Left.ShouldBe(0);
        schedule.Missing.ShouldBeEmpty();
        schedule.TakenIn(Nov).ShouldBe(500);
    }

    [Fact]
    public void A_spread_advance_takes_each_months_share_and_stops_when_nothing_is_left()
    {
        var advance = Given(1, 600, null, (Nov, 200), (Dec, 200), (Jan, 200));
        var pay = Pay((Nov, 1000), (Dec, 1000), (Jan, 1000), (Feb, 1000));

        var untilDecember = Only(advance, pay, Dec);
        untilDecember.Months.Select(m => m.Taken).ShouldBe([200m, 200m]);
        untilDecember.Left.ShouldBe(200);
        untilDecember.TakenIn(Dec).ShouldBe(200);
        untilDecember.TakenBefore(Dec).ShouldBe(200);

        // February is past the last planned month and nothing is left: it takes nothing and isn't listed
        var untilFebruary = Only(advance, pay, Feb);
        untilFebruary.Months.Select(m => m.Month).ShouldBe([Nov, Dec, Jan]);
        untilFebruary.Left.ShouldBe(0);
    }

    [Fact]
    public void What_a_short_month_could_not_take_moves_to_the_next_month()
    {
        var schedule = Only(Given(1, 600, null, (Nov, 300), (Dec, 300)), Pay((Nov, 100), (Dec, 1000)), Dec);

        schedule.Months[0].ShouldBe(new AdvanceRules.MonthTaken(Nov, 300, 300, 100));

        // December owes its own 300 and the 200 November left
        schedule.Months[1].ShouldBe(new AdvanceRules.MonthTaken(Dec, 300, 500, 500));
        schedule.Left.ShouldBe(0);
    }

    [Fact]
    public void A_shortfall_goes_on_past_the_last_planned_month_until_it_is_paid()
    {
        var schedule = Only(Given(1, 600, null, (Nov, 600)), Pay((Nov, 0), (Dec, 250), (Jan, 1000)), Jan);

        schedule.Months.Select(m => (m.Month, m.Planned, m.Due, m.Taken)).ShouldBe(
        [
            (Nov, 600m, 600m, 0m),
            (Dec, 0m, 600m, 250m),
            (Jan, 0m, 350m, 350m),
        ]);
        schedule.Left.ShouldBe(0);
    }

    [Fact]
    public void A_month_never_takes_more_than_its_pay_to_the_cent_below()
    {
        var schedule = Only(Given(1, 200, null, (Nov, 200)), Pay((Nov, 100.559m)), Nov);

        schedule.Months.ShouldHaveSingleItem().Taken.ShouldBe(100.55m);
        schedule.Left.ShouldBe(99.45m);
    }

    [Fact]
    public void In_the_month_someone_leaves_everything_left_is_due_up_to_their_pay()
    {
        var advance = Given(1, 1000, null, (Nov, 200), (Dec, 200), (Jan, 200), (Feb, 400));

        // leaving in December: December takes all 800 that is left
        var covered = Only(advance, Pay((Nov, 5000), (Dec, 5000)), Dec, Dec);
        covered.Months.Select(m => (m.Month, m.Due, m.Taken)).ShouldBe([(Nov, 200m, 200m), (Dec, 800m, 800m)]);
        covered.Left.ShouldBe(0);

        // leaving in November with only 700 to take: the rest stays to be settled
        var thin = Only(advance, Pay((Nov, 700)), Nov, Nov);
        thin.Months.ShouldHaveSingleItem().ShouldBe(new AdvanceRules.MonthTaken(Nov, 200, 1000, 700));
        thin.Left.ShouldBe(300);
    }

    [Fact]
    public void The_month_an_advance_was_settled_in_still_takes_its_share_and_nothing_comes_off_after_it()
    {
        // settled in December with 200 left after December's share
        var advance = Given(1, 600, new DateOnly(2026, 12, 15), (Nov, 200), (Dec, 200), (Jan, 200)) with { SettledAmount = 200 };

        var schedule = Only(advance, Pay((Nov, 1000), (Dec, 1000), (Jan, 1000)), Jan);

        schedule.Months.Select(m => (m.Month, m.Taken)).ShouldBe([(Nov, 200m), (Dec, 200m)]);
        schedule.Left.ShouldBe(200);
    }

    [Fact]
    public void The_month_an_advance_was_settled_in_never_takes_more_than_the_settlement_left_to_it()
    {
        // settled in December for everything after November: December takes nothing, even with a leaving month's claim
        var advance = Given(1, 600, new DateOnly(2026, 12, 15), (Nov, 200), (Dec, 400)) with { SettledAmount = 400 };

        var schedule = Only(advance, Pay((Nov, 1000), (Dec, 1000)), Dec, Dec);

        schedule.Months.Sum(m => m.Taken).ShouldBe(200);
        schedule.Left.ShouldBe(400);
    }

    [Fact]
    public void Two_advances_of_one_person_are_taken_oldest_first_from_each_months_pay()
    {
        var older = Given(1, 800, null, (Nov, 800));
        var newer = Given(2, 600, null, (Nov, 600));
        var pay = Pay((Nov, 1000), (Dec, 1000));

        var november = AdvanceRules.Repayments([older, newer], pay, Nobody, Nov);
        november[1].Left.ShouldBe(0);
        november[2].TakenIn(Nov).ShouldBe(200);
        november[2].Left.ShouldBe(400);

        var december = AdvanceRules.Repayments([older, newer], pay, Nobody, Dec);
        december[2].Months.Select(m => m.Taken).ShouldBe([200m, 400m]);
        december[2].Left.ShouldBe(0);
    }

    [Fact]
    public void A_month_something_is_due_in_with_no_pay_given_is_reported_missing()
    {
        var schedule = Only(Given(1, 500, null, (Nov, 500)), Pay(), Nov);

        schedule.Missing.ShouldBe([Nov]);
        schedule.Months.ShouldHaveSingleItem().Taken.ShouldBe(0);
        schedule.Left.ShouldBe(500);
    }

    [Fact]
    public void An_advance_with_no_months_planned_takes_nothing()
    {
        var schedule = Only(Given(1, 500), Pay((Nov, 2000)), Nov);

        schedule.Months.ShouldBeEmpty();
        schedule.Left.ShouldBe(500);
    }

    // ----- is it open -----

    [Fact]
    public void An_advance_is_open_while_given_unsettled_and_something_is_left()
    {
        AdvanceRules.IsOpen(SalaryAdvanceStatus.Approved, null, 500, 100).ShouldBeTrue();
        AdvanceRules.IsOpen(SalaryAdvanceStatus.Approved, null, 500, 500).ShouldBeFalse();
        AdvanceRules.IsOpen(SalaryAdvanceStatus.Approved, new DateOnly(2026, 11, 10), 500, 0).ShouldBeFalse();
        AdvanceRules.IsOpen(SalaryAdvanceStatus.Pending, null, 500, 0).ShouldBeFalse();
        AdvanceRules.IsOpen(SalaryAdvanceStatus.Cancelled, null, 500, 0).ShouldBeFalse();
        AdvanceRules.IsOpen(SalaryAdvanceStatus.Declined, null, 500, 0).ShouldBeFalse();
    }

    // ----- asking -----

    [Fact]
    public void Asking_needs_pay_and_no_request_waiting_nor_an_advance_still_to_pay_back()
    {
        AdvanceRules.CheckAsk(true, false, false).ShouldBeNull();
        AdvanceRules.CheckAsk(false, false, false)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceNoPay);
        AdvanceRules.CheckAsk(true, true, false)!.Code.ShouldBe(ErrorCodes.SalaryAdvancePending);
        AdvanceRules.CheckAsk(true, false, true)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceOpen);

        // the reason shown first: no pay, then a waiting request, then an open advance
        AdvanceRules.CheckAsk(false, true, true)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceNoPay);
        AdvanceRules.CheckAsk(true, true, true)!.Code.ShouldBe(ErrorCodes.SalaryAdvancePending);
        AdvanceRules.CheckAsk(true, true, true)!.Kind.ShouldBe(DenialKind.BusinessRule);
    }

    // ----- deciding -----

    [Fact]
    public void Nobody_gives_or_decides_their_own_advance()
    {
        var own = AdvanceRules.CheckDecider(7, 7, false, true)!;
        own.Kind.ShouldBe(DenialKind.Forbidden);
        own.Code.ShouldBe(ErrorCodes.EmployeeChangeNotAllowed);

        AdvanceRules.CheckDecider(7, 8, false, false).ShouldBeNull();
    }

    [Fact]
    public void A_super_admins_advance_needs_someone_who_may_change_super_admins()
    {
        var denied = AdvanceRules.CheckDecider(7, 8, personIsSuperAdmin: true, actorChangesSuperAdmins: false)!;
        denied.Kind.ShouldBe(DenialKind.Forbidden);
        denied.Code.ShouldBe(ErrorCodes.Forbidden);

        AdvanceRules.CheckDecider(7, 8, personIsSuperAdmin: true, actorChangesSuperAdmins: true).ShouldBeNull();
    }

    [Fact]
    public void Only_a_pending_request_can_be_decided()
    {
        AdvanceRules.CheckPending(SalaryAdvanceStatus.Pending).ShouldBeNull();
        foreach (var status in new[] { SalaryAdvanceStatus.Approved, SalaryAdvanceStatus.Declined, SalaryAdvanceStatus.Withdrawn, SalaryAdvanceStatus.Cancelled })
        {
            AdvanceRules.CheckPending(status)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceDecided);
        }
    }

    // ----- giving -----

    private static RuleDenial? Give(
        decimal amount = 500, decimal? asked = null, bool hasPay = true, decimal monthlyPay = 2000, bool hasOpen = false, PlannedMonth[]? months = null)
        => AdvanceRules.CheckGive(amount, asked, hasPay, monthlyPay, hasOpen, months ?? [Plan(Nov, amount)], Nov);

    [Fact]
    public void An_advance_is_at_most_what_was_asked_and_a_months_pay_with_no_other_open()
    {
        Give().ShouldBeNull();
        Give(500, asked: 500).ShouldBeNull();
        Give(400, asked: 500).ShouldBeNull();
        Give(2000, monthlyPay: 2000).ShouldBeNull();

        var more = Give(600, asked: 500)!;
        more.Kind.ShouldBe(DenialKind.Validation);
        more.Code.ShouldBe(ErrorCodes.SalaryAdvanceMoreThanAsked);
        more.Field.ShouldBe("amount");

        Give(hasPay: false)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceNoPay);
        Give(hasOpen: true)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceOpen);
        var over = Give(2500, monthlyPay: 2000)!;
        over.Kind.ShouldBe(DenialKind.BusinessRule);
        over.Code.ShouldBe(ErrorCodes.SalaryAdvanceOverPay);
    }

    [Fact]
    public void Giving_checks_more_than_asked_first_then_pay_then_an_open_advance_then_the_months()
    {
        Give(600, asked: 500, hasPay: false, hasOpen: true)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMoreThanAsked);
        Give(hasPay: false, hasOpen: true)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceNoPay);
        Give(hasOpen: true, months: [])!.Code.ShouldBe(ErrorCodes.SalaryAdvanceOpen);
        Give(months: [])!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsInvalid);
        Give(500, months: [Plan(Nov, 400)])!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsTotal);
    }

    // ----- the months -----

    private static RuleDenial? Months(decimal toPlan, decimal monthlyPay, params PlannedMonth[] months)
        => AdvanceRules.CheckMonths(months, toPlan, monthlyPay, Nov);

    [Fact]
    public void The_months_are_from_one_to_twelve_and_add_up_to_what_is_to_be_paid_back()
    {
        Months(600, 2000, Plan(Nov, 600)).ShouldBeNull();
        Months(600, 2000, Plan(Nov, 300), Plan(Dec, 300)).ShouldBeNull();

        // twelve months, the last one eleven months after the first
        var twelve = Enumerable.Range(0, 12).Select(i => Plan(Nov.AddMonths(i), 50)).ToArray();
        Months(600, 2000, twelve).ShouldBeNull();

        var none = Months(600, 2000)!;
        none.Kind.ShouldBe(DenialKind.Validation);
        none.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsInvalid);
        none.Field.ShouldBe("months");

        var thirteen = Enumerable.Range(0, 13).Select(i => Plan(Nov.AddMonths(i), 10)).ToArray();
        Months(130, 2000, thirteen)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsInvalid);
    }

    [Fact]
    public void A_month_is_a_first_day_from_the_first_allowed_month_and_within_twelve_months()
    {
        Months(600, 2000, Plan(new DateOnly(2026, 10, 1), 600))!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsInvalid);
        Months(600, 2000, Plan(new DateOnly(2026, 11, 15), 600))!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsInvalid);
        Months(600, 2000, Plan(Nov.AddMonths(12), 600))!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsInvalid);
        Months(600, 2000, Plan(Nov.AddMonths(11), 600)).ShouldBeNull();
    }

    [Fact]
    public void Each_month_is_chosen_once_and_takes_more_than_zero_to_the_cent()
    {
        Months(600, 2000, Plan(Nov, 300), Plan(Nov, 300))!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsInvalid);
        Months(600, 2000, Plan(Nov, 600), Plan(Dec, 0))!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsInvalid);
        Months(600, 2000, Plan(Nov, 700), Plan(Dec, -100))!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsInvalid);
        Months(600.001m, 2000, Plan(Nov, 600.001m))!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsInvalid);
        Months(600.55m, 2000, Plan(Nov, 600.55m)).ShouldBeNull();
    }

    [Fact]
    public void No_month_takes_more_than_a_months_pay_and_the_months_must_add_up()
    {
        var over = Months(2500, 2000, Plan(Nov, 2500))!;
        over.Kind.ShouldBe(DenialKind.BusinessRule);
        over.Code.ShouldBe(ErrorCodes.SalaryAdvanceOverPay);

        var notEnough = Months(600, 2000, Plan(Nov, 500))!;
        notEnough.Kind.ShouldBe(DenialKind.Validation);
        notEnough.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsTotal);
        notEnough.Field.ShouldBe("months");
        Months(600, 2000, Plan(Nov, 400), Plan(Dec, 300))!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsTotal);
    }

    // ----- given on -----

    [Fact]
    public void A_recorded_advance_was_given_from_the_first_of_this_month_up_to_today()
    {
        var today = new DateOnly(2026, 11, 13);
        AdvanceRules.CheckGivenOn(new DateOnly(2026, 11, 1), today).ShouldBeNull();
        AdvanceRules.CheckGivenOn(today, today).ShouldBeNull();

        var before = AdvanceRules.CheckGivenOn(new DateOnly(2026, 10, 31), today)!;
        before.Kind.ShouldBe(DenialKind.Validation);
        before.Code.ShouldBe(ErrorCodes.SalaryAdvanceGivenOn);
        before.Field.ShouldBe("givenOn");
        AdvanceRules.CheckGivenOn(new DateOnly(2026, 11, 14), today)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceGivenOn);
    }

    // ----- changing, cancelling, settling -----

    [Fact]
    public void Only_a_given_advance_that_is_not_settled_can_be_changed()
    {
        AdvanceRules.CheckGiven(SalaryAdvanceStatus.Approved, null).ShouldBeNull();
        AdvanceRules.CheckGiven(SalaryAdvanceStatus.Approved, new DateOnly(2026, 11, 10))!.Code.ShouldBe(ErrorCodes.SalaryAdvanceClosed);
        foreach (var status in new[] { SalaryAdvanceStatus.Pending, SalaryAdvanceStatus.Declined, SalaryAdvanceStatus.Withdrawn, SalaryAdvanceStatus.Cancelled })
        {
            AdvanceRules.CheckGiven(status, null)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceClosed);
        }
    }

    [Fact]
    public void The_open_months_are_changed_to_add_up_to_what_the_closed_months_did_not_plan()
    {
        static RuleDenial? Change(decimal plannedInClosedMonths, params PlannedMonth[] months)
            => AdvanceRules.CheckChangeMonths(SalaryAdvanceStatus.Approved, null, 600, plannedInClosedMonths, 2000, months, Dec);

        Change(200, Plan(Dec, 400)).ShouldBeNull();
        Change(200, Plan(Dec, 150), Plan(Jan, 250)).ShouldBeNull();
        Change(200, Plan(Dec, 300))!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsTotal);
        Change(200, Plan(Nov, 400))!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsInvalid);
        Change(200)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceMonthsInvalid);

        // every month planned has closed: whatever they couldn't take moves on by itself
        Change(600, Plan(Dec, 100))!.Code.ShouldBe(ErrorCodes.SalaryAdvanceNothingLeft);

        AdvanceRules.CheckChangeMonths(SalaryAdvanceStatus.Approved, new DateOnly(2026, 11, 20), 600, 0, 2000, [Plan(Dec, 600)], Dec)!
            .Code.ShouldBe(ErrorCodes.SalaryAdvanceClosed);
        AdvanceRules.CheckChangeMonths(SalaryAdvanceStatus.Pending, null, 600, 0, 2000, [Plan(Dec, 600)], Dec)!
            .Code.ShouldBe(ErrorCodes.SalaryAdvanceClosed);
    }

    [Fact]
    public void An_advance_is_cancelled_only_in_the_month_it_was_given()
    {
        AdvanceRules.CheckCancel(SalaryAdvanceStatus.Approved, null, new DateOnly(2026, 11, 5), Nov).ShouldBeNull();
        AdvanceRules.CheckCancel(SalaryAdvanceStatus.Approved, null, new DateOnly(2026, 10, 30), Nov)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceCancelClosed);
        AdvanceRules.CheckCancel(SalaryAdvanceStatus.Approved, null, null, Nov)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceCancelClosed);
        AdvanceRules.CheckCancel(SalaryAdvanceStatus.Approved, new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 5), Nov)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceClosed);
        AdvanceRules.CheckCancel(SalaryAdvanceStatus.Cancelled, null, new DateOnly(2026, 11, 5), Nov)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceClosed);
    }

    [Fact]
    public void Settling_needs_a_given_advance_with_something_left()
    {
        AdvanceRules.CheckSettle(SalaryAdvanceStatus.Approved, null, 100).ShouldBeNull();
        AdvanceRules.CheckSettle(SalaryAdvanceStatus.Approved, null, 0)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceNothingLeft);
        AdvanceRules.CheckSettle(SalaryAdvanceStatus.Approved, new DateOnly(2026, 11, 10), 100)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceClosed);
        AdvanceRules.CheckSettle(SalaryAdvanceStatus.Pending, null, 100)!.Code.ShouldBe(ErrorCodes.SalaryAdvanceClosed);
    }
}
