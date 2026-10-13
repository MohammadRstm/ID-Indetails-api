using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalUploadConfiguration : IEntityTypeConfiguration<PortalUpload>
{
    public void Configure(EntityTypeBuilder<PortalUpload> builder)
    {
        builder.ToTable("portal_uploads", t =>
        {
            t.HasCheckConstraint("ck_portal_uploads_section", SnakeCaseEnum.CheckSql<PortalSection>("section"));
            t.HasCheckConstraint("ck_portal_uploads_status", SnakeCaseEnum.CheckSql<PortalUploadStatus>("status"));
            t.HasCheckConstraint("ck_portal_uploads_size_positive", "`size_bytes` > 0");
            // a hero image belongs to no section and replaces no item; every other upload has a section
            t.HasCheckConstraint(
                "ck_portal_uploads_hero_or_section",
                "(`is_hero` = 1 AND `section` IS NULL AND `replace_item_id` IS NULL) OR (`is_hero` = 0 AND `section` IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ClientToken).IsRequired().HasMaxLength(64);
        builder.Property(x => x.StorageKey).IsRequired().HasMaxLength(255);
        builder.Property(x => x.StoreUploadId).IsRequired().HasMaxLength(1024);
        builder.Property(x => x.FileName).IsRequired().HasMaxLength(255);

        builder.HasIndex(x => x.StorageKey).IsUnique();
        // the same file started twice by the same person is the same upload
        builder.HasIndex(x => new { x.CreatedById, x.ClientToken }).IsUnique();
        // the cleanup's search for abandoned uploads
        builder.HasIndex(x => new { x.Status, x.LastActivityAt });

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PortalItem>()
            .WithMany()
            .HasForeignKey(x => x.ReplaceItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
