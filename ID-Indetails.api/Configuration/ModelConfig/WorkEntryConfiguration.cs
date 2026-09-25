using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class WorkEntryConfiguration : IEntityTypeConfiguration<WorkEntry>
{
    public void Configure(EntityTypeBuilder<WorkEntry> builder)
    {
        builder.ToTable("work_entries", t =>
            t.HasCheckConstraint("ck_work_entries_ends_after_start", "`ended_at` > `started_at`"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Description).IsRequired().IsLongText();

        // Computed and stored by MySQL, so it can never disagree with the start and end.
        builder.Property(x => x.DurationMinutes)
            .HasComputedColumnSql("TIMESTAMPDIFF(MINUTE, `started_at`, `ended_at`)", stored: true);

        // The two main read paths: a project's work over time, and a person's work over time.
        builder.HasIndex(x => new { x.ProjectId, x.StartedAt });
        builder.HasIndex(x => new { x.UserId, x.StartedAt });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AttachmentFile)
            .WithMany()
            .HasForeignKey(x => x.AttachmentFileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
