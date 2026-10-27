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
        PermissionCodes.OverheadCategoryManage, PermissionCodes.SessionManage, PermissionCodes.EmployeeChangeEmail,
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
    public void Super_admin_holds_every_permission_but_logging_hours()
    {
        GrantsOf(RoleCodes.SuperAdmin)
            .ShouldBe(PermissionCatalog.Definitions.Select(d => d.Code).Except(PermissionCatalog.OwnWork), ignoreOrder: true);
    }

    [Fact]
    public void Only_employees_log_hours()
    {
        GrantsOf(RoleCodes.Employee).ShouldContain(PermissionCodes.WorkEntryCreate);
        GrantsOf(RoleCodes.Employee).ShouldContain(PermissionCodes.WorkEntryViewOwn);
        // site staff report labour and write daily logs; materials are an admin's (or whoever is given them)
        GrantsOf(RoleCodes.Employee).ShouldContain(PermissionCodes.SiteLogCreate);
        GrantsOf(RoleCodes.Employee).ShouldNotContain(PermissionCodes.MaterialCreate);
        GrantsOf(RoleCodes.Admin).ShouldNotContain(c => PermissionCatalog.OwnWork.Contains(c));
        GrantsOf(RoleCodes.SuperAdmin).ShouldNotContain(c => PermissionCatalog.OwnWork.Contains(c));
    }

    [Fact]
    public void Admin_holds_nothing_at_company_level()
    {
        GrantsOf(RoleCodes.Admin).ShouldNotContain(c => CompanyLevel.Contains(c));
    }

    [Fact]
    public void Admin_sees_no_money_and_invites_no_one()
    {
        GrantsOf(RoleCodes.Admin).ShouldBe(
        [
            PermissionCodes.ProjectView, PermissionCodes.ProjectCreate, PermissionCodes.ProjectEdit, PermissionCodes.ProjectChangeStatus,
            PermissionCodes.RequirementView, PermissionCodes.RequirementManage, PermissionCodes.RequirementCompleteOwn,
            PermissionCodes.MeetingManage, PermissionCodes.MeetingCreate,
            PermissionCodes.WorkEntryViewAll, PermissionCodes.WorkEntryComment,
            PermissionCodes.MaterialCreate, PermissionCodes.MaterialManage, PermissionCodes.LabourReportCreate, PermissionCodes.LabourReportManage,
            PermissionCodes.SiteLogCreate, PermissionCodes.SiteLogViewAll,
            PermissionCodes.ClientView, PermissionCodes.ClientCreate, PermissionCodes.ClientManage, PermissionCodes.ClientAccessManage,
            PermissionCodes.ProjectFilesManage,
            PermissionCodes.EmployeeView, PermissionCodes.EmployeeManage, PermissionCodes.TeamManage,
            PermissionCodes.ActivityView, PermissionCodes.ReportView, PermissionCodes.DomainSettingsManage, PermissionCodes.ProtonUse,
            // limited to the website: only its own admins hold them by default
            PermissionCodes.WebsiteView, PermissionCodes.WebsiteManage, PermissionCodes.WebsiteAnalyticsView,
        ], ignoreOrder: true);
    }

    [Fact]
    public void The_website_s_permissions_belong_to_the_website()
        => PermissionCatalog.Definitions.Where(d => d.Code.StartsWith("website:") || d.Code.StartsWith("website_")).ShouldAllBe(d => d.DomainCode == DomainCodes.Website);

    [Fact]
    public void Admins_manage_project_files_but_only_super_admins_choose_and_publish_what_clients_see()
    {
        GrantsOf(RoleCodes.Admin).ShouldContain(PermissionCodes.ProjectFilesManage);
        GrantsOf(RoleCodes.Admin).ShouldNotContain(PermissionCodes.ClientPortalManage);
        GrantsOf(RoleCodes.SuperAdmin).ShouldContain(PermissionCodes.ClientPortalManage);
        GrantsOf(RoleCodes.Employee).ShouldNotContain(PermissionCodes.ProjectFilesManage);
        PermissionCatalog.Definitions.Single(d => d.Code == PermissionCodes.ProjectFilesManage).DomainCode.ShouldBe(DomainCodes.Design);
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

    [Fact]
    public void Employees_of_both_domains_set_meetings_but_only_managers_change_anyones()
    {
        GrantsOf(RoleCodes.Employee).ShouldContain(PermissionCodes.MeetingCreate);
        GrantsOf(RoleCodes.Employee).ShouldNotContain(PermissionCodes.MeetingManage);
        GrantsOf(RoleCodes.Admin).ShouldContain(PermissionCodes.MeetingCreate);
        PermissionCatalog.Definitions.Single(d => d.Code == PermissionCodes.MeetingCreate).DomainCode.ShouldBeNull();
        PermissionCatalog.Definitions.Single(d => d.Code == PermissionCodes.MeetingManage).DomainCode.ShouldBeNull();
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
