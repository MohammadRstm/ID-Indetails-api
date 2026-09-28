using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", t =>
        {
            t.HasCheckConstraint("ck_payments_method", SnakeCaseEnum.CheckSql<PaymentMethod>("method"));
            t.HasCheckConstraint("ck_payments_amount_positive", "`amount` > 0");
            t.HasCheckConstraint("ck_payments_fx_rate_positive", "`fx_rate` > 0");
            t.HasCheckConstraint("ck_payments_amount_usd_positive", "`amount_usd` > 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Currency).IsCurrencyCode();
        builder.Property(x => x.FxRate).HasPrecision(18, 6);
        builder.Property(x => x.Reference).HasMaxLength(100);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.VoidReason).HasMaxLength(500);

        // One live initial payment per project: NULL for every other payment, and MySQL
        // allows any number of NULLs in a unique index.
        builder.Property(x => x.InitialForProjectId)
            .HasComputedColumnSql("IF(`is_initial` = 1 AND `voided_at` IS NULL, `project_id`, NULL)", stored: true);
        builder.HasIndex(x => x.InitialForProjectId).IsUnique();

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Client)
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Invoice)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RecordedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.VoidedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
