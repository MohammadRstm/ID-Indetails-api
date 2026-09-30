using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Business.WorkingTime;
using Shouldly;
using Work = ID_Indetails.api.Business.Payroll.Overtime.Work;

namespace ID_Indetails.UnitTests.Business;

/// <summary>Monday to Friday, 09:00 to 17:00 with an hour's paid break: 7 required hours.</summary>
public class OvertimePreviewTests
{
    private static readonly PersonCalendar Calendar = new([new PersonCalendar.Policy(new(2000, 1, 1), 62, 540, 1020, 60, 1.5m)], []);
    private static readonly DateOnly Monday = new(2026, 6, 1);

    private static Work Draft(DateOnly day, int startHour, int minutes) => new(OvertimePreview.DraftId, day, startHour * 60, minutes);

    [Fact]
    public void Work_before_or_after_hours_is_overtime_on_that_side_of_the_day()
    {
        var early = OvertimePreview.Of(Draft(Monday, 8, 120), [], Calendar);
        early.ShouldBe(new OvertimePreview.Result(120, 60, 60, 0, 0, Calendar.HoursOn(Monday)));

        // From 23:00 for two hours: after hours, the hour past midnight included.
        var late = OvertimePreview.Of(Draft(Monday, 23, 120), [], Calendar);
        late.Overtime.ShouldBe(120);
        late.AfterHours.ShouldBe(120);
    }

    [Fact]
    public void Past_the_required_hours_counts_with_the_rest_of_the_day()
    {
        // Already 7 hours logged within hours, so another hour at 16:00 is all past what the day asks for.
        var draft = OvertimePreview.Of(Draft(Monday, 16, 60), [new Work(5, Monday, 540, 420)], Calendar);

        draft.Overtime.ShouldBe(60);
        draft.BeforeHours.ShouldBe(0);
        draft.AfterHours.ShouldBe(0);
        draft.PastRequired.ShouldBe(60);

        // Other days don't count.
        OvertimePreview.Of(Draft(Monday, 16, 60), [new Work(5, Monday.AddDays(1), 540, 420)], Calendar).Overtime.ShouldBe(0);
    }

    [Fact]
    public void Everything_on_a_day_off_is_overtime()
    {
        var saturday = OvertimePreview.Of(Draft(new DateOnly(2026, 6, 6), 10, 90), [], Calendar);

        saturday.Overtime.ShouldBe(90);
        saturday.Hours.ShouldBeNull();
    }
}
