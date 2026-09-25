using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class UserInvitationConfiguration : IEntityTypeConfiguration<UserInvitation>
{
    public void Configure(EntityTypeBuilder<UserInvitation> builder)
    {
        builder.ToTable("user_invitations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TokenHash).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => x.TokenHash).IsUnique();

        builder.HasOne(x => x.User)
            .WithMany(x => x.Invitations)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.InvitedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
