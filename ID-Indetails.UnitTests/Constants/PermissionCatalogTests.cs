using ID_Indetails.api.Constants;
using Shouldly;

namespace ID_Indetails.UnitTests.Constants;

public class PermissionCatalogTests
{
    private static IReadOnlyList<string> GrantsOf(string role) => PermissionCatalog.Grants[role];

    private static readonly string[] CompanyLevel =
    [
        PermissionCodes.CompanyView, PermissionCodes.OverheadManage,
        PermissionCodes.SalaryView, PermissionCodes.CompanySettingsManage, PermissionCodes.ExchangeRateManage,
    ];

    [Fact]
    public void Permission_codes_are_unique_and_use_resource_action_form()
    {
        var codes = PermissionCatalog.Definitions.Select(d => d.Code).ToList();

        codes.Distinct().Count().ShouldBe(codes.Count);
        codes.ShouldAllBe(c => System.Text.RegularExpressions.Regex.IsMatch(c, "^[a-z_]+:[a-z_]+$"));
    }

    [Fact]
    public void Every_grant_refers_to_a_defined_permission()
    {
        var defined = PermissionCatalog.Definitions.Select(d => d.Code).ToHashSet();

        foreach (var (role, codes) in PermissionCatalog.Grants)
        {
            codes.ShouldAllBe(c => defined.Contains(c), $"{role} is granted an undefined permission");
        }
    }

    [Fact]
    public void Every_role_has_grants_and_only_known_roles_are_granted()
    {
        PermissionCatalog.Grants.Keys.ShouldBe(RoleCodes.All, ignoreOrder: true);
    }

    [Fact]
    public void Domain_scoped_permissions_point_at_real_domains()
    {
        PermissionCatalog.Definitions
            .Where(d => d.DomainCode is not null)
            .ShouldAllBe(d => DomainCodes.All.Contains(d.DomainCode!));
    }

    [Fact]
    public void Super_admin_holds_every_permission()
    {
        GrantsOf(RoleCodes.SuperAdmin)
            .ShouldBe(PermissionCatalog.Definitions.Select(d => d.Code), ignoreOrder: true);
    }

    [Fact]
    public void Admin_holds_nothing_at_company_level()
    {
        GrantsOf(RoleCodes.Admin).ShouldNotContain(c => CompanyLevel.Contains(c));
    }

    [Fact]
    public void Employee_never_sees_money_or_manages_anything()
    {
        var employee = GrantsOf(RoleCodes.Employee);

        employee.ShouldNotContain(PermissionCodes.FinancialView);
        employee.ShouldNotContain(PermissionCodes.WorkEntryViewAll);
        employee.ShouldNotContain(c => c.StartsWith("invoice:") || c.StartsWith("payment:"));
        employee.ShouldNotContain(c => c.EndsWith(":manage") || c.EndsWith(":invite"));
        employee.ShouldNotContain(c => CompanyLevel.Contains(c));
    }

    [Theory]
    // Strings already used by the React client (config/permissions.ts) must not change.
    [InlineData("project:view")]
    [InlineData("project:create")]
    [InlineData("project:edit")]
    [InlineData("client:view")]
    [InlineData("client:create")]
    [InlineData("financial:view")]
    [InlineData("invoice:create")]
    [InlineData("payment:record")]
    [InlineData("material:create")]
    [InlineData("labour_report:create")]
    [InlineData("employee:view")]
    [InlineData("employee:manage")]
    [InlineData("company:view")]
    public void Client_permission_strings_exist(string code)
    {
        PermissionCatalog.Definitions.Select(d => d.Code).ShouldContain(code);
    }

    [Fact]
    public void Role_strings_match_the_client()
    {
        RoleCodes.All.ShouldBe(["super_admin", "admin", "employee"]);
    }
}
