using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class BillingSettingsConfiguration : IEntityTypeConfiguration<BillingSettings>
{
    public void Configure(EntityTypeBuilder<BillingSettings> builder)
    {
        builder.ToTable("billing_settings", t =>
        {
            t.HasCheckConstraint("ck_billing_settings_terms_non_negative", "`default_terms_days` >= 0");
            t.HasCheckConstraint(
                "ck_billing_settings_reminders",
                "`reminder_first_after_days` BETWEEN 1 AND 60 AND `reminder_every_days` BETWEEN 1 AND 60 AND `reminder_max` BETWEEN 1 AND 10");
        });

        builder.HasKey(x => x.DomainCode);

        builder.Property(x => x.DomainCode).HasMaxLength(16);
        builder.Property(x => x.InvoicePrefix).IsRequired().HasMaxLength(8);
        builder.Property(x => x.BankAccount).HasMaxLength(60);
        builder.Property(x => x.BankName).HasMaxLength(120);
        builder.Property(x => x.Iban).HasMaxLength(34);
        builder.Property(x => x.Swift).HasMaxLength(11);
        builder.Property(x => x.TermsText).IsLongText();
        builder.Property(x => x.Footer).HasMaxLength(500);

        builder.HasOne(x => x.Domain)
            .WithOne()
            .HasForeignKey<BillingSettings>(x => x.DomainCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UpdatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
