using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class LabourWeekConfiguration : IEntityTypeConfiguration<LabourWeek>
{
    public void Configure(EntityTypeBuilder<LabourWeek> builder)
    {
        // WEEKDAY() is 0 for Monday: every report covers a Monday-to-Sunday week.
        builder.ToTable("labour_weeks", t =>
            t.HasCheckConstraint("ck_labour_weeks_starts_on_monday", "WEEKDAY(`week_start`) = 0"));

        builder.HasKey(x => x.Id);

        // One report per project per week; resubmitting replaces it.
        builder.HasIndex(x => new { x.ProjectId, x.WeekStart }).IsUnique();

        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.SubmittedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
