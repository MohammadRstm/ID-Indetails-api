using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalFileConfiguration : IEntityTypeConfiguration<PortalFile>
{
    public void Configure(EntityTypeBuilder<PortalFile> builder)
    {
        builder.ToTable("portal_files", t =>
        {
            t.HasCheckConstraint("ck_portal_files_original_size_positive", "`original_size_bytes` > 0");
            t.HasCheckConstraint("ck_portal_files_stored_size_non_negative", "`stored_size_bytes` >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StorageKey).IsRequired().HasMaxLength(255);
        builder.HasIndex(x => x.StorageKey).IsUnique();

        builder.Property(x => x.OriginalName).IsRequired().HasMaxLength(255);
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(127);

        // a project's storage totals, and the cleanup's files waiting to be deleted
        builder.HasIndex(x => new { x.ProjectId, x.DeletedAt });
        builder.HasIndex(x => x.DeletedAt);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
