using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class InvoiceSequenceConfiguration : IEntityTypeConfiguration<InvoiceSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceSequence> builder)
    {
        builder.ToTable("invoice_sequences", t =>
            t.HasCheckConstraint("ck_invoice_sequences_number_non_negative", "`last_number` >= 0"));

        builder.HasKey(x => new { x.DomainCode, x.Year });

        builder.Property(x => x.DomainCode).HasMaxLength(16);

        builder.HasOne(x => x.Domain)
            .WithMany()
            .HasForeignKey(x => x.DomainCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
