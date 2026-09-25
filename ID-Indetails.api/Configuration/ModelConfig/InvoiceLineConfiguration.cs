using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines", t =>
            t.HasCheckConstraint("ck_invoice_lines_amount_positive", "`amount` > 0"));

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.InvoiceId, x.Position }).IsUnique();

        builder.Property(x => x.Description).IsRequired().HasMaxLength(500);

        // Lines belong to their invoice; discarding a draft removes them.
        builder.HasOne(x => x.Invoice)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
