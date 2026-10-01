using ID_Indetails.api.Business.Company;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class OverheadGroupConfiguration : IEntityTypeConfiguration<OverheadGroup>
{
    /// <summary>The two groups overhead had before categories could be managed; existing lines move into them.</summary>
    public const long Software = 1;
    public const long Operating = 2;

    public void Configure(EntityTypeBuilder<OverheadGroup> builder)
    {
        builder.ToTable("overhead_groups");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(OverheadRules.MaxGroupName);
        builder.HasIndex(x => x.Name).IsUnique();

        var seeded = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        string[] names =
        [
            "Software & services", "Operating costs", "Rent & utilities", "Internet & phone", "Marketing & advertising",
            "Professional fees", "Insurance", "Office supplies", "Equipment & maintenance", "Vehicles & transport",
            "Bank fees", "Taxes & licences", "Training & memberships",
        ];
        builder.HasData(names
            .Select((name, i) => new OverheadGroup { Id = i + 1, Name = name, SortOrder = (i + 1) * 10, CreatedAt = seeded })
            .Append(new OverheadGroup { Id = names.Length + 1, Name = "Other", SortOrder = OverheadRules.LastSortOrder, CreatedAt = seeded }));
    }
}
