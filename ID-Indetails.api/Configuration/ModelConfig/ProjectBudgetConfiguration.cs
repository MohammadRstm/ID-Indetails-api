using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProjectBudgetConfiguration : IEntityTypeConfiguration<ProjectBudget>
{
    public void Configure(EntityTypeBuilder<ProjectBudget> builder)
    {
        builder.ToTable("project_budgets");

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ProjectId, x.Version }).IsUnique();

        builder.Property(x => x.Reason).IsRequired().HasMaxLength(BudgetRules.MaxReasonLength);

        // a budget goes with its project when an archived one made by mistake is deleted
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.SetById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(l => l.BudgetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProjectBudgetLineConfiguration : IEntityTypeConfiguration<ProjectBudgetLine>
{
    public void Configure(EntityTypeBuilder<ProjectBudgetLine> builder)
    {
        builder.ToTable("project_budget_lines", t =>
        {
            t.HasCheckConstraint("ck_project_budget_lines_amount_non_negative", "`amount_usd` >= 0");
            t.HasCheckConstraint("ck_project_budget_lines_one_key", "(`team_id` IS NULL) <> (`category` IS NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.BudgetId, x.TeamId }).IsUnique();
        builder.HasIndex(x => new { x.BudgetId, x.Category }).IsUnique();

        builder.Property(x => x.Category).HasMaxLength(BudgetRules.MaxCategoryLength);

        // a department in a budget stays: rename it instead
        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
