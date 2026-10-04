using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Business.Notifications;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Billing;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PrivilegeRulesTests
{
    private static readonly string[] AdminDefaults = [PermissionCodes.ProjectView, PermissionCodes.FinancialView, PermissionCodes.EmployeeManage];

    private static UserAccess Actor(string role, params string[] codes) => new(7, role, DomainCodes.Design, codes, false, [DomainCodes.Design]);

    [Fact]
    public void Nobody_but_a_super_admin_hands_out_a_role_default_they_lack()
    {
        var manager = Actor(RoleCodes.Employee, PermissionCodes.ProjectView, PermissionCodes.EmployeeManage);
        EmployeeRules.CheckRoleHandOut(manager, AdminDefaults, hasRevokedPermissions: false)!.Code.ShouldBe(ErrorCodes.PermissionChangeNotAllowed);
        InvitationPolicy.CheckDefaultsHeld(manager, AdminDefaults)!.ShouldContain(PermissionCodes.FinancialView);

        var admin = Actor(RoleCodes.Admin, AdminDefaults);
        EmployeeRules.CheckRoleHandOut(admin, AdminDefaults, hasRevokedPermissions: false).ShouldBeNull();
        InvitationPolicy.CheckDefaultsHeld(admin, AdminDefaults).ShouldBeNull();

        var superAdmin = new UserAccess(1, RoleCodes.SuperAdmin, null, [], false, DomainCodes.All);
        InvitationPolicy.CheckDefaultsHeld(superAdmin, AdminDefaults).ShouldBeNull();
    }

    [Fact]
    public void Permissions_taken_away_by_hand_come_back_with_a_new_role_only_for_someone_who_manages_permissions()
    {
        EmployeeRules.CheckRoleHandOut(Actor(RoleCodes.Admin, AdminDefaults), AdminDefaults, hasRevokedPermissions: true)!
            .Kind.ShouldBe(DenialKind.Forbidden);
        EmployeeRules.CheckRoleHandOut(Actor(RoleCodes.Admin, [.. AdminDefaults, PermissionCodes.PermissionManage]), AdminDefaults, hasRevokedPermissions: true)
            .ShouldBeNull();
    }

    [Fact]
    public void A_notification_is_read_under_the_permission_it_was_sent_under()
    {
        var hidden = NotificationAccess.HiddenKinds(Actor(RoleCodes.Admin, PermissionCodes.FinancialView));
        hidden.ShouldContain(NotificationKinds.WorkLogged);
        hidden.ShouldContain(NotificationKinds.HoursShort);
        hidden.ShouldNotContain(NotificationKinds.InvoiceIssued);
        hidden.ShouldNotContain(NotificationKinds.RequirementAssigned);
    }

    [Fact]
    public void A_delivery_recorded_as_an_email_names_a_real_address()
    {
        var validator = new RecordDeliveryRequestValidator();
        validator.Validate(new RecordDeliveryRequest { Channel = DeliveryChannel.Email, Recipient = "Accounts team" }).IsValid.ShouldBeFalse();
        validator.Validate(new RecordDeliveryRequest { Channel = DeliveryChannel.Email, Recipient = "accounts@client.test" }).IsValid.ShouldBeTrue();
        validator.Validate(new RecordDeliveryRequest { Channel = DeliveryChannel.Whatsapp, Recipient = "Accounts team" }).IsValid.ShouldBeTrue();
    }
}
