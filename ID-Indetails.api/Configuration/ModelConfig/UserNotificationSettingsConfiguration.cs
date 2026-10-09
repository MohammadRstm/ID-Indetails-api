using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class UserNotificationSettingsConfiguration : IEntityTypeConfiguration<UserNotificationSettings>
{
    public void Configure(EntityTypeBuilder<UserNotificationSettings> builder)
    {
        // Both quiet-hour ends or neither, each a minute of the day.
        builder.ToTable("user_notification_settings", t => t.HasCheckConstraint(
            "ck_user_notification_settings_quiet",
            "(`quiet_from_min` IS NULL AND `quiet_to_min` IS NULL) OR (`quiet_from_min` BETWEEN 0 AND 1439 AND `quiet_to_min` BETWEEN 0 AND 1439)"));

        builder.HasKey(x => x.UserId);
        builder.Property(x => x.UserId).ValueGeneratedNever();

        builder.HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<UserNotificationSettings>(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
