using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Requirements;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Requirements;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Requirements;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class RequirementRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);

    [Theory]
    [InlineData(ProjectStatus.Active, true)]
    [InlineData(ProjectStatus.Hold, true)]
    [InlineData(ProjectStatus.Completed, false)]
    [InlineData(ProjectStatus.Archived, false)]
    public void Requirements_change_only_while_the_project_is_open(ProjectStatus status, bool editable)
        => (RequirementRules.CheckEditable(status) is null).ShouldBe(editable);

    [Fact]
    public void Assignees_are_active_or_invited_employees_of_the_project_domain()
    {
        RequirementRules.Person[] people =
        [
            new(1, RoleCodes.Employee, DomainCodes.Design, UserStatus.Active),
            new(2, RoleCodes.Employee, DomainCodes.Design, UserStatus.Invited),
            new(3, RoleCodes.Admin, DomainCodes.Design, UserStatus.Active),
            new(4, RoleCodes.Employee, DomainCodes.Execution, UserStatus.Active),
            new(5, RoleCodes.Employee, DomainCodes.Design, UserStatus.Deactivated),
        ];

        RequirementRules.CheckAssignees([1, 2], people, DomainCodes.Design).ShouldBeNull();
        RequirementRules.CheckAssignees([], people, DomainCodes.Design).ShouldBeNull();
        foreach (var refused in new long[] { 3, 4, 5, 99 })
        {
            RequirementRules.CheckAssignees([1, refused], people, DomainCodes.Design)!.Code.ShouldBe(ErrorCodes.AssigneeNotAllowed);
        }
    }

    [Fact]
    public void Only_open_requirements_past_their_deadline_are_overdue()
    {
        RequirementRules.IsOverdue(Today.AddDays(-1), isDone: false, Today).ShouldBeTrue();
        RequirementRules.IsOverdue(Today, isDone: false, Today).ShouldBeFalse();
        RequirementRules.IsOverdue(Today.AddDays(-1), isDone: true, Today).ShouldBeFalse();
        RequirementRules.IsOverdue(null, isDone: false, Today).ShouldBeFalse();
    }

    [Fact]
    public void Managers_tick_anything_and_assignees_only_their_own_ticks()
    {
        const long me = 7, admin = 1;

        RequirementRules.CheckTick(canManage: true, isAssignee: false, isDone: true, done: false, doneById: me, actorId: admin).ShouldBeNull();
        RequirementRules.CheckTick(false, isAssignee: true, isDone: false, done: true, doneById: null, actorId: me).ShouldBeNull();
        RequirementRules.CheckTick(false, isAssignee: true, isDone: true, done: false, doneById: me, actorId: me).ShouldBeNull();
        RequirementRules.CheckTick(false, isAssignee: false, isDone: false, done: true, doneById: null, actorId: me)!
            .Kind.ShouldBe(DenialKind.Forbidden);
        RequirementRules.CheckTick(false, isAssignee: true, isDone: true, done: false, doneById: admin, actorId: me)!
            .Kind.ShouldBe(DenialKind.Forbidden);

        // Unticking an open one changes nothing, so it isn't refused.
        RequirementRules.CheckTick(false, isAssignee: true, isDone: false, done: false, doneById: null, actorId: me).ShouldBeNull();
    }

    [Fact]
    public void A_requirement_needs_text_and_valid_assignees()
    {
        var validator = new SaveRequirementRequestValidator();

        validator.Validate(new SaveRequirementRequest { Text = "  " }).IsValid.ShouldBeFalse();
        validator.Validate(new SaveRequirementRequest { Text = new string('x', 501) }).IsValid.ShouldBeFalse();
        validator.Validate(new SaveRequirementRequest { Text = "Plans", AssigneeIds = [0] }).IsValid.ShouldBeFalse();
        validator.Validate(new SaveRequirementRequest { Text = "Plans", AssigneeIds = [4, 5] }).IsValid.ShouldBeTrue();
    }
}
