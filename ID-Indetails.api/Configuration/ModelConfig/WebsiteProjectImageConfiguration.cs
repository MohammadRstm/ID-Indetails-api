using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class WebsiteProjectImageConfiguration : IEntityTypeConfiguration<WebsiteProjectImage>
{
    public void Configure(EntityTypeBuilder<WebsiteProjectImage> builder)
    {
        builder.ToTable("website_project_images");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Alt).IsRequired().HasMaxLength(WebsiteRules.MaxAltLength);

        // a gallery in order
        builder.HasIndex(x => new { x.WebsiteProjectId, x.Position });
        // a file shows in one place only
        builder.HasIndex(x => x.FileId).IsUnique();

        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}
