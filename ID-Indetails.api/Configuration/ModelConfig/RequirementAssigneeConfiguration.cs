using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class RequirementAssigneeConfiguration : IEntityTypeConfiguration<RequirementAssignee>
{
    public void Configure(EntityTypeBuilder<RequirementAssignee> builder)
    {
        builder.ToTable("requirement_assignees");

        builder.HasKey(x => new { x.RequirementId, x.UserId });

        builder.HasOne(x => x.Requirement)
            .WithMany(x => x.Assignees)
            .HasForeignKey(x => x.RequirementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
