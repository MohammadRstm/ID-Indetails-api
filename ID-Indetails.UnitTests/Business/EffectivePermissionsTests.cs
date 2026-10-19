using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;
using Grant = ID_Indetails.api.Business.Auth.EffectivePermissions.Grant;
using Override = ID_Indetails.api.Business.Auth.EffectivePermissions.Override;

namespace ID_Indetails.UnitTests.Business;

public class EffectivePermissionsTests
{
    private static readonly Grant[] Catalog =
    [
        new("project:view", null),
        new("invoice:void", null),
        new("work_entry:create", DomainCodes.Design),
        new("material:create", DomainCodes.Execution),
    ];

    private static readonly Grant[] EmployeeGrants =
    [
        new("project:view", null),
        new("work_entry:create", DomainCodes.Design),
        new("material:create", DomainCodes.Execution),
    ];

    private static UserAccess Resolve(string role, string? domain, params Override[] overrides)
        => EffectivePermissions.Resolve(1, role, domain, Catalog, role == RoleCodes.SuperAdmin ? Catalog : EmployeeGrants, overrides);

    [Fact]
    public void Design_user_does_not_get_execution_only_defaults()
        => Resolve(RoleCodes.Employee, DomainCodes.Design).Permissions.ShouldBe(["project:view", "work_entry:create"]);

    [Fact]
    public void Execution_user_does_not_get_design_only_defaults()
        => Resolve(RoleCodes.Employee, DomainCodes.Execution).Permissions.ShouldBe(["material:create", "project:view"]);

    [Fact]
    public void Plain_role_defaults_are_not_a_joker_and_see_only_their_domain()
    {
        var access = Resolve(RoleCodes.Employee, DomainCodes.Design);

        access.IsJoker.ShouldBeFalse();
        access.VisibleDomains.ShouldBe([DomainCodes.Design]);
    }

    [Fact]
    public void A_grant_adds_a_permission_and_makes_a_joker()
    {
        var access = Resolve(RoleCodes.Employee, DomainCodes.Design, new Override("invoice:void", PermissionEffect.Grant));

        access.Permissions.ShouldContain("invoice:void");
        access.IsJoker.ShouldBeTrue();
    }

    [Fact]
    public void A_revoke_removes_a_permission_and_makes_a_joker()
    {
        var access = Resolve(RoleCodes.Employee, DomainCodes.Design, new Override("project:view", PermissionEffect.Revoke));

        access.Permissions.ShouldBe(["work_entry:create"]);
        access.IsJoker.ShouldBeTrue();
    }

    [Fact]
    public void Redundant_overrides_do_not_make_a_joker()
    {
        var access = Resolve(
            RoleCodes.Employee,
            DomainCodes.Design,
            new Override("project:view", PermissionEffect.Grant),
            new Override("invoice:void", PermissionEffect.Revoke));

        access.IsJoker.ShouldBeFalse();
    }

    [Fact]
    public void Holding_a_permission_of_the_other_domain_opens_that_domain()
    {
        var access = Resolve(RoleCodes.Employee, DomainCodes.Design, new Override("material:create", PermissionEffect.Grant));

        access.Permissions.ShouldContain("material:create");
        access.VisibleDomains.ShouldBe([DomainCodes.Design, DomainCodes.Execution]);
        access.CanSee(DomainCodes.Execution).ShouldBeTrue();
    }

    [Fact]
    public void Overrides_for_unknown_permissions_are_ignored()
        => Resolve(RoleCodes.Employee, DomainCodes.Design, new Override("nuke:everything", PermissionEffect.Grant))
            .Permissions.ShouldNotContain("nuke:everything");

    [Fact]
    public void Super_admin_holds_everything_but_logging_hours_and_overrides_never_apply()
    {
        var access = Resolve(RoleCodes.SuperAdmin, null, new Override("invoice:void", PermissionEffect.Revoke));

        access.Permissions.ShouldBe(["invoice:void", "material:create", "project:view"]);
        access.IsJoker.ShouldBeFalse();
        access.VisibleDomains.ShouldBe(DomainCodes.All);
    }

    [Fact]
    public void An_admin_never_logs_hours_whatever_their_grants_say()
    {
        // The admin is given the employee grants here, as if work_entry:create were still among the admin defaults.
        var plain = Resolve(RoleCodes.Admin, DomainCodes.Design);
        var granted = Resolve(RoleCodes.Admin, DomainCodes.Design, new Override("work_entry:create", PermissionEffect.Grant));

        plain.Permissions.ShouldBe(["project:view"]);
        plain.IsJoker.ShouldBeFalse();
        granted.Permissions.ShouldNotContain("work_entry:create");
    }

    [Fact]
    public void Real_catalog_employee_in_design_logs_work_but_never_reports_materials()
    {
        var catalog = PermissionCatalog.Definitions.Select(d => new Grant(d.Code, d.DomainCode)).ToList();
        var employee = catalog.Where(g => PermissionCatalog.Grants[RoleCodes.Employee].Contains(g.Code));

        var design = EffectivePermissions.Resolve(1, RoleCodes.Employee, DomainCodes.Design, catalog, employee, []);

        design.Permissions.ShouldContain(PermissionCodes.WorkEntryCreate);
        design.Permissions.ShouldNotContain(PermissionCodes.MaterialCreate);
        design.Permissions.ShouldNotContain(PermissionCodes.LabourReportCreate);
    }

    [Fact]
    public void Website_staff_hold_only_the_website_s_permissions_and_see_only_the_website()
    {
        Grant[] catalog = [new("project:view", null), new("proton:use", null), new("work_entry:create", DomainCodes.Design), new("website:manage", DomainCodes.Website)];

        var access = EffectivePermissions.Resolve(1, RoleCodes.Admin, DomainCodes.Website, catalog, catalog, []);

        access.Permissions.ShouldBe(["website:manage"]);
        access.VisibleDomains.ShouldBe([DomainCodes.Website]);
        access.VisibleBusinesses.ShouldBeEmpty();
    }

    [Fact]
    public void A_website_permission_opens_the_website_to_a_design_admin_without_making_it_a_business_of_theirs()
    {
        Grant[] catalog = [new("project:view", null), new("website:manage", DomainCodes.Website)];

        var access = EffectivePermissions.Resolve(
            1, RoleCodes.Admin, DomainCodes.Design, catalog, [catalog[0]], [new Override("website:manage", PermissionEffect.Grant)]);

        access.Permissions.ShouldBe(["project:view", "website:manage"]);
        access.VisibleDomains.ShouldBe([DomainCodes.Design, DomainCodes.Website]);
        access.VisibleBusinesses.ShouldBe([DomainCodes.Design]);
        access.SeesEveryBusiness.ShouldBeFalse();
    }
}
