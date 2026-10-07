using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProtonBudgetTopUpConfiguration : IEntityTypeConfiguration<ProtonBudgetTopUp>
{
    public void Configure(EntityTypeBuilder<ProtonBudgetTopUp> builder)
    {
        builder.ToTable("proton_budget_top_ups", t =>
            t.HasCheckConstraint("ck_proton_budget_top_ups_amount_positive", "`amount_usd` > 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AmountUsd).HasPrecision(12, 2);
        builder.Property(x => x.Note).HasMaxLength(200);

        builder.HasIndex(x => x.Month);

        builder.HasOne(x => x.AddedBy)
            .WithMany()
            .HasForeignKey(x => x.AddedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
