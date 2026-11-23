using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.WorkEntries;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class WorkEntryRulesTests
{
    private static readonly DateTime Nine = new(2026, 9, 28, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Work_is_logged_on_active_design_projects_and_on_hold_only_when_allowed()
    {
        WorkEntryRules.CheckProject(DomainCodes.Design, ProjectStatus.Active, holdLogging: false).ShouldBeNull();
        WorkEntryRules.CheckProject(DomainCodes.Design, ProjectStatus.Hold, holdLogging: true).ShouldBeNull();
        WorkEntryRules.CheckProject(DomainCodes.Design, ProjectStatus.Hold, holdLogging: false)!.Code.ShouldBe(ErrorCodes.ProjectNotAcceptingWork);
        WorkEntryRules.CheckProject(DomainCodes.Design, ProjectStatus.Completed, holdLogging: true)!.Code.ShouldBe(ErrorCodes.ProjectNotAcceptingWork);
        WorkEntryRules.CheckProject(DomainCodes.Design, ProjectStatus.Archived, holdLogging: true)!.Code.ShouldBe(ErrorCodes.ProjectNotAcceptingWork);
        WorkEntryRules.CheckProject(DomainCodes.Execution, ProjectStatus.Active, holdLogging: true)!.Code.ShouldBe(ErrorCodes.NotForThisDomain);
    }

    [Fact]
    public void A_description_is_required_when_the_domain_asks_and_future_ends_only_when_allowed()
    {
        WorkEntryRules.CheckEntry("  ", Nine, Nine, requireDescription: true, allowFuture: false)!.Code.ShouldBe(ErrorCodes.DescriptionRequired);
        WorkEntryRules.CheckEntry("", Nine, Nine, requireDescription: false, allowFuture: false).ShouldBeNull();
        WorkEntryRules.CheckEntry("Sketches", Nine.AddMinutes(1), Nine, requireDescription: true, allowFuture: false)!.Code.ShouldBe(ErrorCodes.EntryInFuture);
        WorkEntryRules.CheckEntry("Sketches", Nine.AddHours(3), Nine, requireDescription: true, allowFuture: true).ShouldBeNull();
    }

    [Fact]
    public void Overlaps_are_refused_but_back_to_back_entries_are_fine()
    {
        var others = new List<WorkEntryRules.Span> { new(1, Nine, Nine.AddHours(1), "Brand") };

        WorkEntryRules.CheckOverlap([new(0, Nine.AddHours(1), Nine.AddHours(2), "Web")], others).ShouldBeNull();
        WorkEntryRules.CheckOverlap([new(0, Nine.AddMinutes(30), Nine.AddHours(2), "Web")], others)!.Code.ShouldBe(ErrorCodes.EntryOverlaps);

        // An entry never clashes with its own old times, and moved entries are checked at their new times.
        WorkEntryRules.CheckOverlap([new(1, Nine.AddMinutes(10), Nine.AddHours(1), "Brand")], others).ShouldBeNull();
        WorkEntryRules.CheckOverlap(
            [new(0, Nine, Nine.AddHours(1), "Web"), new(1, Nine.AddHours(1), Nine.AddHours(2), "Brand")],
            others).ShouldBeNull();
        WorkEntryRules.CheckOverlap(
            [new(0, Nine, Nine.AddHours(1), "Web"), new(2, Nine.AddMinutes(30), Nine.AddHours(2), "Brand")],
            []).ShouldNotBeNull();
    }

    [Fact]
    public void The_edit_window_counts_from_when_the_entry_was_logged()
    {
        WorkEntryRules.IsWithinEditWindow(Nine, Nine.AddHours(23), EditWindow.OneDay).ShouldBeTrue();
        WorkEntryRules.IsWithinEditWindow(Nine, Nine.AddDays(1), EditWindow.OneDay).ShouldBeFalse();
        WorkEntryRules.IsWithinEditWindow(Nine, Nine.AddDays(6), EditWindow.SevenDays).ShouldBeTrue();
        WorkEntryRules.IsWithinEditWindow(Nine, Nine.AddDays(7), EditWindow.SevenDays).ShouldBeFalse();
        WorkEntryRules.IsWithinEditWindow(Nine, Nine, EditWindow.None).ShouldBeFalse();
        WorkEntryRules.IsWithinEditWindow(Nine, Nine.AddYears(3), EditWindow.Unlimited).ShouldBeTrue();

        WorkEntryRules.CheckChangeable(Nine, Nine.AddDays(2), EditWindow.OneDay, ProjectStatus.Active, false)!.Code.ShouldBe(ErrorCodes.EditWindowClosed);
        WorkEntryRules.CheckChangeable(Nine, Nine, EditWindow.OneDay, ProjectStatus.Completed, false)!.Code.ShouldBe(ErrorCodes.ProjectNotAcceptingWork);
        WorkEntryRules.CheckChangeable(Nine, Nine, EditWindow.OneDay, ProjectStatus.Active, false).ShouldBeNull();
    }

    [Fact]
    public void An_entry_is_5_minutes_to_16_hours_in_whole_minutes()
    {
        var validator = new SaveWorkEntryRequestValidator();
        SaveWorkEntryRequest Entry(DateTime start, DateTime end) => new() { ProjectId = 1, Description = "Work", StartedAt = start, EndedAt = end };

        validator.Validate(Entry(Nine, Nine.AddMinutes(5))).IsValid.ShouldBeTrue();
        validator.Validate(Entry(Nine, Nine.AddHours(16))).IsValid.ShouldBeTrue();
        validator.Validate(Entry(Nine, Nine.AddMinutes(4))).IsValid.ShouldBeFalse();
        validator.Validate(Entry(Nine, Nine.AddHours(16).AddMinutes(1))).IsValid.ShouldBeFalse();
        validator.Validate(Entry(Nine.AddSeconds(30), Nine.AddHours(1))).IsValid.ShouldBeFalse();
        validator.Validate(Entry(Nine, Nine.AddHours(1)) with { ProjectId = 0 }).IsValid.ShouldBeFalse();
        validator.Validate(Entry(Nine, Nine.AddHours(1)) with
        {
            Moves = [new() { Id = 5, StartedAt = Nine, EndedAt = Nine.AddHours(1) }, new() { Id = 5, StartedAt = Nine, EndedAt = Nine.AddHours(1) }],
        }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Times_without_a_zone_are_taken_as_utc()
    {
        WorkEntryRules.AsUtc(new DateTime(2026, 9, 28, 6, 0, 0, DateTimeKind.Unspecified)).ShouldBe(Nine);
        WorkEntryRules.AsUtc(Nine).Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void Past_months_never_change()
    {
        // 1 September 2026 begins at 21:00 UTC on 31 August in Beirut.
        var monthStart = new DateTime(2026, 8, 31, 21, 0, 0, DateTimeKind.Utc);

        var open = new OpenMonths(monthStart, []);

        open.IsOpen(monthStart).ShouldBeTrue();
        open.IsOpen(monthStart.AddMinutes(-1)).ShouldBeFalse();
        WorkEntryRules.CheckOpenMonth(open, monthStart.AddDays(2)).ShouldBeNull();
        WorkEntryRules.CheckOpenMonth(open, monthStart.AddMinutes(-1))!.Code.ShouldBe(ErrorCodes.MonthClosed);

        // A move is refused when either its current or its new start is in a closed month.
        WorkEntryRules.CheckOpenMonth(open, monthStart.AddDays(2), monthStart.AddDays(-2))!.Code.ShouldBe(ErrorCodes.MonthClosed);
        WorkEntryRules.CheckOpenMonth(open, monthStart.AddDays(-2), monthStart.AddDays(2))!.Kind.ShouldBe(ID_Indetails.api.Business.Common.DenialKind.BusinessRule);
    }

    [Fact]
    public void A_reopened_month_takes_work_while_it_stays_open()
    {
        // September 2026 reopened: from 31 August 21:00 UTC to 30 September 21:00 UTC; the open month is November
        var september = new OpenMonths.Window(new DateTime(2026, 8, 31, 21, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 30, 21, 0, 0, DateTimeKind.Utc));
        var open = new OpenMonths(new DateTime(2026, 10, 31, 22, 0, 0, DateTimeKind.Utc), [september]);

        open.IsOpen(september.FromUtc.AddDays(10)).ShouldBeTrue();
        open.IsOpen(september.ToUtc.AddDays(10)).ShouldBeFalse();
        open.IsOpen(september.FromUtc.AddMinutes(-1)).ShouldBeFalse();
    }

    [Fact]
    public void Only_a_closed_month_is_reopened_and_until_today_or_later()
    {
        var today = new DateOnly(2026, 11, 23);
        ReopenRules.Check(new DateOnly(2026, 9, 1), today, today).ShouldBeNull();
        ReopenRules.Check(new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31), today).ShouldBeNull();
        ReopenRules.Check(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), today)!.Code.ShouldBe(ErrorCodes.MonthNotClosed);
        ReopenRules.Check(new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 22), today)!.Code.ShouldBe(ErrorCodes.ReopenUntilInPast);
        ReopenRules.Check(new DateOnly(2026, 10, 5), today, today)!.Code.ShouldBe(ErrorCodes.ValidationFailed);
        // the month just ended stays open through the 1st, so it isn't closed yet
        ReopenRules.Check(new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 1))!.Code.ShouldBe(ErrorCodes.MonthNotClosed);
    }

    [Theory]
    // On the 1st, the month just ended is still open, so last night's work can be logged after midnight.
    [InlineData("2026-10-01", "2026-09-01")]
    [InlineData("2026-10-02", "2026-10-01")]
    [InlineData("2026-10-31", "2026-10-01")]
    [InlineData("2027-01-01", "2026-12-01")]
    public void The_month_just_ended_stays_open_through_the_first(string today, string openFrom)
        => WorkEntryRules.OpenFrom(DateOnly.Parse(today)).ShouldBe(DateOnly.Parse(openFrom));
}
