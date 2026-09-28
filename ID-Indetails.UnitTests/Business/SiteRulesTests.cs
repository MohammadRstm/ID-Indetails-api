using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class SiteRulesTests
{
    [Fact]
    public void Only_execution_projects_that_are_not_finalised_take_site_records()
    {
        SiteRules.CheckOpen(DomainCodes.Execution, ProjectStatus.Active, revenueLocked: false).ShouldBeNull();
        SiteRules.CheckOpen(DomainCodes.Execution, ProjectStatus.Hold, revenueLocked: false).ShouldBeNull();
        SiteRules.CheckOpen(DomainCodes.Execution, ProjectStatus.Completed, revenueLocked: true)!.Code.ShouldBe(ErrorCodes.ProjectLocked);
        SiteRules.CheckOpen(DomainCodes.Execution, ProjectStatus.Archived, revenueLocked: true)!.Code.ShouldBe(ErrorCodes.ProjectLocked);
        SiteRules.CheckOpen(DomainCodes.Execution, ProjectStatus.Active, revenueLocked: true)!.Code.ShouldBe(ErrorCodes.ProjectLocked);
        SiteRules.CheckOpen(DomainCodes.Design, ProjectStatus.Active, revenueLocked: false)!.Code.ShouldBe(ErrorCodes.NotForThisDomain);
    }

    [Fact]
    public void People_change_their_own_records_and_managers_anyone_s()
    {
        SiteRules.CheckCanChange(isOwn: true, canManageAll: false).ShouldBeNull();
        SiteRules.CheckCanChange(isOwn: false, canManageAll: true).ShouldBeNull();
        SiteRules.CheckCanChange(isOwn: false, canManageAll: false)!.Code.ShouldBe(ErrorCodes.Forbidden);
    }
}
