using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ReopenedMonthConfiguration : IEntityTypeConfiguration<ReopenedMonth>
{
    public void Configure(EntityTypeBuilder<ReopenedMonth> builder)
    {
        builder.ToTable("reopened_months");

        builder.HasKey(x => x.Month);

        builder.HasOne(x => x.ReopenedBy)
            .WithMany()
            .HasForeignKey(x => x.ReopenedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
