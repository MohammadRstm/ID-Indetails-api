using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;
using Target = ID_Indetails.api.Business.Employees.EmployeeRules.Target;

namespace ID_Indetails.UnitTests.Business;

public class EmployeeRulesTests
{
    private const string Design = DomainCodes.Design;
    private const string Execution = DomainCodes.Execution;

    private static readonly UserAccess SuperAdmin = new(
        1, RoleCodes.SuperAdmin, null,
        [PermissionCodes.EmployeeManage, PermissionCodes.EmployeeInviteSuperAdmin], false, DomainCodes.All);

    private static readonly UserAccess DesignAdmin = new(
        2, RoleCodes.Admin, Design, [PermissionCodes.EmployeeManage], false, [Design]);

    private static readonly UserAccess CrossDomainAdmin = new(
        3, RoleCodes.Admin, Design, [PermissionCodes.EmployeeManage], true, [Design, Execution]);

    private static Target Employee(string domain = Design, UserStatus status = UserStatus.Active) =>
        new(10, RoleCodes.Employee, domain, status);

    private static Target SuperAdminTarget(UserStatus status = UserStatus.Active) =>
        new(11, RoleCodes.SuperAdmin, null, status);

    [Fact]
    public void Super_admins_are_visible_only_to_callers_who_see_both_domains()
    {
        EmployeeRules.CanSee(SuperAdmin, null).ShouldBeTrue();
        EmployeeRules.CanSee(CrossDomainAdmin, null).ShouldBeTrue();
        EmployeeRules.CanSee(DesignAdmin, null).ShouldBeFalse();
    }

    [Fact]
    public void People_of_another_domain_are_not_visible()
    {
        EmployeeRules.CanSee(DesignAdmin, Design).ShouldBeTrue();
        EmployeeRules.CanSee(DesignAdmin, Execution).ShouldBeFalse();
    }

    [Fact]
    public void Changing_a_super_admin_needs_the_super_admin_invite_permission()
    {
        EmployeeRules.CheckCanChange(CrossDomainAdmin, SuperAdminTarget())!.Code.ShouldBe(ErrorCodes.EmployeeChangeNotAllowed);
        EmployeeRules.CheckCanChange(SuperAdmin, SuperAdminTarget()).ShouldBeNull();
        EmployeeRules.CheckCanChange(DesignAdmin, Employee()).ShouldBeNull();
    }

    [Fact]
    public void Nobody_deactivates_themselves()
        => EmployeeRules.CheckDeactivate(DesignAdmin, new Target(DesignAdmin.UserId, RoleCodes.Admin, Design, UserStatus.Active), 2)!
            .Kind.ShouldBe(DenialKind.Forbidden);

    [Fact]
    public void The_last_active_super_admin_cannot_be_deactivated()
    {
        EmployeeRules.CheckDeactivate(SuperAdmin, SuperAdminTarget(), activeSuperAdmins: 1)!.Code.ShouldBe(ErrorCodes.LastSuperAdmin);
        EmployeeRules.CheckDeactivate(SuperAdmin, SuperAdminTarget(), activeSuperAdmins: 2).ShouldBeNull();
    }

    [Fact]
    public void Deactivating_twice_or_reactivating_an_active_account_is_refused()
    {
        EmployeeRules.CheckDeactivate(DesignAdmin, Employee(status: UserStatus.Deactivated), 1)!.Code
            .ShouldBe(ErrorCodes.UserAlreadyDeactivated);
        EmployeeRules.CheckReactivate(DesignAdmin, Employee())!.Code.ShouldBe(ErrorCodes.UserNotDeactivated);
        EmployeeRules.CheckReactivate(DesignAdmin, Employee(status: UserStatus.Deactivated)).ShouldBeNull();
    }

    [Fact]
    public void Reactivated_accounts_return_to_active_or_to_invited()
    {
        EmployeeRules.ReactivatedStatus(hasPassword: true).ShouldBe(UserStatus.Active);
        EmployeeRules.ReactivatedStatus(hasPassword: false).ShouldBe(UserStatus.Invited);
    }

    [Fact]
    public void Employee_to_admin_keeps_the_domain()
    {
        var (decision, denial) = EmployeeRules.CheckRoleChange(DesignAdmin, Employee(), RoleCodes.Admin, null, 1);

        denial.ShouldBeNull();
        decision.ShouldBe(new EmployeeRules.RoleDecision(RoleCodes.Admin, Design));
    }

    [Fact]
    public void A_role_change_cannot_also_move_domains()
        => EmployeeRules.CheckRoleChange(SuperAdmin, Employee(), RoleCodes.Admin, Execution, 1).Denial!
            .Kind.ShouldBe(DenialKind.Validation);

    [Fact]
    public void Making_a_super_admin_needs_the_super_admin_invite_permission_and_removes_the_domain()
    {
        EmployeeRules.CheckRoleChange(DesignAdmin, Employee(), RoleCodes.SuperAdmin, null, 1).Denial!
            .Code.ShouldBe(ErrorCodes.EmployeeChangeNotAllowed);

        EmployeeRules.CheckRoleChange(SuperAdmin, Employee(), RoleCodes.SuperAdmin, null, 1).Decision
            .ShouldBe(new EmployeeRules.RoleDecision(RoleCodes.SuperAdmin, null));
    }

