using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalNotificationConfiguration : IEntityTypeConfiguration<PortalNotification>
{
    public void Configure(EntityTypeBuilder<PortalNotification> builder)
    {
        builder.ToTable("portal_notifications", t =>
        {
            t.HasCheckConstraint("ck_portal_notifications_kind", SnakeCaseEnum.CheckSql<PortalNotificationKind>("kind"));
            t.HasCheckConstraint("ck_portal_notifications_count_positive", "`count` > 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DigestKey).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Payload).HasColumnType("text");

        // one rolling notification per client and key; a mention's key is its own, so it is sent once
        builder.HasIndex(x => new { x.AccountId, x.DigestKey }).IsUnique();
        // the bell: newest first, unread counted
        builder.HasIndex(x => new { x.AccountId, x.ReadAt, x.Id });

        builder.HasOne<PortalAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
