using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalProjectAccessConfiguration : IEntityTypeConfiguration<PortalProjectAccess>
{
    public void Configure(EntityTypeBuilder<PortalProjectAccess> builder)
    {
        builder.ToTable("portal_project_access");

        builder.HasKey(x => x.Id);

        // One row per account and project: access ended and given again reuses it.
        builder.HasIndex(x => new { x.AccountId, x.ProjectId }).IsUnique();
        builder.HasIndex(x => x.ProjectId);

        builder.HasOne(x => x.Account)
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.GrantedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RevokedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
