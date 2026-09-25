using ID_Indetails.api.Constants;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", t =>
        {
            t.HasCheckConstraint("ck_users_status", SnakeCaseEnum.CheckSql<UserStatus>("status"));

            // Super admins stand outside both domains; everyone else belongs to exactly one.
            t.HasCheckConstraint(
                "ck_users_super_admin_has_no_domain",
                $"(`role_code` = '{RoleCodes.SuperAdmin}') = (`domain_code` IS NULL)");

            // Invited users have no password or name until they accept the invitation.
            t.HasCheckConstraint(
                "ck_users_active_has_credentials",
                "`status` <> 'active' OR (`password_hash` IS NOT NULL AND `first_name` IS NOT NULL AND `last_name` IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Email).IsRequired().HasMaxLength(254);
        builder.HasIndex(x => x.Email).IsUnique();

        builder.Property(x => x.PasswordHash).HasMaxLength(255);
        builder.Property(x => x.FirstName).HasMaxLength(100);
        builder.Property(x => x.LastName).HasMaxLength(100);
        builder.Property(x => x.Phone).HasMaxLength(32);
        builder.Property(x => x.RoleCode).IsRequired().HasMaxLength(32);
        builder.Property(x => x.DomainCode).HasMaxLength(16);
        builder.Property(x => x.Position).HasMaxLength(120);

        builder.HasOne(x => x.Role)
            .WithMany()
            .HasForeignKey(x => x.RoleCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Domain)
            .WithMany()
            .HasForeignKey(x => x.DomainCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Team)
            .WithMany()
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PhotoFile)
            .WithMany()
            .HasForeignKey(x => x.PhotoFileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
