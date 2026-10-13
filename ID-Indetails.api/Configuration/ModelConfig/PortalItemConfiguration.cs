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
            t.HasCheckConstraint("ck_portal_items_section", SnakeCaseEnum.CheckSql<PortalSection>("section")));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);

        // one item per file: a file is never shown twice
        builder.HasIndex(x => x.FileId).IsUnique();
        builder.HasIndex(x => new { x.ProjectId, x.Section, x.SortOrder });

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.File)
            .WithMany()
            .HasForeignKey(x => x.FileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UpdatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
