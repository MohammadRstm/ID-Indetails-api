using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class OverheadItemConfiguration : IEntityTypeConfiguration<OverheadItem>
{
    public void Configure(EntityTypeBuilder<OverheadItem> builder)
    {
        builder.ToTable("overhead_items", t =>
        {
            t.HasCheckConstraint("ck_overhead_items_end_after_start", "`ended_on` IS NULL OR `ended_on` >= `started_on`");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
        builder.Property(x => x.Currency).IsCurrencyCode();

        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
