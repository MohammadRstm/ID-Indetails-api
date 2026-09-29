using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class EmploymentPeriodConfiguration : IEntityTypeConfiguration<EmploymentPeriod>
{
    public void Configure(EntityTypeBuilder<EmploymentPeriod> builder)
    {
        builder.ToTable("employment_periods", t =>
            t.HasCheckConstraint("ck_employment_periods_ends_after_start", "`ended_on` IS NULL OR `ended_on` >= `started_on`"));

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.UserId, x.StartedOn }).IsUnique();

        builder.HasOne(x => x.User)
            .WithMany(x => x.EmploymentPeriods)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
