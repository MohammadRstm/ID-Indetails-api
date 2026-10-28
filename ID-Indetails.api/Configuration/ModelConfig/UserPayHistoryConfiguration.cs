using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
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
            t.HasCheckConstraint("ck_user_pay_history_basis", SnakeCaseEnum.CheckSql<PayBasis>("basis"));
            t.HasCheckConstraint("ck_user_pay_history_amount_positive", "`amount` > 0");
            t.HasCheckConstraint("ck_user_pay_history_rate_non_negative", "`overtime_rate` >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.UserId, x.EffectiveFrom }).IsUnique();

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
    }
}
