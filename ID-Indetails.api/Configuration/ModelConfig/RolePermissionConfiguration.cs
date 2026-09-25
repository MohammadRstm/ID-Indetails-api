using ID_Indetails.api.Constants;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");

        builder.HasKey(x => new { x.RoleCode, x.PermissionCode });

        builder.Property(x => x.RoleCode).HasMaxLength(32);

        builder.Property(x => x.PermissionCode).HasMaxLength(64);

        builder.HasOne(x => x.Role)
            .WithMany(x => x.RolePermissions)
            .HasForeignKey(x => x.RoleCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Permission)
            .WithMany()
            .HasForeignKey(x => x.PermissionCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(PermissionCatalog.Grants.SelectMany(grant => grant.Value.Select(code =>
            new RolePermission { RoleCode = grant.Key, PermissionCode = code })));
    }
}
