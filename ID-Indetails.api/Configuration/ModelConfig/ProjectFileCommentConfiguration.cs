using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProjectFileCommentConfiguration : IEntityTypeConfiguration<ProjectFileComment>
{
    public void Configure(EntityTypeBuilder<ProjectFileComment> builder)
    {
        builder.ToTable("project_file_comments", t =>
            // fixed by someone, or open
            t.HasCheckConstraint("ck_project_file_comments_fixed", "(`fixed_at` IS NULL) = (`fixed_by_id` IS NULL)"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Body).IsRequired().HasMaxLength(ProjectFileRules.MaxCommentLength);

        // a file's comments in order
        builder.HasIndex(x => new { x.ItemId, x.Id });

        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        // a file deleted before it ever reached the client goes with its comments
        builder.HasOne<PortalItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.FixedById).OnDelete(DeleteBehavior.Restrict);
    }
}
