using ID_Indetails.api.Business.Company;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class AccountLabelConfiguration : IEntityTypeConfiguration<AccountLabel>
{
    public void Configure(EntityTypeBuilder<AccountLabel> builder)
    {
        builder.ToTable("account_labels");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(AccountLabelRules.MaxName);
        builder.HasIndex(x => x.Name).IsUnique();

        var seeded = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            new AccountLabel { Id = 1, Name = "Concrete", Position = 1, CreatedAt = seeded },
            new AccountLabel { Id = 2, Name = "Finishing", Position = 2, CreatedAt = seeded },
            new AccountLabel { Id = 3, Name = "Permits", Position = 3, CreatedAt = seeded });
    }
}
