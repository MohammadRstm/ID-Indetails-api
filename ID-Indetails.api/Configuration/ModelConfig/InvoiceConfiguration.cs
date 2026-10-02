using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", t =>
        {
            t.HasCheckConstraint("ck_invoices_total_non_negative", "`total` >= 0");
            t.HasCheckConstraint("ck_invoices_terms_non_negative", "`terms_days` >= 0");

            // A number is assigned exactly when the invoice is issued, never to a draft.
            t.HasCheckConstraint("ck_invoices_numbered_when_issued", "(`number` IS NULL) = (`issued_at` IS NULL)");
            t.HasCheckConstraint("ck_invoices_issued_has_due_date", "`issued_at` IS NULL OR `due_at` IS NOT NULL");

            // Only a draft waits for its day.
            t.HasCheckConstraint("ck_invoices_scheduled_is_draft", "`send_on` IS NULL OR `issued_at` IS NULL");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Number).HasMaxLength(32);
        builder.HasIndex(x => x.Number).IsUnique();

        builder.Property(x => x.Currency).IsCurrencyCode();
        builder.Property(x => x.Notes).IsLongText();
        builder.Property(x => x.BillToName).HasMaxLength(200);
        builder.Property(x => x.BillToContact).HasMaxLength(255);
        builder.Property(x => x.VoidReason).HasMaxLength(500);
        builder.Property(x => x.SendTo).HasMaxLength(254);

        // the hourly job's search for drafts whose day has come
        builder.HasIndex(x => x.SendOn);

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Client)
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.VoidedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ScheduledById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
