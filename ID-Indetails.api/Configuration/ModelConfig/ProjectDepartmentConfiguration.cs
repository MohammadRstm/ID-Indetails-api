using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProjectDepartmentConfiguration : IEntityTypeConfiguration<ProjectDepartment>
{
    public void Configure(EntityTypeBuilder<ProjectDepartment> builder)
    {
        builder.ToTable("project_departments", t =>
            t.HasCheckConstraint("ck_project_departments_share", "`share` >= 0 AND `share` <= 1"));

        builder.HasKey(x => new { x.ProjectId, x.TeamId });
        builder.HasIndex(x => x.TeamId);

        builder.Property(x => x.Share).HasPrecision(5, 4);

        builder.HasOne(x => x.Project)
            .WithMany(x => x.Departments)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        // a department on a project can't be deleted: rename it instead
        builder.HasOne(x => x.Team)
            .WithMany()
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AddedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProjectDepartmentPlanConfiguration : IEntityTypeConfiguration<ProjectDepartmentPlan>
{
    public void Configure(EntityTypeBuilder<ProjectDepartmentPlan> builder)
    {
        builder.ToTable("project_department_plans", t =>
            t.HasCheckConstraint("ck_project_department_plans_planned_minutes_positive", "`planned_minutes` > 0"));

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ProjectId, x.TeamId }).IsUnique();

        // the plans go with their project when an archived one made by mistake is deleted
        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // a department's plan is removed with it from the project, so the team is never deleted under one
        builder.HasOne<Team>()
            .WithMany()
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.SetById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProjectDepartmentChangeConfiguration : IEntityTypeConfiguration<ProjectDepartmentChange>
{
    public void Configure(EntityTypeBuilder<ProjectDepartmentChange> builder)
    {
        builder.ToTable("project_department_share_changes", t =>
            t.HasCheckConstraint("ck_project_department_share_changes_kind", SnakeCaseEnum.CheckSql<ProjectDepartmentChangeKind>("kind")));

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ProjectId, x.ChangedAt });

        builder.Property(x => x.Before).HasColumnType("text");
        builder.Property(x => x.After).IsRequired().HasColumnType("text");
        builder.Property(x => x.Reason).HasMaxLength(500);

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ChangedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
