using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class InvitationPolicyTests
{
    private const string Design = DomainCodes.Design;
    private const string Execution = DomainCodes.Execution;

    [Theory]
    [InlineData(RoleCodes.Admin, Design)]
    [InlineData(RoleCodes.Admin, Execution)]
    [InlineData(RoleCodes.Employee, Design)]
    [InlineData(RoleCodes.Employee, Execution)]
    public void Super_admin_can_invite_admins_and_employees_into_either_domain(string role, string domain)
    {
        var decision = InvitationPolicy.Evaluate(RoleCodes.SuperAdmin, null, role, domain);

        decision.Allowed.ShouldBeTrue();
        decision.DomainCode.ShouldBe(domain);
    }

    [Fact]
    public void Super_admin_can_invite_another_super_admin_without_a_domain()
    {
        var decision = InvitationPolicy.Evaluate(RoleCodes.SuperAdmin, null, RoleCodes.SuperAdmin, null);

        decision.Allowed.ShouldBeTrue();
        decision.DomainCode.ShouldBeNull();
    }

    [Fact]
    public void Super_admin_cannot_give_a_super_admin_a_domain()
        => InvitationPolicy.Evaluate(RoleCodes.SuperAdmin, null, RoleCodes.SuperAdmin, Design).Allowed.ShouldBeFalse();

    [Fact]
    public void Super_admin_must_choose_a_domain_for_admins_and_employees()
        => InvitationPolicy.Evaluate(RoleCodes.SuperAdmin, null, RoleCodes.Employee, null).Allowed.ShouldBeFalse();

    [Theory]
    [InlineData(RoleCodes.Admin)]
    [InlineData(RoleCodes.Employee)]
    public void Admin_invites_into_their_own_domain_even_when_none_is_given(string role)
    {
        var decision = InvitationPolicy.Evaluate(RoleCodes.Admin, Design, role, null);

        decision.Allowed.ShouldBeTrue();
        decision.DomainCode.ShouldBe(Design);
    }

    [Fact]
    public void Admin_cannot_invite_into_the_other_domain()
        => InvitationPolicy.Evaluate(RoleCodes.Admin, Design, RoleCodes.Employee, Execution).Allowed.ShouldBeFalse();

    [Fact]
    public void Admin_cannot_invite_a_super_admin()
        => InvitationPolicy.Evaluate(RoleCodes.Admin, Design, RoleCodes.SuperAdmin, null).Allowed.ShouldBeFalse();

    [Fact]
    public void Employee_cannot_invite_anyone()
        => InvitationPolicy.Evaluate(RoleCodes.Employee, Design, RoleCodes.Employee, Design).Allowed.ShouldBeFalse();

    [Fact]
    public void Unknown_role_or_domain_is_refused()
    {
        InvitationPolicy.Evaluate(RoleCodes.SuperAdmin, null, "owner", Design).Allowed.ShouldBeFalse();
        InvitationPolicy.Evaluate(RoleCodes.SuperAdmin, null, RoleCodes.Employee, "marketing").Allowed.ShouldBeFalse();
    }
}
