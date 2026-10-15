using ID_Indetails.api.Business.Portal;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalSettingsConfiguration : IEntityTypeConfiguration<PortalSettings>
{
    public void Configure(EntityTypeBuilder<PortalSettings> builder)
    {
        builder.ToTable("portal_settings", t =>
            t.HasCheckConstraint("ck_portal_settings_revision_non_negative", "`revision` >= 0"));

        builder.HasKey(x => x.ProjectId);
        builder.Property(x => x.ProjectId).ValueGeneratedNever();

        builder.Property(x => x.Summary).HasMaxLength(PortalPublishRules.MaxSummaryLength);
        builder.Property(x => x.PublishedSummary).HasMaxLength(PortalPublishRules.MaxSummaryLength);

        builder.HasOne<Project>()
            .WithOne()
            .HasForeignKey<PortalSettings>(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PortalFile>()
            .WithMany()
            .HasForeignKey(x => x.HeroFileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PortalFile>()
            .WithMany()
            .HasForeignKey(x => x.PublishedHeroFileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.PublishedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
