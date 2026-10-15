using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalCommentReadConfiguration : IEntityTypeConfiguration<PortalCommentRead>
{
    public void Configure(EntityTypeBuilder<PortalCommentRead> builder)
    {
        builder.ToTable("portal_comment_reads", t =>
            t.HasCheckConstraint("ck_portal_comment_reads_one_reader", "(`user_id` IS NULL) <> (`account_id` IS NULL)"));

        builder.HasKey(x => x.Id);

        // one marker per person and file
        builder.HasIndex(x => new { x.ItemId, x.UserId }).IsUnique();
        builder.HasIndex(x => new { x.ItemId, x.AccountId }).IsUnique();

        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PortalAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
