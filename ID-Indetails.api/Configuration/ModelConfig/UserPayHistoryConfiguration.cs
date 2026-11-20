using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class UserPayHistoryConfiguration : IEntityTypeConfiguration<UserPayHistory>
{
    public void Configure(EntityTypeBuilder<UserPayHistory> builder)
    {
        builder.ToTable("user_pay_history", t =>
        {
            t.HasCheckConstraint("ck_user_pay_history_amount_positive", "`amount` > 0");
            t.HasCheckConstraint("ck_user_pay_history_rate_non_negative", "`overtime_rate` >= 0");
            t.HasCheckConstraint("ck_user_pay_history_superseded", "(`superseded_at` IS NULL) = (`superseded_by_id` IS NULL)");
        });

        builder.HasKey(x => x.Id);

        // superseded rows are kept for the record only: nothing reads them unless it asks (the pay history)
        builder.HasQueryFilter(x => x.SupersededAt == null);

        // one active row per person and day: NULL for every superseded row, and MySQL allows any number of NULLs in a
        // unique index
        builder.Property(x => x.ActiveEffectiveFrom)
            .HasComputedColumnSql("IF(`superseded_at` IS NULL, `effective_from`, NULL)", stored: true);
        builder.HasIndex(x => new { x.UserId, x.ActiveEffectiveFrom }).IsUnique();

        builder.Property(x => x.Currency).IsCurrencyCode();
        builder.Property(x => x.OvertimeRate).HasPrecision(18, 4);
        builder.Property(x => x.Note).HasMaxLength(255);

        builder.HasOne(x => x.User)
            .WithMany(x => x.PayHistory)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.SupersededById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
