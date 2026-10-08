using ID_Indetails.api.Constants;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;

namespace ID_Indetails.UnitTests.Data;

public class DatabaseModelTests
{
    private static IModel Model => ModelTestContext.Model;

    private static IEntityType Entity<T>() => Model.FindEntityType(typeof(T))!;

    private static IReadOnlyList<string> CheckNames<T>()
        => Entity<T>().GetCheckConstraints().Select(c => c.ModelName).ToList();

    [Fact]
    public void Model_has_all_59_tables_with_snake_case_names()
    {
        var tables = Model.GetEntityTypes().Select(e => e.GetTableName()!).ToList();

        tables.Count.ShouldBe(59);
        tables.ShouldAllBe(t => t == t.ToLowerInvariant() && !t.Contains(' '));
    }

    [Fact]
    public void Only_composition_children_cascade_on_delete()
    {
        // Everything else is RESTRICT so history can never be deleted by accident.
        var allowed = new[]
        {
            "client_contacts", "invoice_lines", "invoice_deliveries", "labour_week_days",
            "requirement_assignees", "overhead_amounts", "message_mentions", "message_attachments",
            "user_schedule_days", "hour_excuse_days", "client_statement_lines", "client_statement_deliveries",
        };

        var cascading = Model.GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys())
            .Where(fk => fk.DeleteBehavior == DeleteBehavior.Cascade)
            .Select(fk => fk.DeclaringEntityType.GetTableName())
            .ToList();

        cascading.ShouldBe(allowed, ignoreOrder: true);
    }

    [Fact]
    public void Every_enum_column_is_limited_by_a_check_constraint()
    {
        foreach (var entity in Model.GetEntityTypes())
        {
            var checks = entity.GetCheckConstraints().Select(c => c.Sql).ToList();
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!);

            foreach (var property in entity.GetProperties().Where(p => p.ClrType.IsEnum))
            {
                var column = property.GetColumnName(table)!;
                checks.ShouldContain(
                    sql => sql.StartsWith($"`{column}` IN ("),
                    $"{entity.GetTableName()}.{column} has no enum CHECK constraint");
            }
        }
    }

    [Fact]
    public void Company_settings_table_can_only_hold_one_row()
    {
        var check = Entity<CompanySettings>().GetCheckConstraints()
            .Single(c => c.ModelName == "ck_company_settings_single_row");

        check.Sql.ShouldBe("`id` = 1");
    }

    [Fact]
    public void No_table_carries_a_tenant_or_company_key()
    {
        var columns = Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties().Select(p => p.GetColumnName()))
            .ToList();

        columns.ShouldNotContain("tenant_id");
        columns.ShouldNotContain("company_id");
    }

    [Fact]
    public void Project_client_must_belong_to_the_same_domain()
    {
        var fk = Entity<Project>().GetForeignKeys()
            .Single(f => f.PrincipalEntityType.ClrType == typeof(Client));

        fk.Properties.Select(p => p.Name).ShouldBe([nameof(Project.ClientId), nameof(Project.DomainCode)]);
        fk.PrincipalKey.Properties.Select(p => p.Name).ShouldBe([nameof(Client.Id), nameof(Client.DomainCode)]);
    }

    [Fact]
    public void Structural_business_rules_are_enforced_by_check_constraints()
    {
        CheckNames<User>().ShouldContain("ck_users_super_admin_has_no_domain");
        CheckNames<User>().ShouldContain("ck_users_active_has_credentials");
        CheckNames<Project>().ShouldContain("ck_projects_internal_has_no_client_or_revenue");
        CheckNames<WorkEntry>().ShouldContain("ck_work_entries_ends_after_start");
        CheckNames<Invoice>().ShouldContain("ck_invoices_numbered_when_issued");
        CheckNames<Conversation>().ShouldContain("ck_conversations_shape");
        CheckNames<LabourWeek>().ShouldContain("ck_labour_weeks_starts_on_monday");
    }

    [Fact]
    public void Derived_amounts_are_generated_by_the_database()
    {
        Entity<WorkEntry>().FindProperty(nameof(WorkEntry.DurationMinutes))!.GetComputedColumnSql()
            .ShouldBe("TIMESTAMPDIFF(MINUTE, `started_at`, `ended_at`)");
        Entity<ProjectMaterial>().FindProperty(nameof(ProjectMaterial.Total))!.GetComputedColumnSql()
            .ShouldBe("CASE WHEN `quantity` > 0 AND `unit_cost` > 0 THEN ROUND(`quantity` * `unit_cost`, 2) ELSE `amount` END");
        Entity<LabourWeekDay>().FindProperty(nameof(LabourWeekDay.Cost))!.GetComputedColumnSql()
            .ShouldBe("`workers` * `daily_pay`");
    }

    [Fact]
    public void Tokens_are_stored_only_as_unique_hashes()
    {
        foreach (var type in new[] { typeof(RefreshToken), typeof(UserInvitation), typeof(PasswordResetToken) })
        {
            var entity = Model.FindEntityType(type)!;

            entity.GetProperties().Select(p => p.Name).ShouldNotContain("Token");
            entity.GetIndexes().ShouldContain(i =>
                i.IsUnique && i.Properties.Single().Name == "TokenHash");
        }
    }

    [Fact]
    public void Reference_data_is_seeded_from_the_constants()
    {
        Entity<Domain>().GetSeedData().Select(d => d[nameof(Domain.Code)])
            .ShouldBe(DomainCodes.All, ignoreOrder: true);

        Entity<Role>().GetSeedData().Select(r => r[nameof(Role.Code)])
            .ShouldBe(RoleCodes.All, ignoreOrder: true);

        Entity<Permission>().GetSeedData().Count()
            .ShouldBe(PermissionCatalog.Definitions.Count);

        Entity<RolePermission>().GetSeedData().Count()
            .ShouldBe(PermissionCatalog.Grants.Sum(g => g.Value.Count));
    }
}
