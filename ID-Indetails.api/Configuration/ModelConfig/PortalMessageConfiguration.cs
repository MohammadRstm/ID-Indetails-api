using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalMessageConfiguration : IEntityTypeConfiguration<PortalMessage>
{
    public void Configure(EntityTypeBuilder<PortalMessage> builder)
    {
        builder.ToTable("portal_messages", t =>
        {
            // written by a staff member or by a client, never both and never neither
            t.HasCheckConstraint("ck_portal_messages_one_author", "(`author_user_id` IS NULL) <> (`author_account_id` IS NULL)");
            // only a deleted message has no text
            t.HasCheckConstraint("ck_portal_messages_body_unless_deleted", "`body` IS NOT NULL OR `deleted_at` IS NOT NULL");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Body).HasMaxLength(PortalDiscussionRules.MaxMessageLength);
        builder.Property(x => x.ClientToken).IsRequired().HasMaxLength(64);

        // the chat, page by page
        builder.HasIndex(x => new { x.ProjectId, x.Id });
        // the same message sent twice is the same message
        builder.HasIndex(x => new { x.AuthorUserId, x.ClientToken }).IsUnique();
        builder.HasIndex(x => new { x.AuthorAccountId, x.ClientToken }).IsUnique();

        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PortalAccount>().WithMany().HasForeignKey(x => x.AuthorAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
