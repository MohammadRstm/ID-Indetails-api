using ID_Indetails.api.Business.Labour;
using Shouldly;
using Day = ID_Indetails.api.Business.Labour.LabourRules.Day;

namespace ID_Indetails.UnitTests.Business;

public class LabourResubmissionTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly DateTime Submitted = new(2026, 10, 5, 14, 30, 0, 123, DateTimeKind.Utc);

    private static readonly Day[] Week = [new(Monday, 4, 25m), new(Monday.AddDays(1), 5, 25m)];

    [Fact]
    public void An_unreported_week_is_version_0_and_a_reported_one_moves_with_each_change()
    {
        LabourResubmission.VersionOf(null, null).ShouldBe("0");
        var submitted = LabourResubmission.VersionOf(Submitted, null);
        var updated = LabourResubmission.VersionOf(Submitted, Submitted.AddMinutes(5));

        submitted.ShouldNotBe("0");
        updated.ShouldNotBe(submitted);
        LabourResubmission.VersionOf(Submitted, null).ShouldBe(submitted);
    }

    [Fact]
    public void A_change_is_always_recorded_after_the_previous_one_in_whole_milliseconds()
    {
        LabourResubmission.ChangedAt(Submitted, Submitted.AddMinutes(1)).ShouldBe(Submitted.AddMinutes(1));
        LabourResubmission.ChangedAt(Submitted, Submitted.AddTicks(4000)).ShouldBe(Submitted.AddMilliseconds(1));
        LabourResubmission.ChangedAt(Submitted, Submitted.AddMilliseconds(-3)).ShouldBe(Submitted.AddMilliseconds(1));
        LabourResubmission.ChangedAt(Submitted, Submitted.AddSeconds(2).AddTicks(7)).Ticks.ShouldBe(Submitted.AddSeconds(2).Ticks);
    }

    [Fact]
    public void The_same_report_is_the_same_days_workers_and_pay_and_note_in_any_order()
    {
        LabourResubmission.SameReport(Week, "Slab", Week.Reverse().ToList(), " Slab ").ShouldBeTrue();
        LabourResubmission.SameReport(Week, null, Week, "  ").ShouldBeTrue();
        LabourResubmission.SameReport([new(Monday, 4, 25.00m)], null, [new(Monday, 4, 25m)], null).ShouldBeTrue();
        LabourResubmission.SameReport(Week, "Slab", Week, "Roof").ShouldBeFalse();
        LabourResubmission.SameReport(Week, null, [Week[0]], null).ShouldBeFalse();
        LabourResubmission.SameReport(Week, null, [Week[0], new(Monday.AddDays(1), 6, 25m)], null).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true, true, "100", null, LabourSubmission.Unchanged)]
    [InlineData(true, true, "100", "50", LabourSubmission.Unchanged)]
    [InlineData(true, false, "100", "50", LabourSubmission.Conflict)]
    [InlineData(true, false, "100", "0", LabourSubmission.Conflict)]
    [InlineData(true, false, "100", "100", LabourSubmission.Save)]
    [InlineData(true, false, "100", null, LabourSubmission.Save)]
    [InlineData(false, false, "0", "0", LabourSubmission.Save)]
    [InlineData(false, false, "0", null, LabourSubmission.Save)]
    public void What_a_submission_does(bool reported, bool same, string current, string? expected, LabourSubmission outcome)
        => LabourResubmission.Decide(reported, same, current, expected).ShouldBe(outcome);
}
