using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class SalaryAdvanceConfiguration : IEntityTypeConfiguration<SalaryAdvance>
{
    public void Configure(EntityTypeBuilder<SalaryAdvance> builder)
    {
        builder.ToTable("salary_advances", t =>
        {
            t.HasCheckConstraint("ck_salary_advances_status", SnakeCaseEnum.CheckSql<SalaryAdvanceStatus>("status"));
            t.HasCheckConstraint("ck_salary_advances_settled_kind", SnakeCaseEnum.CheckSql<SalaryAdvanceSettlement>("settled_kind"));
            t.HasCheckConstraint("ck_salary_advances_amount_positive", "`amount` > 0 AND (`asked_amount` IS NULL OR `asked_amount` > 0)");
            // the money changes hands once it is given; a cancelled one was given too
            t.HasCheckConstraint("ck_salary_advances_given_on", "(`status` IN ('approved', 'cancelled')) = (`given_on` IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_salary_advances_settled",
                "(`settled_kind` IS NULL) = (`settled_on` IS NULL) AND (`settled_kind` IS NULL) = (`settled_amount` IS NULL) " +
                "AND (`settled_kind` IS NULL OR `status` = 'approved') AND (`settled_amount` IS NULL OR `settled_amount` > 0)");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.UserId, x.Status });
        builder.HasIndex(x => new { x.Status, x.CreatedAt });

        builder.Property(x => x.Currency).IsCurrencyCode();
        builder.Property(x => x.Reason).HasMaxLength(AdvanceRules.MaxNoteLength);
        builder.Property(x => x.DecisionNote).HasMaxLength(AdvanceRules.MaxNoteLength);
        builder.Property(x => x.SettlementNote).HasMaxLength(AdvanceRules.MaxNoteLength);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.DecidedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CancelledById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.SettledById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Repayments)
            .WithOne(x => x.Advance)
            .HasForeignKey(x => x.AdvanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
