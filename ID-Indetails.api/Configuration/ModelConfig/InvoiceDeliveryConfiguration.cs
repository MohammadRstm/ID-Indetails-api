using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class InvoiceDeliveryConfiguration : IEntityTypeConfiguration<InvoiceDelivery>
{
    public void Configure(EntityTypeBuilder<InvoiceDelivery> builder)
    {
        builder.ToTable("invoice_deliveries", t =>
            t.HasCheckConstraint("ck_invoice_deliveries_channel", SnakeCaseEnum.CheckSql<DeliveryChannel>("channel")));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Recipient).HasMaxLength(255);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasOne(x => x.Invoice)
            .WithMany(x => x.Deliveries)
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.SentById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
