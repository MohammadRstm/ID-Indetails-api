using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalHeroImageConfiguration : IEntityTypeConfiguration<PortalHeroImage>
{
    public void Configure(EntityTypeBuilder<PortalHeroImage> builder)
    {
        builder.ToTable("portal_hero_images");

        builder.HasKey(x => x.Id);

        // an image is in each list once
        builder.HasIndex(x => new { x.ProjectId, x.IsPublished, x.FileId }).IsUnique();
        // each list in order
        builder.HasIndex(x => new { x.ProjectId, x.IsPublished, x.SortOrder });

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.File)
            .WithMany()
            .HasForeignKey(x => x.FileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