    [Fact]
    public void A_super_admin_becoming_an_admin_needs_a_domain_and_is_never_the_last_one()
    {
        EmployeeRules.CheckRoleChange(SuperAdmin, SuperAdminTarget(), RoleCodes.Admin, null, 2).Denial!
            .Kind.ShouldBe(DenialKind.Validation);
        EmployeeRules.CheckRoleChange(SuperAdmin, SuperAdminTarget(), RoleCodes.Admin, Execution, 1).Denial!
            .Code.ShouldBe(ErrorCodes.LastSuperAdmin);
        EmployeeRules.CheckRoleChange(SuperAdmin, SuperAdminTarget(), RoleCodes.Admin, Execution, 2).Decision
            .ShouldBe(new EmployeeRules.RoleDecision(RoleCodes.Admin, Execution));
    }

    [Fact]
    public void Role_changes_refuse_self_same_role_and_deactivated_accounts()
    {
        EmployeeRules.CheckRoleChange(DesignAdmin, new Target(DesignAdmin.UserId, RoleCodes.Admin, Design, UserStatus.Active),
            RoleCodes.Employee, null, 1).Denial!.Kind.ShouldBe(DenialKind.Forbidden);
        EmployeeRules.CheckRoleChange(DesignAdmin, Employee(), RoleCodes.Employee, null, 1).Denial!.Code
            .ShouldBe(ErrorCodes.RoleUnchanged);
        EmployeeRules.CheckRoleChange(DesignAdmin, Employee(status: UserStatus.Deactivated), RoleCodes.Admin, null, 1).Denial!.Code
            .ShouldBe(ErrorCodes.UserDeactivated);
    }

    [Fact]
    public void Only_callers_who_see_both_domains_move_people_between_them()
    {
        EmployeeRules.CheckMove(DesignAdmin, Employee(), Execution)!.Kind.ShouldBe(DenialKind.Forbidden);
        EmployeeRules.CheckMove(CrossDomainAdmin, Employee(), Execution).ShouldBeNull();
        EmployeeRules.CheckMove(CrossDomainAdmin, Employee(), Design)!.Code.ShouldBe(ErrorCodes.DomainUnchanged);
        EmployeeRules.CheckMove(SuperAdmin, SuperAdminTarget(), Design)!.Code.ShouldBe(ErrorCodes.SuperAdminHasNoDomain);
    }

    [Fact]
    public void Nobody_sets_their_own_pay_or_a_deactivated_persons_pay()
    {
        EmployeeRules.CheckPayChange(DesignAdmin, new Target(DesignAdmin.UserId, RoleCodes.Admin, Design, UserStatus.Active))!
            .Kind.ShouldBe(DenialKind.Forbidden);
        EmployeeRules.CheckPayChange(DesignAdmin, Employee(status: UserStatus.Deactivated))!.Code.ShouldBe(ErrorCodes.UserDeactivated);
        EmployeeRules.CheckPayChange(DesignAdmin, Employee(status: UserStatus.Invited)).ShouldBeNull();
    }

    [Fact]
    public void Sign_in_addresses_change_for_others_never_ones_own_this_way_and_a_super_admin_s_needs_the_permission_to_invite_super_admins()
    {
        EmployeeRules.CheckEmailChange(SuperAdmin, Employee()).ShouldBeNull();
        EmployeeRules.CheckEmailChange(SuperAdmin, Employee(status: UserStatus.Invited)).ShouldBeNull();
        EmployeeRules.CheckEmailChange(SuperAdmin, SuperAdminTarget()).ShouldBeNull();
        EmployeeRules.CheckEmailChange(SuperAdmin, Employee(status: UserStatus.Deactivated))!.Code.ShouldBe(ErrorCodes.UserDeactivated);
        EmployeeRules.CheckEmailChange(SuperAdmin, new Target(SuperAdmin.UserId, RoleCodes.SuperAdmin, null, UserStatus.Active))!
            .Kind.ShouldBe(DenialKind.Forbidden);

        // employee:change_email is checked by the endpoint; the rules are about whose address it is
        EmployeeRules.CheckEmailChange(DesignAdmin, Employee()).ShouldBeNull();
        EmployeeRules.CheckEmailChange(DesignAdmin, SuperAdminTarget())!.Code.ShouldBe(ErrorCodes.EmployeeChangeNotAllowed);
    }

    [Fact]
    public void Website_staff_stay_admins_and_are_not_moved()
    {
        var websiteAdmin = new Target(12, RoleCodes.Admin, DomainCodes.Website, UserStatus.Active);

        EmployeeRules.CheckRoleChange(SuperAdmin, websiteAdmin, RoleCodes.Employee, null, 2).Denial!.Code.ShouldBe(ErrorCodes.NotForThisDomain);
        EmployeeRules.CheckRoleChange(SuperAdmin, SuperAdminTarget(), RoleCodes.Employee, DomainCodes.Website, 2).Denial!.Code
            .ShouldBe(ErrorCodes.NotForThisDomain);
        EmployeeRules.CheckRoleChange(SuperAdmin, SuperAdminTarget(), RoleCodes.Admin, DomainCodes.Website, 2).Decision!.Domain
            .ShouldBe(DomainCodes.Website);
        EmployeeRules.CheckMove(SuperAdmin, websiteAdmin, Design)!.Code.ShouldBe(ErrorCodes.NotForThisDomain);
        EmployeeRules.CheckMove(SuperAdmin, Employee(), DomainCodes.Website)!.Code.ShouldBe(ErrorCodes.NotForThisDomain);
    }

    [Fact]
    public void Seeing_both_businesses_is_enough_to_see_super_admins_without_the_website()
        => EmployeeRules.CanSee(CrossDomainAdmin, null).ShouldBeTrue();
}
