using ID_Indetails.api.Constants;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(x => x.Code);

        builder.Property(x => x.Code).HasMaxLength(32);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(64);

        builder.HasData(
            new Role { Code = RoleCodes.SuperAdmin, Name = "Super admin", Rank = 3 },
            new Role { Code = RoleCodes.Admin, Name = "Admin", Rank = 2 },
            new Role { Code = RoleCodes.Employee, Name = "Employee", Rank = 1 });
    }
}
