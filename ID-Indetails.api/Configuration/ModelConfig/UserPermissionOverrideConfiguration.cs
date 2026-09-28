using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class UserPermissionOverrideConfiguration : IEntityTypeConfiguration<UserPermissionOverride>
{
    public void Configure(EntityTypeBuilder<UserPermissionOverride> builder)
    {
        builder.ToTable("user_permission_overrides", t =>
        {
            t.HasCheckConstraint("ck_user_permission_overrides_effect", SnakeCaseEnum.CheckSql<PermissionEffect>("effect"));
        });

        // One override per user and permission.
        builder.HasKey(x => new { x.UserId, x.PermissionCode });

        builder.Property(x => x.PermissionCode).HasMaxLength(64);
        builder.Property(x => x.Reason).HasMaxLength(500);

        builder.HasOne(x => x.User)
            .WithMany(x => x.PermissionOverrides)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Permission)
            .WithMany()
            .HasForeignKey(x => x.PermissionCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.GrantedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
