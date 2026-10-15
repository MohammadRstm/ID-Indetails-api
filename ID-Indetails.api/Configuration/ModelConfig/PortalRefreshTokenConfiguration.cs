using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalRefreshTokenConfiguration : IEntityTypeConfiguration<PortalRefreshToken>
{
    public void Configure(EntityTypeBuilder<PortalRefreshToken> builder)
    {
        builder.ToTable("portal_refresh_tokens", t =>
            t.HasCheckConstraint("ck_portal_refresh_tokens_revoke_reason", SnakeCaseEnum.CheckSql<RevokeReason>("revoke_reason")));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TokenHash).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.SessionId);

        builder.HasOne<PortalAccount>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PortalRefreshToken>()
            .WithMany()
            .HasForeignKey(x => x.ReplacedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
