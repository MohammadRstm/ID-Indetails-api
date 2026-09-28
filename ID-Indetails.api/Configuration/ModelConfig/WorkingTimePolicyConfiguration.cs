using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class WorkingTimePolicyConfiguration : IEntityTypeConfiguration<WorkingTimePolicy>
{
    /// <summary>Monday–Friday as a bitmask (bits 1–5).</summary>
    public const byte MondayToFriday = 0b0111110;

    public void Configure(EntityTypeBuilder<WorkingTimePolicy> builder)
    {
        builder.ToTable("working_time_policies", t =>
        {
            t.HasCheckConstraint(
                "ck_working_time_policies_hours",
                "`work_start_minute` >= 0 AND `work_end_minute` <= 1440 AND `work_start_minute` < `work_end_minute`");

            // At least one working day; only bits 0–6 (Sunday–Saturday) exist.
            t.HasCheckConstraint("ck_working_time_policies_work_days", "`work_days` BETWEEN 1 AND 127");

            t.HasCheckConstraint(
                "ck_working_time_policies_break",
                "`break_minutes` >= 0 AND `break_minutes` < `work_end_minute` - `work_start_minute`");

            t.HasCheckConstraint("ck_working_time_policies_overtime_multiplier", "`overtime_multiplier` >= 1");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.EffectiveFrom).IsUnique();

        builder.Property(x => x.OvertimeMultiplier).HasPrecision(4, 2);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        // The prototype's default: Monday–Friday 09:00–17:00, a paid 1-hour break, overtime
        // at the normal rate. In force from before any real data, so a policy always applies.
        builder.HasData(new WorkingTimePolicy
        {
            Id = 1,
            EffectiveFrom = new DateOnly(2000, 1, 1),
            WorkStartMinute = 9 * 60,
            WorkEndMinute = 17 * 60,
            WorkDays = MondayToFriday,
            BreakMinutes = 60,
            OvertimeMultiplier = 1m,
            CreatedAt = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
