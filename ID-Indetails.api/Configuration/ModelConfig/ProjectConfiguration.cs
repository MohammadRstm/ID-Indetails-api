using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects", t =>
        {
            t.HasCheckConstraint("ck_projects_status", SnakeCaseEnum.CheckSql<ProjectStatus>("status"));
            t.HasCheckConstraint("ck_projects_billing_type", SnakeCaseEnum.CheckSql<BillingType>("billing_type"));

            // Internal projects are cost-only: no client and no revenue.
            t.HasCheckConstraint(
                "ck_projects_internal_has_no_client_or_revenue",
                "`billing_type` = 'client' OR (`client_id` IS NULL AND `revenue` = 0)");

            t.HasCheckConstraint("ck_projects_revenue_non_negative", "`revenue` >= 0");
            t.HasCheckConstraint("ck_projects_progress_range", "`progress_pct` <= 100");
            t.HasCheckConstraint("ck_projects_tva_rate_range", "`tva_rate` >= 0 AND `tva_rate` <= 1");
            t.HasCheckConstraint("ck_projects_supervision_rate_range", "`supervision_rate` IS NULL OR `supervision_rate` BETWEEN 0 AND 1");

            // Execution sites are always for a client.
            t.HasCheckConstraint(
                "ck_projects_execution_is_client_work",
                $"`domain_code` <> '{DomainCodes.Execution}' OR `billing_type` = 'client'");

            // Execution projects have a work price (the project fee); Design projects have none.
            t.HasCheckConstraint(
                "ck_projects_execution_money_fields",
                $"(`domain_code` = '{DomainCodes.Execution}' AND `project_fee` IS NOT NULL)"
                + $" OR (`domain_code` <> '{DomainCodes.Execution}' AND `project_fee` IS NULL)");

            t.HasCheckConstraint("ck_projects_project_fee_non_negative", "`project_fee` IS NULL OR `project_fee` >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DomainCode).IsRequired().HasMaxLength(16);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).IsLongText();
        builder.Property(x => x.Location).HasMaxLength(200);
        builder.Property(x => x.Icon).HasMaxLength(40);
        builder.Property(x => x.Tint).HasMaxLength(9);
        builder.Property(x => x.RevenueCurrency).IsCurrencyCode();
        builder.Property(x => x.TvaRate).HasPrecision(5, 4).HasDefaultValue(0m);
        builder.Property(x => x.SupervisionRate).HasPrecision(5, 4);

        builder.HasIndex(x => new { x.DomainCode, x.Status });

        builder.HasOne(x => x.Domain)
            .WithMany()
            .HasForeignKey(x => x.DomainCode)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite key: the client must belong to the project's own domain. MySQL skips the
        // check when client_id is NULL, which is exactly the internal-project case.
        builder.HasOne(x => x.Client)
            .WithMany(x => x.Projects)
            .HasForeignKey(x => new { x.ClientId, x.DomainCode })
            .HasPrincipalKey(x => new { x.Id, x.DomainCode })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Owner)
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
