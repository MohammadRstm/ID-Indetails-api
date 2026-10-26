using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProjectStaffConfiguration : IEntityTypeConfiguration<ProjectStaff>
{
    public void Configure(EntityTypeBuilder<ProjectStaff> builder)
    {
        builder.ToTable("project_staff");

        builder.HasKey(x => x.Id);

        // one row per person and site: taken off and put back is the same row
        builder.HasIndex(x => new { x.ProjectId, x.UserId }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.RemovedAt });

        builder.HasOne(x => x.Project)
            .WithMany(x => x.Staff)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AssignedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
