using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class NotificationSentKeyConfiguration : IEntityTypeConfiguration<NotificationSentKey>
{
    public void Configure(EntityTypeBuilder<NotificationSentKey> builder)
    {
        builder.ToTable("notification_sent_keys");

        // once per person and key: the key also stops two sends at the same moment
        builder.HasKey(x => new { x.RecipientId, x.Key });
        builder.Property(x => x.Key).HasMaxLength(64);

        builder.HasOne(x => x.Recipient)
            .WithMany()
            .HasForeignKey(x => x.RecipientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
