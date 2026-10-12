using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
{
    public void Configure(EntityTypeBuilder<UserDevice> builder)
    {
        builder.ToTable("user_devices", t =>
        {
            t.HasCheckConstraint("ck_user_devices_provider", SnakeCaseEnum.CheckSql<PushProvider>("provider"));
            t.HasCheckConstraint("ck_user_devices_platform", SnakeCaseEnum.CheckSql<DevicePlatform>("platform"));
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider).HasMaxLength(10);
        builder.Property(x => x.Platform).HasMaxLength(10);
        builder.Property(x => x.PushToken).IsRequired().HasMaxLength(255);
        builder.Property(x => x.AppVersion).HasMaxLength(32);

        builder.HasIndex(x => x.PushToken).IsUnique();
        builder.HasIndex(x => x.SessionId);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
