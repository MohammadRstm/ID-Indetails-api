using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("exchange_rates", t =>
            t.HasCheckConstraint("ck_exchange_rates_rate_positive", "`rate_per_usd` > 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Currency).IsCurrencyCode();
        builder.Property(x => x.RatePerUsd).HasPrecision(18, 6);

        builder.HasIndex(x => new { x.Currency, x.EffectiveFrom }).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
