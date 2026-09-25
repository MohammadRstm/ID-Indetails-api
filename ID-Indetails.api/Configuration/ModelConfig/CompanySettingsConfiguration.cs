using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class CompanySettingsConfiguration : IEntityTypeConfiguration<CompanySettings>
{
    public void Configure(EntityTypeBuilder<CompanySettings> builder)
    {
        builder.ToTable("company_settings", t =>
        {
            // Single-company system: the table can physically hold only one row.
            t.HasCheckConstraint("ck_company_settings_single_row", $"`id` = {CompanySettings.SingletonId}");
            t.HasCheckConstraint("ck_company_settings_week_start", "`week_start` IN (0, 1)");
            t.HasCheckConstraint("ck_company_settings_overtime_multiplier", "`overtime_multiplier` >= 1");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
        builder.Property(x => x.TimeZone).IsRequired().HasMaxLength(64);
        builder.Property(x => x.DefaultCurrency).IsCurrencyCode();
        builder.Property(x => x.OvertimeMultiplier).HasPrecision(4, 2);

        builder.HasOne<StoredFile>()
            .WithMany()
            .HasForeignKey(x => x.LogoFileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UpdatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
