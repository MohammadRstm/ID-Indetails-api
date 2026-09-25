using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProjectLabourDefaultConfiguration : IEntityTypeConfiguration<ProjectLabourDefault>
{
    public void Configure(EntityTypeBuilder<ProjectLabourDefault> builder)
    {
        builder.ToTable("project_labour_defaults", t =>
        {
            t.HasCheckConstraint("ck_project_labour_defaults_workers_non_negative", "`workers` >= 0");
            t.HasCheckConstraint("ck_project_labour_defaults_pay_non_negative", "`daily_pay` >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.ProjectId, x.EffectiveFrom }).IsUnique();

        builder.Property(x => x.Currency).IsCurrencyCode();

        builder.HasOne(x => x.Project)
            .WithMany(x => x.LabourDefaults)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
