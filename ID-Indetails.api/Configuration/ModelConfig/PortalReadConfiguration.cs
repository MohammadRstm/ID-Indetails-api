using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PortalReadConfiguration : IEntityTypeConfiguration<PortalRead>
{
    public void Configure(EntityTypeBuilder<PortalRead> builder)
    {
        builder.ToTable("portal_reads", t =>
            t.HasCheckConstraint("ck_portal_reads_one_reader", "(`user_id` IS NULL) <> (`account_id` IS NULL)"));

        builder.HasKey(x => x.Id);

        // one marker per person and project
        builder.HasIndex(x => new { x.ProjectId, x.UserId }).IsUnique();
        builder.HasIndex(x => new { x.ProjectId, x.AccountId }).IsUnique();

        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PortalAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
