using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.ToTable("quotes", t =>
        {
            t.HasCheckConstraint("ck_quotes_amounts_positive", "`net_usd` > 0 AND `total_usd` >= `net_usd`");
            t.HasCheckConstraint("ck_quotes_tva_rate_range", "`tva_rate` >= 0 AND `tva_rate` <= 1");
        });

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => new { x.ProjectId, x.CreatedAt });

        builder.Property(x => x.Number).IsRequired().HasMaxLength(20);
        builder.Property(x => x.TvaRate).HasPrecision(5, 4);
        builder.Property(x => x.Scope).HasColumnType("text");

        builder.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
    }
}

public class QuoteDeliveryConfiguration : IEntityTypeConfiguration<QuoteDelivery>
{
    public void Configure(EntityTypeBuilder<QuoteDelivery> builder)
    {
        builder.ToTable("quote_deliveries", t =>
            t.HasCheckConstraint("ck_quote_deliveries_channel", SnakeCaseEnum.CheckSql<DeliveryChannel>("channel")));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Recipient).HasMaxLength(255);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.HasOne(x => x.Quote).WithMany(x => x.Deliveries).HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.SentById).OnDelete(DeleteBehavior.Restrict);
    }
}

public class QuoteSequenceConfiguration : IEntityTypeConfiguration<QuoteSequence>
{
    public void Configure(EntityTypeBuilder<QuoteSequence> builder)
    {
        builder.ToTable("quote_sequences", t => t.HasCheckConstraint("ck_quote_sequences_number_non_negative", "`last_number` >= 0"));
        builder.HasKey(x => x.Year);
        builder.Property(x => x.Year).ValueGeneratedNever();
    }
}
