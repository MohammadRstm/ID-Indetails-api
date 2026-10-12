using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", t =>
        {
            t.HasCheckConstraint("ck_refresh_tokens_client", SnakeCaseEnum.CheckSql<RefreshTokenClient>("client"));
            t.HasCheckConstraint("ck_refresh_tokens_revoke_reason", SnakeCaseEnum.CheckSql<RevokeReason>("revoke_reason"));
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TokenHash).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => x.TokenHash).IsUnique();

        builder.Property(x => x.Client).HasMaxLength(10);
        builder.HasIndex(x => x.SessionId);

        builder.HasOne(x => x.User)
            .WithMany(x => x.RefreshTokens)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RefreshToken>()
            .WithMany()
            .HasForeignKey(x => x.ReplacedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
