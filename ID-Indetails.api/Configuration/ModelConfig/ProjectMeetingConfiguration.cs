using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProjectMeetingConfiguration : IEntityTypeConfiguration<ProjectMeeting>
{
    public void Configure(EntityTypeBuilder<ProjectMeeting> builder)
    {
        builder.ToTable("project_meetings", t =>
            t.HasCheckConstraint("ck_project_meetings_ends_after_start", "`ends_at` > `starts_at`"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Topic).HasMaxLength(200);
        builder.Property(x => x.Description).IsLongText();

        builder.HasIndex(x => new { x.ProjectId, x.StartsAt });

        builder.HasOne(x => x.Project)
            .WithMany(x => x.Meetings)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UpdatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
