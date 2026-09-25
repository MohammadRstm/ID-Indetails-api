using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", t =>
            t.HasCheckConstraint("ck_notifications_count_positive", "`count` >= 1"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Kind).IsRequired().HasMaxLength(40);
        builder.Property(x => x.SubjectType).HasMaxLength(40);
        builder.Property(x => x.Payload).IsJson();
        builder.Property(x => x.DigestKey).HasMaxLength(64);

        // A rolling digest is upserted: one row per recipient per digest key.
        builder.HasIndex(x => new { x.RecipientId, x.DigestKey }).IsUnique();
        builder.HasIndex(x => new { x.RecipientId, x.CreatedAt });

        builder.HasOne(x => x.Recipient)
            .WithMany()
            .HasForeignKey(x => x.RecipientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
