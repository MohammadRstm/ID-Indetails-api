using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using Shouldly;
using Grant = ID_Indetails.api.Business.Auth.EffectivePermissions.Grant;

namespace ID_Indetails.UnitTests.Business;

public class EffectivePermissionsTests
{
    private static readonly Grant[] Grants =
    [
        new("project:view", null),
        new("work_entry:create", DomainCodes.Design),
        new("material:create", DomainCodes.Execution),
    ];

    [Fact]
    public void Design_user_does_not_get_execution_only_permissions()
        => EffectivePermissions.For(RoleCodes.Employee, DomainCodes.Design, Grants)
            .ShouldBe(["project:view", "work_entry:create"]);

    [Fact]
    public void Execution_user_does_not_get_design_only_permissions()
        => EffectivePermissions.For(RoleCodes.Employee, DomainCodes.Execution, Grants)
            .ShouldBe(["material:create", "project:view"]);

    [Fact]
    public void Super_admin_keeps_permissions_of_both_domains()
        => EffectivePermissions.For(RoleCodes.SuperAdmin, null, Grants)
            .ShouldBe(["material:create", "project:view", "work_entry:create"]);

    [Fact]
    public void Real_catalog_employee_in_design_logs_work_but_never_reports_materials()
    {
        var catalog = PermissionCatalog.Definitions
            .Where(d => PermissionCatalog.Grants[RoleCodes.Employee].Contains(d.Code))
            .Select(d => new Grant(d.Code, d.DomainCode));

        var design = EffectivePermissions.For(RoleCodes.Employee, DomainCodes.Design, catalog);

        design.ShouldContain(PermissionCodes.WorkEntryCreate);
        design.ShouldNotContain(PermissionCodes.MaterialCreate);
        design.ShouldNotContain(PermissionCodes.LabourReportCreate);
    }
}
