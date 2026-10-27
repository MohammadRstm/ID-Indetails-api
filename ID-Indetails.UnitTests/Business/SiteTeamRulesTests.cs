using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class SiteTeamRulesTests
{
    private static SiteTeamRules.Candidate Staff(long id, string role = RoleCodes.Employee, string? domain = DomainCodes.Execution, UserStatus status = UserStatus.Active)
        => new(id, $"Person {id}", role, domain, status);

    [Fact]
    public void A_site_is_overseen_by_at_least_one_active_execution_employee_each_once()
    {
        SiteTeamRules.CheckStaff([1, 2], [Staff(1), Staff(2)]).ShouldBeNull();
        SiteTeamRules.CheckStaff([], []).ShouldSatisfyAllConditions(d => d!.Field.ShouldBe("staffIds"), d => d!.Message.ShouldContain("at least one"));
        SiteTeamRules.CheckStaff([1, 1], [Staff(1)])!.Message.ShouldContain("once");
        SiteTeamRules.CheckStaff([1, 9], [Staff(1)])!.Message.ShouldContain("doesn't exist");
        SiteTeamRules.CheckStaff([3], [Staff(3, domain: DomainCodes.Design)])!.Message.ShouldContain("Person 3");
        SiteTeamRules.CheckStaff([4], [Staff(4, role: RoleCodes.Admin)]).ShouldNotBeNull();
        SiteTeamRules.CheckStaff([5], [Staff(5, status: UserStatus.Deactivated)]).ShouldNotBeNull();
        SiteTeamRules.CheckStaff([6], [Staff(6, status: UserStatus.Invited)]).ShouldNotBeNull();
    }

    [Fact]
    public void Only_the_team_records_on_a_site()
    {
        SiteTeamRules.CheckOnTeam(true).ShouldBeNull();
        SiteTeamRules.CheckOnTeam(false)!.Code.ShouldBe(ErrorCodes.NotOnSiteTeam);
    }

    [Fact]
    public void A_log_is_changed_until_the_end_of_the_day_it_was_written()
    {
        var today = new DateOnly(2026, 10, 26);
        SiteTeamRules.IsEditable(today, today).ShouldBeTrue();
        SiteTeamRules.CheckEditable(today.AddDays(-1), today)!.Code.ShouldBe(ErrorCodes.SiteLogLocked);
    }
}
