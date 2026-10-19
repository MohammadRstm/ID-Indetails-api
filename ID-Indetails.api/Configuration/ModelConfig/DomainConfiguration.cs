using ID_Indetails.api.Constants;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class DomainConfiguration : IEntityTypeConfiguration<Domain>
{
    public void Configure(EntityTypeBuilder<Domain> builder)
    {
        builder.ToTable("domains");

        builder.HasKey(x => x.Code);

        builder.Property(x => x.Code).HasMaxLength(16);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(64);

        builder.HasData(
            new Domain { Code = DomainCodes.Design, Name = "Design" },
            new Domain { Code = DomainCodes.Execution, Name = "Execution" },
            new Domain { Code = DomainCodes.Website, Name = "Website" });
    }
}
