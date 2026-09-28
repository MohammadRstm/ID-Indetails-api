using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class UserScheduleDayConfiguration : IEntityTypeConfiguration<UserScheduleDay>
{
    public void Configure(EntityTypeBuilder<UserScheduleDay> builder)
    {
        builder.ToTable("user_schedule_days", t =>
        {
            t.HasCheckConstraint("ck_user_schedule_days_weekday", "`weekday` BETWEEN 0 AND 6");

            // One span inside a single day: no overnight shifts.
            t.HasCheckConstraint(
                "ck_user_schedule_days_hours",
                "`start_minute` >= 0 AND `end_minute` <= 1440 AND `start_minute` < `end_minute`");
        });

        builder.HasKey(x => new { x.ScheduleId, x.Weekday });

        // Days are part of their schedule.
        builder.HasOne(x => x.Schedule)
            .WithMany(x => x.Days)
            .HasForeignKey(x => x.ScheduleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
