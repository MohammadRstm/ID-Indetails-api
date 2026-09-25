using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class OverheadAmountConfiguration : IEntityTypeConfiguration<OverheadAmount>
{
    public void Configure(EntityTypeBuilder<OverheadAmount> builder)
    {
        builder.ToTable("overhead_amounts", t =>
            t.HasCheckConstraint("ck_overhead_amounts_amount_non_negative", "`amount` >= 0"));

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.OverheadItemId, x.EffectiveFrom }).IsUnique();

        builder.HasOne(x => x.OverheadItem)
            .WithMany(x => x.Amounts)
            .HasForeignKey(x => x.OverheadItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
