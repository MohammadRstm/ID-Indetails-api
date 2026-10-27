using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class SiteLogConfiguration : IEntityTypeConfiguration<SiteLog>
{
    public void Configure(EntityTypeBuilder<SiteLog> builder)
    {
        builder.ToTable("site_logs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Body).IsRequired().HasMaxLength(SiteTeamRules.MaxLogLength);

        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
        builder.HasIndex(x => new { x.ProjectId, x.CreatedAt });

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
