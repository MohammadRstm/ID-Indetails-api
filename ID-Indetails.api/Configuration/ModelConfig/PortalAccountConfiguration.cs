using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalAccountConfiguration : IEntityTypeConfiguration<PortalAccount>
{
    public void Configure(EntityTypeBuilder<PortalAccount> builder)
    {
        builder.ToTable("portal_accounts", t =>
        {
            t.HasCheckConstraint("ck_portal_accounts_status", SnakeCaseEnum.CheckSql<PortalAccountStatus>("status"));
            t.HasCheckConstraint(
                "ck_portal_accounts_active_has_credentials",
                "`status` <> 'active' OR (`password_hash` IS NOT NULL AND `first_name` IS NOT NULL AND `last_name` IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Email).IsRequired().HasMaxLength(254);
        builder.HasIndex(x => x.Email).IsUnique();

        builder.Property(x => x.FirstName).HasMaxLength(100);
        builder.Property(x => x.LastName).HasMaxLength(100);
        builder.Property(x => x.PasswordHash).HasMaxLength(255);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
