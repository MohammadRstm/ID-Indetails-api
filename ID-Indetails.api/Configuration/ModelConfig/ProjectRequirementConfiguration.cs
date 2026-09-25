using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProjectRequirementConfiguration : IEntityTypeConfiguration<ProjectRequirement>
{
    public void Configure(EntityTypeBuilder<ProjectRequirement> builder)
    {
        builder.ToTable("project_requirements", t =>
            t.HasCheckConstraint(
                "ck_project_requirements_done_state",
                "(`is_done` = 1) = (`done_at` IS NOT NULL)"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Text).IsRequired().HasMaxLength(500);

        builder.HasOne(x => x.Project)
            .WithMany(x => x.Requirements)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.DoneById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
