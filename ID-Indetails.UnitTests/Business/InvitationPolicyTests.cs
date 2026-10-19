using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class InvitationPolicyTests
{
    private const string Design = DomainCodes.Design;
    private const string Execution = DomainCodes.Execution;

    private static UserAccess Inviter(string role, string? domain, IReadOnlyList<string> visible, params string[] permissions)
        => new(1, role, domain, permissions, IsJoker: false, visible);

    private static readonly UserAccess SuperAdmin = Inviter(
        RoleCodes.SuperAdmin, null, DomainCodes.All, PermissionCodes.EmployeeInvite, PermissionCodes.EmployeeInviteSuperAdmin);

    private static readonly UserAccess DesignAdmin = Inviter(RoleCodes.Admin, Design, [Design], PermissionCodes.EmployeeInvite);

    [Theory]
    [InlineData(RoleCodes.Admin, Design)]
    [InlineData(RoleCodes.Admin, Execution)]
    [InlineData(RoleCodes.Employee, Design)]
    [InlineData(RoleCodes.Employee, Execution)]
    public void Super_admin_can_invite_admins_and_employees_into_either_domain(string role, string domain)
    {
        var decision = InvitationPolicy.Evaluate(SuperAdmin, role, domain);

        decision.Allowed.ShouldBeTrue();
        decision.DomainCode.ShouldBe(domain);
    }

    [Fact]
    public void Super_admin_can_invite_another_super_admin_without_a_domain()
    {
        var decision = InvitationPolicy.Evaluate(SuperAdmin, RoleCodes.SuperAdmin, null);

        decision.Allowed.ShouldBeTrue();
        decision.DomainCode.ShouldBeNull();
    }

    [Fact]
    public void A_super_admin_cannot_be_given_a_domain()
        => InvitationPolicy.Evaluate(SuperAdmin, RoleCodes.SuperAdmin, Design).Allowed.ShouldBeFalse();

    [Fact]
    public void Someone_who_sees_both_domains_must_choose_one()
        => InvitationPolicy.Evaluate(SuperAdmin, RoleCodes.Employee, null).Allowed.ShouldBeFalse();

    [Theory]
    [InlineData(RoleCodes.Admin)]
    [InlineData(RoleCodes.Employee)]
    public void Admin_invites_into_their_own_domain_even_when_none_is_given(string role)
    {
        var decision = InvitationPolicy.Evaluate(DesignAdmin, role, null);

        decision.Allowed.ShouldBeTrue();
        decision.DomainCode.ShouldBe(Design);
    }

    [Fact]
    public void Admin_cannot_invite_into_a_domain_they_do_not_see()
        => InvitationPolicy.Evaluate(DesignAdmin, RoleCodes.Employee, Execution).Allowed.ShouldBeFalse();

    [Fact]
    public void Inviting_a_super_admin_needs_its_own_permission()
        => InvitationPolicy.Evaluate(DesignAdmin, RoleCodes.SuperAdmin, null).Allowed.ShouldBeFalse();

    [Fact]
    public void Admin_granted_the_super_admin_invite_permission_can_invite_a_super_admin()
    {
        var joker = Inviter(RoleCodes.Admin, Design, [Design], PermissionCodes.EmployeeInvite, PermissionCodes.EmployeeInviteSuperAdmin);

        InvitationPolicy.Evaluate(joker, RoleCodes.SuperAdmin, null).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void Super_admin_invite_permission_alone_does_not_invite_admins_or_employees()
    {
        var inviter = Inviter(RoleCodes.Admin, Design, [Design], PermissionCodes.EmployeeInviteSuperAdmin);

        InvitationPolicy.Evaluate(inviter, RoleCodes.Employee, Design).Allowed.ShouldBeFalse();
    }

    [Fact]
    public void Employee_without_the_permission_cannot_invite_anyone()
        => InvitationPolicy.Evaluate(Inviter(RoleCodes.Employee, Design, [Design]), RoleCodes.Employee, Design)
            .Allowed.ShouldBeFalse();

    [Fact]
    public void Employee_granted_the_invite_permission_can_invite_into_their_domain()
    {
        var joker = Inviter(RoleCodes.Employee, Design, [Design], PermissionCodes.EmployeeInvite);

        InvitationPolicy.Evaluate(joker, RoleCodes.Employee, null).DomainCode.ShouldBe(Design);
    }

    [Fact]
    public void Cross_domain_inviter_can_invite_into_the_other_domain_but_must_choose()
    {
        var crossDomain = Inviter(RoleCodes.Admin, Design, [Design, Execution], PermissionCodes.EmployeeInvite);

        InvitationPolicy.Evaluate(crossDomain, RoleCodes.Employee, Execution).DomainCode.ShouldBe(Execution);
        InvitationPolicy.Evaluate(crossDomain, RoleCodes.Employee, null).Allowed.ShouldBeFalse();
    }

    [Fact]
    public void Unknown_role_or_domain_is_refused()
    {
        InvitationPolicy.Evaluate(SuperAdmin, "owner", Design).Allowed.ShouldBeFalse();
        InvitationPolicy.Evaluate(SuperAdmin, RoleCodes.Employee, "marketing").Allowed.ShouldBeFalse();
    }

    [Fact]
    public void Website_staff_are_invited_as_admins_only()
    {
        InvitationPolicy.Evaluate(SuperAdmin, RoleCodes.Admin, DomainCodes.Website).DomainCode.ShouldBe(DomainCodes.Website);
        InvitationPolicy.Evaluate(SuperAdmin, RoleCodes.Employee, DomainCodes.Website).DenialReason.ShouldBe("Website staff are admins.");
    }

    [Fact]
    public void Seeing_the_website_too_does_not_make_an_admin_choose_a_domain()
    {
        var inviter = Inviter(RoleCodes.Admin, Design, [Design, DomainCodes.Website], PermissionCodes.EmployeeInvite);

        InvitationPolicy.Evaluate(inviter, RoleCodes.Employee, null).DomainCode.ShouldBe(Design);
    }
}
