using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalMentionConfiguration : IEntityTypeConfiguration<PortalMention>
{
    public void Configure(EntityTypeBuilder<PortalMention> builder)
    {
        builder.ToTable("portal_mentions", t =>
        {
            t.HasCheckConstraint("ck_portal_mentions_one_place", "(`message_id` IS NULL) <> (`comment_id` IS NULL)");
            t.HasCheckConstraint("ck_portal_mentions_one_person", "(`user_id` IS NULL) <> (`account_id` IS NULL)");
        });

        builder.HasKey(x => x.Id);

        // someone is mentioned once per message or comment
        builder.HasIndex(x => new { x.MessageId, x.UserId }).IsUnique();
        builder.HasIndex(x => new { x.MessageId, x.AccountId }).IsUnique();
        builder.HasIndex(x => new { x.CommentId, x.UserId }).IsUnique();
        builder.HasIndex(x => new { x.CommentId, x.AccountId }).IsUnique();

        builder.HasOne<PortalMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PortalComment>().WithMany().HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PortalAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
