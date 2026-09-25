using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class DomainSettingsConfiguration : IEntityTypeConfiguration<DomainSettings>
{
    public void Configure(EntityTypeBuilder<DomainSettings> builder)
    {
        builder.ToTable("domain_settings", t =>
        {
            t.HasCheckConstraint("ck_domain_settings_edit_window", SnakeCaseEnum.CheckSql<EditWindow>("edit_window"));
            t.HasCheckConstraint("ck_domain_settings_notify_entry", SnakeCaseEnum.CheckSql<EntryNotificationMode>("notify_entry"));
        });

        builder.HasKey(x => x.DomainCode);

        builder.Property(x => x.DomainCode).HasMaxLength(16);

        builder.HasOne(x => x.Domain)
            .WithOne()
            .HasForeignKey<DomainSettings>(x => x.DomainCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UpdatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
