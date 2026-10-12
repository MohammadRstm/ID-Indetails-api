using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class UserNotificationMuteConfiguration : IEntityTypeConfiguration<UserNotificationMute>
{
    public void Configure(EntityTypeBuilder<UserNotificationMute> builder)
    {
        builder.ToTable("user_notification_mutes", t =>
            t.HasCheckConstraint("ck_user_notification_mutes_category", SnakeCaseEnum.CheckSql<NotificationCategory>("category")));

        builder.HasKey(x => new { x.UserId, x.Category });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
