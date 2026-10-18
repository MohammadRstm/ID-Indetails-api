using ID_Indetails.api.Business.Website;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class WebsiteOfferingConfiguration : IEntityTypeConfiguration<WebsiteOffering>
{
    public void Configure(EntityTypeBuilder<WebsiteOffering> builder)
    {
        builder.ToTable("website_services");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).IsRequired().HasMaxLength(WebsiteRules.MaxServiceTitleLength);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(WebsiteRules.MaxServiceDescriptionLength);
    }
}

public class WebsiteLocationConfiguration : IEntityTypeConfiguration<WebsiteLocation>
{
    public void Configure(EntityTypeBuilder<WebsiteLocation> builder)
    {
        builder.ToTable("website_locations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.City).IsRequired().HasMaxLength(WebsiteRules.MaxPlaceLength);
        builder.Property(x => x.Country).IsRequired().HasMaxLength(WebsiteRules.MaxPlaceLength);
        builder.Property(x => x.Latitude).HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasPrecision(9, 6);
        builder.Property(x => x.Address).IsRequired().HasMaxLength(WebsiteRules.MaxAddressLength);
        builder.Property(x => x.Phone).IsRequired().HasMaxLength(WebsiteRules.MaxPhoneLength);
    }
}

public class WebsiteSocialLinkConfiguration : IEntityTypeConfiguration<WebsiteSocialLink>
{
    public void Configure(EntityTypeBuilder<WebsiteSocialLink> builder)
    {
        builder.ToTable("website_social_links");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Label).IsRequired().HasMaxLength(WebsiteRules.MaxLabelLength);
        builder.Property(x => x.Url).IsRequired().HasMaxLength(WebsiteRules.MaxUrlLength);
    }
}
