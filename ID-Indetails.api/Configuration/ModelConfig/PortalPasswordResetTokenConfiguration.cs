using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalPasswordResetTokenConfiguration : IEntityTypeConfiguration<PortalPasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PortalPasswordResetToken> builder)
    {
        builder.ToTable("portal_password_reset_tokens");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TokenHash).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => x.TokenHash).IsUnique();

        builder.HasOne(x => x.Account)
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
