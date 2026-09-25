using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class UserBoardLayoutConfiguration : IEntityTypeConfiguration<UserBoardLayout>
{
    public void Configure(EntityTypeBuilder<UserBoardLayout> builder)
    {
        builder.ToTable("user_board_layouts");

        builder.HasKey(x => new { x.UserId, x.BoardKey });

        builder.Property(x => x.BoardKey).HasMaxLength(64);
        builder.Property(x => x.Layout).IsRequired().IsJson();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
