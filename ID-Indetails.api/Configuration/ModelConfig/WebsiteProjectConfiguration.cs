using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class WebsiteProjectConfiguration : IEntityTypeConfiguration<WebsiteProject>
{
    public void Configure(EntityTypeBuilder<WebsiteProject> builder)
    {
        builder.ToTable("website_projects", t =>
        {
            t.HasCheckConstraint("ck_website_projects_type", SnakeCaseEnum.CheckSql<WebsiteProjectType>("type"));
            t.HasCheckConstraint("ck_website_projects_status", SnakeCaseEnum.CheckSql<WebsiteProjectStatus>("status"));
            // an unlinked project carries its own status and year
            t.HasCheckConstraint("ck_website_projects_follows_link", "`project_id` IS NOT NULL OR (`status` IS NOT NULL AND `year` IS NOT NULL)");
            // only published projects are in the moving strip, at places 1 to 20
            t.HasCheckConstraint(
                "ck_website_projects_conveyor", $"`conveyor_order` IS NULL OR (`published` AND `conveyor_order` BETWEEN 1 AND {WebsiteRules.MaxConveyor})");
            t.HasCheckConstraint("ck_website_projects_position", "(`latitude` IS NULL) = (`longitude` IS NULL)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Slug).IsRequired().HasMaxLength(WebsiteRules.MaxSlugLength);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(WebsiteRules.MaxTitleLength);
        builder.Property(x => x.Client).HasMaxLength(WebsiteRules.MaxClientLength);
        builder.Property(x => x.City).HasMaxLength(WebsiteRules.MaxPlaceLength);
        builder.Property(x => x.Country).HasMaxLength(WebsiteRules.MaxPlaceLength);
        builder.Property(x => x.Latitude).HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasPrecision(9, 6);
        builder.Property(x => x.Description).HasColumnType("text");
        builder.Property(x => x.HeroAlt).HasMaxLength(WebsiteRules.MaxAltLength);
        builder.Property(x => x.HeroCreditPhotographer).HasMaxLength(WebsiteRules.MaxCreditLength);
        builder.Property(x => x.HeroCreditSource).HasMaxLength(WebsiteRules.MaxCreditLength);
        builder.Property(x => x.HeroCreditUrl).HasMaxLength(WebsiteRules.MaxUrlLength);

        builder.HasIndex(x => x.Slug).IsUnique();
        // one website project per system project
        builder.HasIndex(x => x.ProjectId).IsUnique();
        builder.HasIndex(x => x.ConveyorOrder).IsUnique();

        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.ThumbnailFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.HeroFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Images).WithOne().HasForeignKey(x => x.WebsiteProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}
