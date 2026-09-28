using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using Shouldly;
using Change = ID_Indetails.api.Business.Auth.PermissionChangePlanner.Change;
using Grant = ID_Indetails.api.Business.Auth.EffectivePermissions.Grant;
using Override = ID_Indetails.api.Business.Auth.EffectivePermissions.Override;

namespace ID_Indetails.UnitTests.Business;

/// <summary>Runs against the real catalogue and role defaults.</summary>
public class PermissionChangePlannerTests
{
    private const string Design = DomainCodes.Design;

    private static readonly IReadOnlyList<Grant> Catalog =
        PermissionCatalog.Definitions.Select(d => new Grant(d.Code, d.DomainCode)).ToList();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<Grant>> GrantsByRole =
        PermissionCatalog.Grants.ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<Grant>)Catalog.Where(c => g.Value.Contains(c.Code)).ToList());

    private static IReadOnlySet<string> Defaults(string role)
        => EffectivePermissions.RoleDefaults(role, Design, GrantsByRole[role]);

    private static PermissionChangePlanner.Plan Plan(string role, IEnumerable<Override> current, params Change[] changes)
        => PermissionChangePlanner.Build(role, Design, Catalog, GrantsByRole, current, changes);

    private static Change Grant(string code) => new(code, PermissionChangeAction.Grant);

    private static Change Revoke(string code) => new(code, PermissionChangeAction.Revoke);

    private static Change Reset(string code) => new(code, PermissionChangeAction.Reset);

    [Fact]
    public void Granting_an_extra_permission_stores_a_grant_override()
    {
        var plan = Plan(RoleCodes.Employee, [], Grant(PermissionCodes.ClientView));

        plan.Role.ShouldBe(RoleCodes.Employee);
        plan.PreviousRole.ShouldBeNull();
        plan.Overrides.ShouldBe(new Dictionary<string, PermissionEffect> { [PermissionCodes.ClientView] = PermissionEffect.Grant });
        plan.Applied.ShouldBe([Grant(PermissionCodes.ClientView)]);
    }

    [Fact]
    public void Revoking_a_default_permission_stores_a_revoke_override()
    {
        var plan = Plan(RoleCodes.Admin, [], Revoke(PermissionCodes.InvoiceVoid));

        plan.Role.ShouldBe(RoleCodes.Admin);
        plan.Overrides.ShouldBe(new Dictionary<string, PermissionEffect> { [PermissionCodes.InvoiceVoid] = PermissionEffect.Revoke });
    }

    [Fact]
    public void Granting_what_the_role_already_gives_changes_nothing()
    {
        var plan = Plan(RoleCodes.Employee, [], Grant(PermissionCodes.ProjectView));

        plan.Overrides.ShouldBeEmpty();
        plan.Applied.ShouldBeEmpty();
    }

    [Fact]
    public void Granting_back_a_revoked_default_removes_the_override()
    {
        var plan = Plan(
            RoleCodes.Admin,
            [new Override(PermissionCodes.InvoiceVoid, PermissionEffect.Revoke)],
            Grant(PermissionCodes.InvoiceVoid));

        plan.Overrides.ShouldBeEmpty();
        plan.Applied.Count.ShouldBe(1);
    }

    [Fact]
    public void Reset_returns_a_permission_to_the_role_default()
    {
        var current = new[]
        {
            new Override(PermissionCodes.InvoiceVoid, PermissionEffect.Revoke),
            new Override(PermissionCodes.SalaryView, PermissionEffect.Grant),
        };

        var plan = Plan(RoleCodes.Admin, current, Reset(PermissionCodes.InvoiceVoid), Reset(PermissionCodes.SalaryView));

        plan.Overrides.ShouldBeEmpty();
        plan.Applied.Count.ShouldBe(2);
    }

    [Fact]
    public void Employee_granted_exactly_the_admin_defaults_becomes_an_admin_with_no_overrides()
    {
        var missing = Defaults(RoleCodes.Admin).Except(Defaults(RoleCodes.Employee)).Select(Grant).ToArray();

        var plan = Plan(RoleCodes.Employee, [], missing);

        plan.Role.ShouldBe(RoleCodes.Admin);
        plan.PreviousRole.ShouldBe(RoleCodes.Employee);
        plan.Overrides.ShouldBeEmpty();
    }

    [Fact]
    public void Employee_one_permission_short_of_admin_stays_an_employee()
    {
        var missing = Defaults(RoleCodes.Admin).Except(Defaults(RoleCodes.Employee)).Order().Skip(1).Select(Grant).ToArray();

        var plan = Plan(RoleCodes.Employee, [], missing);

        plan.Role.ShouldBe(RoleCodes.Employee);
        plan.PreviousRole.ShouldBeNull();
        plan.Overrides.Count.ShouldBe(missing.Length);
    }

    [Fact]
    public void Employee_with_every_admin_default_plus_an_extra_is_not_an_exact_match_and_stays_an_employee()
    {
        var changes = Defaults(RoleCodes.Admin).Except(Defaults(RoleCodes.Employee))
            .Select(Grant)
            .Append(Grant(PermissionCodes.SalaryView))
            .ToArray();

        var plan = Plan(RoleCodes.Employee, [], changes);

        plan.Role.ShouldBe(RoleCodes.Employee);
    }

    [Fact]
    public void Admin_revoked_down_to_exactly_the_employee_defaults_becomes_an_employee()
    {
        var extra = Defaults(RoleCodes.Admin).Except(Defaults(RoleCodes.Employee)).Select(Revoke).ToArray();

        var plan = Plan(RoleCodes.Admin, [], extra);

        plan.Role.ShouldBe(RoleCodes.Employee);
        plan.PreviousRole.ShouldBe(RoleCodes.Admin);
        plan.Overrides.ShouldBeEmpty();
    }

    [Fact]
    public void Admin_revoked_below_the_employee_defaults_stays_an_admin()
    {
        var everything = Defaults(RoleCodes.Admin).Select(Revoke).ToArray();

        var plan = Plan(RoleCodes.Admin, [], everything);

        plan.Role.ShouldBe(RoleCodes.Admin);
        plan.Overrides.Count.ShouldBe(everything.Length);
    }

    [Fact]
    public void Promotion_counts_existing_overrides_too()
    {
        var missing = Defaults(RoleCodes.Admin).Except(Defaults(RoleCodes.Employee)).Order().ToList();
        var current = missing.Skip(1).Select(c => new Override(c, PermissionEffect.Grant));

        var plan = Plan(RoleCodes.Employee, current, Grant(missing[0]));

        plan.Role.ShouldBe(RoleCodes.Admin);
        plan.Overrides.ShouldBeEmpty();
    }

    [Fact]
    public void Super_admins_are_never_planned_for()
        => Should.Throw<InvalidOperationException>(() => Plan(RoleCodes.SuperAdmin, [], Revoke(PermissionCodes.ProjectView)));
}
