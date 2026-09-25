using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class LabourWeekDayConfiguration : IEntityTypeConfiguration<LabourWeekDay>
{
    public void Configure(EntityTypeBuilder<LabourWeekDay> builder)
    {
        builder.ToTable("labour_week_days", t =>
        {
            t.HasCheckConstraint("ck_labour_week_days_workers_non_negative", "`workers` >= 0");
            t.HasCheckConstraint("ck_labour_week_days_pay_non_negative", "`daily_pay` >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.LabourWeekId, x.WorkDate }).IsUnique();

        builder.Property(x => x.Cost)
            .HasComputedColumnSql("`workers` * `daily_pay`", stored: true);

        // Days are part of their week and are replaced together with it.
        builder.HasOne(x => x.LabourWeek)
            .WithMany(x => x.Days)
            .HasForeignKey(x => x.LabourWeekId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
