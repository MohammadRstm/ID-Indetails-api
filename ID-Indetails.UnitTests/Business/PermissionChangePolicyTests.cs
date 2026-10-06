using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PermissionChangePolicyTests
{
    private static readonly UserAccess SuperAdmin =
        new(1, RoleCodes.SuperAdmin, null, [PermissionCodes.PermissionManage, PermissionCodes.SalaryView], false, DomainCodes.All);

    private static readonly UserAccess Delegate =
        new(2, RoleCodes.Admin, DomainCodes.Design, [PermissionCodes.PermissionManage, PermissionCodes.ClientView], true, [DomainCodes.Design]);

    private static PermissionChangePolicy.Denial? Check(
        UserAccess actor,
        long targetId = 10,
        string targetRole = RoleCodes.Employee,
        UserStatus status = UserStatus.Active,
        params string[] codes)
        => PermissionChangePolicy.Check(actor, targetId, targetRole, status, codes);

    [Fact]
    public void Super_admin_may_change_anything_for_an_admin_or_employee()
    {
        Check(SuperAdmin, codes: [PermissionCodes.CompanyView]).ShouldBeNull();
        Check(SuperAdmin, targetRole: RoleCodes.Admin, codes: [PermissionCodes.OverheadManage]).ShouldBeNull();
    }

    [Fact]
    public void An_admin_hands_out_logging_hours_with_the_employee_role_without_logging_any_themselves()
    {
        PermissionChangePolicy.NotHeld(Delegate, [PermissionCodes.WorkEntryCreate, PermissionCodes.WorkEntryViewOwn, PermissionCodes.ClientView])
            .ShouldBeEmpty();
        PermissionChangePolicy.NotHeld(Delegate, [PermissionCodes.WorkEntryCreate, PermissionCodes.InvoiceVoid])
            .ShouldBe([PermissionCodes.InvoiceVoid]);
    }

    [Fact]
    public void Nobody_can_change_a_super_admin()
        => Check(SuperAdmin, targetRole: RoleCodes.SuperAdmin, codes: [PermissionCodes.ClientView])!.Code
            .ShouldBe(ErrorCodes.PermissionsLocked);

    [Fact]
    public void Nobody_can_change_their_own_permissions()
        => Check(Delegate, targetId: Delegate.UserId, targetRole: RoleCodes.Admin, codes: [PermissionCodes.ClientView])!.Code
            .ShouldBe(ErrorCodes.PermissionChangeNotAllowed);

    [Fact]
    public void Deactivated_accounts_cannot_be_changed()
    {
        var denial = Check(SuperAdmin, status: UserStatus.Deactivated, codes: [PermissionCodes.ClientView])!;

        denial.Kind.ShouldBe(PermissionChangePolicy.DenialKind.BusinessRule);
        denial.Code.ShouldBe(ErrorCodes.UserDeactivated);
    }

    [Fact]
    public void Invited_accounts_can_be_changed()
        => Check(SuperAdmin, status: UserStatus.Invited, codes: [PermissionCodes.ClientView]).ShouldBeNull();

    [Fact]
    public void Delegate_may_change_permissions_they_hold()
        => Check(Delegate, codes: [PermissionCodes.ClientView, PermissionCodes.PermissionManage]).ShouldBeNull();

    [Fact]
    public void Delegate_may_not_change_permissions_they_do_not_hold()
    {
        var denial = Check(Delegate, codes: [PermissionCodes.ClientView, PermissionCodes.SalaryView])!;

        denial.Kind.ShouldBe(PermissionChangePolicy.DenialKind.Forbidden);
        denial.Message.ShouldContain(PermissionCodes.SalaryView);
        denial.Message.ShouldNotContain(PermissionCodes.ClientView);
    }
}
