using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalItemConfiguration : IEntityTypeConfiguration<PortalItem>
{
    public void Configure(EntityTypeBuilder<PortalItem> builder)
    {
        builder.ToTable("portal_items", t =>
        {
            t.HasCheckConstraint("ck_portal_items_section", SnakeCaseEnum.CheckSql<PortalSection>("section"));
            t.HasCheckConstraint("ck_portal_items_published_section", SnakeCaseEnum.CheckSql<PortalSection>("published_section"));
            // the published copy is all there or not at all
            t.HasCheckConstraint(
                "ck_portal_items_published_whole",
                "(`published_file_id` IS NULL AND `published_title` IS NULL AND `published_section` IS NULL AND `published_sort_order` IS NULL)"
                + " OR (`published_file_id` IS NOT NULL AND `published_title` IS NOT NULL AND `published_section` IS NOT NULL AND `published_sort_order` IS NOT NULL)");
            // only what clients see can be waiting to be removed from it
            t.HasCheckConstraint("ck_portal_items_removed_is_published", "`removed_at` IS NULL OR `published_file_id` IS NOT NULL");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.PublishedTitle).HasMaxLength(200);

        // one item per file: a file is never shown twice
        builder.HasIndex(x => x.FileId).IsUnique();
        builder.HasIndex(x => new { x.ProjectId, x.Section, x.SortOrder });
        // the client's page
        builder.HasIndex(x => new { x.ProjectId, x.PublishedSection, x.PublishedSortOrder });

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.File)
            .WithMany()
            .HasForeignKey(x => x.FileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PortalFile>()
            .WithMany()
            .HasForeignKey(x => x.PublishedFileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UpdatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
