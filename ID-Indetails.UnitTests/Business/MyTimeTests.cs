using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class MyTimeTests
{
    // Monday to Friday, 9 to 5 with a paid hour's break: 7 hours required a day.
    private static readonly PersonCalendar Calendar = new(
        [new PersonCalendar.Policy(new DateOnly(2020, 1, 1), WorkWeek.ToMask([1, 2, 3, 4, 5]), 540, 1020, 60, 1.5m)],
        []);

    private static readonly DateOnly September = new(2026, 9, 1); // a Tuesday

    private static Overtime.Work Work(long id, int day, int start, int minutes) => new(id, new DateOnly(2026, 9, day), start, minutes);

    [Fact]
    public void The_full_month_counts_every_working_day_from_the_day_they_joined()
    {
        MyTime.ExpectedFullMonth(September, joinedOn: new DateOnly(2025, 1, 1), Calendar).ShouldBe(22 * 420);
        MyTime.ExpectedFullMonth(September, joinedOn: new DateOnly(2026, 9, 28), Calendar).ShouldBe(3 * 420);
    }

    [Fact]
    public void Days_show_what_each_asked_for_and_what_was_logged_newest_first()
    {
        var work = Overtime.Of([Work(1, 1, 540, 420), Work(2, 2, 540, 480), Work(3, 5, 600, 120), Work(4, 7, 540, 60)], Calendar);

        var days = MyTime.Days(September, today: new DateOnly(2026, 9, 7), joinedOn: new DateOnly(2025, 1, 1), Calendar, work,
            excusedDays: new HashSet<DateOnly> { new(2026, 9, 4) }, wholeMonthExcused: false);

        days.Select(d => d.Date.Day).ShouldBe([7, 5, 4, 3, 2, 1]);
        days.Single(d => d.Date.Day == 1).Difference.ShouldBe(0);
        days.Single(d => d.Date.Day == 2).ShouldSatisfyAllConditions(d => d.Overtime.ShouldBe(60), d => d.Difference.ShouldBe(0));
        days.Single(d => d.Date.Day == 3).Difference.ShouldBe(-420);
        days.Single(d => d.Date.Day == 4).ShouldSatisfyAllConditions(d => d.Excused.ShouldBeTrue(), d => d.Difference.ShouldBeNull());
        days.Single(d => d.Date.Day == 5).ShouldSatisfyAllConditions(d => d.DayOff.ShouldBeTrue(), d => d.Overtime.ShouldBe(120));
        days[0].ShouldSatisfyAllConditions(d => d.IsToday.ShouldBeTrue(), d => d.Difference.ShouldBeNull());
    }

    [Fact]
    public void Days_before_joining_ask_for_nothing()
    {
        var days = MyTime.Days(September, today: new DateOnly(2026, 9, 3), joinedOn: new DateOnly(2026, 9, 2), Calendar, [], new HashSet<DateOnly>(), false);

        days.Select(d => d.Date.Day).ShouldBe([3, 2]);
    }

    /// <summary>1,733.30 a month (10 an hour), worked 9 to 4 on the given days of September, as seen on <paramref name="today"/>.</summary>
    private static (SalaryCalculator.Result Pay, SalaryCalculator.Input Input) SeptemberPay(DateOnly today, bool ledgerApplies, params int[] worked)
    {
        var input = new SalaryCalculator.Input(
            September,
            today,
            new DateOnly(2025, 1, 1),
            ledgerApplies,
            [new SalaryCalculator.PayTerms(new DateOnly(2025, 1, 1), PayBasis.Monthly, 1733.30m, "USD", 10m)],
            Calendar,
            worked.Select((day, i) => Work(i + 1, day, 540, 420)).ToList(),
            []);
        return (SalaryCalculator.Calculate(input)!, input);
    }

    [Fact]
    public void A_salaried_month_in_progress_accrues_with_the_hours_required_so_far()
    {
        // Up to the 14th: 10 working days, 70h required. The 11th and the 14th are missed: 14h behind, and each whole
        // missed day would also cost its paid hour of break - 16h at 10 an hour, as Salaries would deduct it.
        var (pay, input) = SeptemberPay(new DateOnly(2026, 9, 15), ledgerApplies: true, 1, 2, 3, 4, 7, 8, 9, 10);

        MyTime.EstimateOf(pay, input, inProgress: true)
            .ShouldBe(new MyTime.Estimate(EarnedSoFar: 787.86m, FullMonth: 1573.30m, WouldLose: 160m));
        MyTime.EstimateOf(pay, input, inProgress: false).ShouldBe(new MyTime.Estimate(pay.Total, null, 0));
    }

    [Fact]
    public void Without_an_hours_ledger_a_salaried_month_accrues_with_the_working_days_passed()
    {
        // 10 of September's 22 working days have passed by the 15th; nothing is owed on the 1st.
        var (pay, input) = SeptemberPay(new DateOnly(2026, 9, 15), ledgerApplies: false);
        MyTime.EstimateOf(pay, input, inProgress: true).ShouldBe(new MyTime.Estimate(EarnedSoFar: 787.86m, FullMonth: 1733.30m, WouldLose: 0));

        var (firstDay, firstInput) = SeptemberPay(September, ledgerApplies: false);
        MyTime.EstimateOf(firstDay, firstInput, inProgress: true).EarnedSoFar.ShouldBe(0m);
    }

    [Fact]
    public void A_whole_missed_day_costs_its_paid_break_on_top_of_its_required_hours()
    {
        var (pay, input) = SeptemberPay(new DateOnly(2026, 9, 15), ledgerApplies: true, 1, 2, 3, 4, 7, 8, 9, 10);

        pay.Ledger!.Short.ShouldBe(2 * 420);
        SalaryCalculator.ShortfallMinutes(pay.Ledger, input.Calendar).ShouldBe(2 * 480);
    }

    [Fact]
    public void Ledger_status_reads_excused_short_behind_on_track_or_complete()
    {
        HoursLedger.Result Ledger(bool inProgress, int shortMinutes) => new(inProgress, 420, 0, 420 - shortMinutes, 0, 0, shortMinutes, new Dictionary<long, int>(), []);

        MyTime.StatusOf(Ledger(false, 60), wholeMonthExcused: true).ShouldBe(MyTime.LedgerStatus.Excused);
        MyTime.StatusOf(Ledger(false, 60), false).ShouldBe(MyTime.LedgerStatus.Short);
        MyTime.StatusOf(Ledger(true, 60), false).ShouldBe(MyTime.LedgerStatus.Behind);
        MyTime.StatusOf(Ledger(true, 0), false).ShouldBe(MyTime.LedgerStatus.OnTrack);
        MyTime.StatusOf(Ledger(false, 0), false).ShouldBe(MyTime.LedgerStatus.Complete);
    }

    [Fact]
    public void Ledger_months_run_back_to_the_joining_month_twelve_a_page()
    {
        var today = new DateOnly(2026, 9, 29);

        var (first, total) = MyTime.MonthsPage(joinedOn: new DateOnly(2025, 3, 17), today, page: 1);
        total.ShouldBe(19);
        first.Count.ShouldBe(12);
        first[0].ShouldBe(September);
        first[^1].ShouldBe(new DateOnly(2025, 10, 1));

        MyTime.MonthsPage(new DateOnly(2025, 3, 17), today, page: 2).Months.ShouldBe(
            Enumerable.Range(0, 7).Select(i => new DateOnly(2025, 9, 1).AddMonths(-i)).ToList());
        MyTime.MonthsPage(new DateOnly(2025, 3, 17), today, page: 3).Months.ShouldBeEmpty();
        MyTime.MonthsPage(new DateOnly(2026, 9, 28), today, page: 1).Months.ShouldBe([September]);
    }

    [Fact]
    public void A_leaver_s_ledger_months_end_with_the_month_they_left()
    {
        var (months, total) = MyTime.MonthsPage(new DateOnly(2026, 1, 5), new DateOnly(2026, 9, 29), page: 1, leftOn: new DateOnly(2026, 6, 25));

        total.ShouldBe(6);
        months[0].ShouldBe(new DateOnly(2026, 6, 1));
        months[^1].ShouldBe(new DateOnly(2026, 1, 1));
    }
}
