using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ActivityEventConfiguration : IEntityTypeConfiguration<ActivityEvent>
{
    public void Configure(EntityTypeBuilder<ActivityEvent> builder)
    {
        builder.ToTable("activity_events");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DomainCode).HasMaxLength(16);
        builder.Property(x => x.Kind).IsRequired().HasMaxLength(40);
        builder.Property(x => x.SubjectType).HasMaxLength(40);
        builder.Property(x => x.Payload).IsJson();

        // Project timeline and history (status/progress at time T).
        builder.HasIndex(x => new { x.ProjectId, x.OccurredAt });
        // Domain activity feed.
        builder.HasIndex(x => new { x.DomainCode, x.OccurredAt });
        builder.HasIndex(x => x.Kind);

        builder.HasOne<Domain>()
            .WithMany()
            .HasForeignKey(x => x.DomainCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Client>()
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
