using ID_Indetails.api.Business.Messaging;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations", t =>
        {
            t.HasCheckConstraint("ck_conversations_type", SnakeCaseEnum.CheckSql<ConversationType>("type"));

            // A DM has exactly two different users stored low-then-high (so each pair has one
            // row); a group chat has a team and no users; Announcements has neither; a custom
            // group has a name, and only it has a name or a photo.
            t.HasCheckConstraint(
                "ck_conversations_shape",
                "(`type` = 'dm' AND `team_id` IS NULL AND `dm_user_low_id` IS NOT NULL AND `dm_user_high_id` IS NOT NULL AND `dm_user_low_id` < `dm_user_high_id` AND `name` IS NULL AND `photo_file_id` IS NULL)"
                + " OR (`type` = 'group' AND `team_id` IS NOT NULL AND `dm_user_low_id` IS NULL AND `dm_user_high_id` IS NULL AND `name` IS NULL AND `photo_file_id` IS NULL)"
                + " OR (`type` = 'announcements' AND `team_id` IS NULL AND `dm_user_low_id` IS NULL AND `dm_user_high_id` IS NULL AND `name` IS NULL AND `photo_file_id` IS NULL)"
                + " OR (`type` = 'custom_group' AND `team_id` IS NULL AND `dm_user_low_id` IS NULL AND `dm_user_high_id` IS NULL AND `name` IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DomainCode).HasMaxLength(16);
        builder.Property(x => x.Name).HasMaxLength(MessagingRules.MaxGroupNameLength);

        builder.HasIndex(x => new { x.DmUserLowId, x.DmUserHighId }).IsUnique();
        builder.HasIndex(x => x.TeamId).IsUnique();

        builder.HasOne(x => x.Team)
            .WithMany()
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain>()
            .WithMany()
            .HasForeignKey(x => x.DomainCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.DmUserLowId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.DmUserHighId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StoredFile>()
            .WithMany()
            .HasForeignKey(x => x.PhotoFileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
