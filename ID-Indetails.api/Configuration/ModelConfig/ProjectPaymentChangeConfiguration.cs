using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProjectPaymentChangeConfiguration : IEntityTypeConfiguration<ProjectPaymentChange>
{
    public void Configure(EntityTypeBuilder<ProjectPaymentChange> builder)
    {
        builder.ToTable("project_payment_changes", t =>
        {
            t.HasCheckConstraint("ck_project_payment_changes_amount_non_negative", "`amount` >= 0 AND (`previous_amount` IS NULL OR `previous_amount` >= 0)");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.ProjectId, x.ChangedAt });

        builder.Property(x => x.MaterialMarkup).HasPrecision(7, 4);
        builder.Property(x => x.PreviousMaterialMarkup).HasPrecision(7, 4);
        builder.Property(x => x.TvaRate).HasPrecision(5, 4);
        builder.Property(x => x.PreviousTvaRate).HasPrecision(5, 4);
        builder.Property(x => x.SupervisionRate).HasPrecision(5, 4);
        builder.Property(x => x.PreviousSupervisionRate).HasPrecision(5, 4);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(ProjectRules.MaxPaymentDescriptionLength);

        builder.HasOne(x => x.Project)
            .WithMany(x => x.PaymentChanges)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ChangedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
