using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;
using Action = ID_Indetails.api.Business.Projects.ProjectStatusRules.Action;

namespace ID_Indetails.UnitTests.Business;

public class ProjectStatusRulesTests
{
    [Theory]
    [InlineData(ProjectStatus.Active, Action.Hold, ProjectStatus.Hold)]
    [InlineData(ProjectStatus.Hold, Action.Resume, ProjectStatus.Active)]
    [InlineData(ProjectStatus.Active, Action.Complete, ProjectStatus.Completed)]
    [InlineData(ProjectStatus.Hold, Action.Complete, ProjectStatus.Completed)]
    [InlineData(ProjectStatus.Active, Action.Archive, ProjectStatus.Archived)]
    [InlineData(ProjectStatus.Hold, Action.Archive, ProjectStatus.Archived)]
    [InlineData(ProjectStatus.Completed, Action.Archive, ProjectStatus.Archived)]
    [InlineData(ProjectStatus.Completed, Action.Reopen, ProjectStatus.Active)]
    [InlineData(ProjectStatus.Archived, Action.Reopen, ProjectStatus.Active)]
    public void Allowed_changes(ProjectStatus from, Action action, ProjectStatus to)
    {
        var (transition, denial) = ProjectStatusRules.Plan(from, action);

        denial.ShouldBeNull();
        transition!.To.ShouldBe(to);
    }

    [Theory]
    [InlineData(ProjectStatus.Completed, Action.Hold)]
    [InlineData(ProjectStatus.Archived, Action.Complete)]
    [InlineData(ProjectStatus.Hold, Action.Reopen)]
    [InlineData(ProjectStatus.Completed, Action.Resume)]
    public void Other_changes_are_refused(ProjectStatus from, Action action)
        => ProjectStatusRules.Plan(from, action).Denial!.Code.ShouldBe(ErrorCodes.InvalidStatusChange);

    [Theory]
    [InlineData(ProjectStatus.Hold, Action.Hold)]
    [InlineData(ProjectStatus.Completed, Action.Complete)]
    [InlineData(ProjectStatus.Archived, Action.Archive)]
    [InlineData(ProjectStatus.Active, Action.Resume)]
    public void Changing_to_the_current_status_is_refused(ProjectStatus from, Action action)
        => ProjectStatusRules.Plan(from, action).Denial!.Code.ShouldBe(ErrorCodes.StatusUnchanged);

    [Fact]
    public void Completing_or_archiving_unfinished_work_finalises_and_reopening_undoes_it()
    {
        ProjectStatusRules.Plan(ProjectStatus.Active, Action.Complete).Transition!.Finalises.ShouldBeTrue();
        ProjectStatusRules.Plan(ProjectStatus.Hold, Action.Archive).Transition!.Finalises.ShouldBeTrue();
        ProjectStatusRules.Plan(ProjectStatus.Completed, Action.Archive).Transition!.Finalises.ShouldBeFalse();
        ProjectStatusRules.Plan(ProjectStatus.Active, Action.Hold).Transition!.Finalises.ShouldBeFalse();
        ProjectStatusRules.Plan(ProjectStatus.Archived, Action.Reopen).Transition!.Reopens.ShouldBeTrue();
        ProjectStatusRules.Plan(ProjectStatus.Hold, Action.Resume).Transition!.Reopens.ShouldBeFalse();
    }

    [Fact]
    public void Resuming_from_hold_is_logged_apart_from_reopening()
    {
        ProjectStatusRules.Plan(ProjectStatus.Hold, Action.Resume).Transition!.EventKind.ShouldBe(ActivityEventKinds.ProjectResumed);
        ProjectStatusRules.Plan(ProjectStatus.Completed, Action.Reopen).Transition!.EventKind.ShouldBe(ActivityEventKinds.ProjectReopened);
    }

    [Theory]
    [InlineData(3, 1, ProjectStatus.Active, 33)]
    [InlineData(3, 2, ProjectStatus.Active, 67)]
    [InlineData(8, 1, ProjectStatus.Active, 13)]
    [InlineData(4, 4, ProjectStatus.Hold, 100)]
    [InlineData(0, 0, ProjectStatus.Active, 0)]
    [InlineData(0, 0, ProjectStatus.Completed, 100)]
    [InlineData(0, 0, ProjectStatus.Archived, 100)]
    public void Progress_is_done_over_total_and_full_for_finished_projects_without_requirements(int total, int done, ProjectStatus status, int expected)
        => ProgressCalculator.Percent(total, done, status).ShouldBe((byte)expected);
}
