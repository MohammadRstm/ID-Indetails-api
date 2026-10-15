using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalCommentConfiguration : IEntityTypeConfiguration<PortalComment>
{
    public void Configure(EntityTypeBuilder<PortalComment> builder)
    {
        builder.ToTable("portal_comments", t =>
        {
            t.HasCheckConstraint("ck_portal_comments_one_author", "(`author_user_id` IS NULL) <> (`author_account_id` IS NULL)");
            // only a deleted comment has no text
            t.HasCheckConstraint("ck_portal_comments_body_unless_deleted", "`body` IS NOT NULL OR `deleted_at` IS NOT NULL");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Body).HasMaxLength(PortalDiscussionRules.MaxCommentLength);
        builder.Property(x => x.ClientToken).IsRequired().HasMaxLength(64);
        builder.Property(x => x.ItemTitle).IsRequired().HasMaxLength(200);
        builder.Property(x => x.FileName).IsRequired().HasMaxLength(255);

        // a file's thread, and the project's comments in order
        builder.HasIndex(x => new { x.ProjectId, x.ItemId, x.Id });
        builder.HasIndex(x => new { x.ProjectId, x.Id });
        builder.HasIndex(x => new { x.AuthorUserId, x.ClientToken }).IsUnique();
        builder.HasIndex(x => new { x.AuthorAccountId, x.ClientToken }).IsUnique();

        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PortalAccount>().WithMany().HasForeignKey(x => x.AuthorAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
