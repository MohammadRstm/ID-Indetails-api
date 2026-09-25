using ID_Indetails.api.Constants;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");

        builder.HasKey(x => x.Code);

        builder.Property(x => x.Code).HasMaxLength(64);

        builder.Property(x => x.Description).IsRequired().HasMaxLength(255);

        builder.Property(x => x.DomainCode).HasMaxLength(16);

        builder.HasOne(x => x.Domain)
            .WithMany()
            .HasForeignKey(x => x.DomainCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(PermissionCatalog.Definitions.Select(d => new Permission
        {
            Code = d.Code,
            Description = d.Description,
            DomainCode = d.DomainCode,
        }));
    }
}
