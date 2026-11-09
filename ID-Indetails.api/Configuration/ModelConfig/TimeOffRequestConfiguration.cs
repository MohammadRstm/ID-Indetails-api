using ID_Indetails.api.Business.TimeOff;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class TimeOffRequestConfiguration : IEntityTypeConfiguration<TimeOffRequest>
{
    public void Configure(EntityTypeBuilder<TimeOffRequest> builder)
    {
        builder.ToTable("time_off_requests", t =>
        {
            t.HasCheckConstraint("ck_time_off_requests_status", SnakeCaseEnum.CheckSql<TimeOffStatus>("status"));
            t.HasCheckConstraint("ck_time_off_requests_days", "`last_day` >= `first_day`");
            // paid or not is decided on approval, and stays with the days taken
            t.HasCheckConstraint(
                "ck_time_off_requests_is_paid",
                "(`status` IN ('approved', 'rejected', 'ended')) = (`is_paid` IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_time_off_requests_ended_on",
                "(`status` IN ('rejected', 'ended')) = (`ended_on` IS NOT NULL) AND (`ended_on` IS NULL OR `ended_on` <= `last_day`)");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.UserId, x.Status, x.FirstDay });
        builder.HasIndex(x => new { x.Status, x.FirstDay });

        builder.Property(x => x.Reason).HasMaxLength(TimeOffRules.MaxNoteLength);
        builder.Property(x => x.DecisionNote).HasMaxLength(TimeOffRules.MaxNoteLength);
        builder.Property(x => x.EndNote).HasMaxLength(TimeOffRules.MaxNoteLength);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.DecidedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.EndedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
