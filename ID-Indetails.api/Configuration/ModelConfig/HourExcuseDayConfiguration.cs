using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class HourExcuseDayConfiguration : IEntityTypeConfiguration<HourExcuseDay>
{
    public void Configure(EntityTypeBuilder<HourExcuseDay> builder)
    {
        builder.ToTable("hour_excuse_days");

        builder.HasKey(x => new { x.ExcuseId, x.Day });

        // Days are part of their excuse. That they fall in the excuse's month, and that a day
        // is not excused twice, spans rows and is checked by the API.
        builder.HasOne(x => x.Excuse)
            .WithMany(x => x.Days)
            .HasForeignKey(x => x.ExcuseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
