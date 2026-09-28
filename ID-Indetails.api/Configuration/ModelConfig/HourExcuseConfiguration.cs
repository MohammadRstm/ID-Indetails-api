using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class HourExcuseConfiguration : IEntityTypeConfiguration<HourExcuse>
{
    public void Configure(EntityTypeBuilder<HourExcuse> builder)
    {
        builder.ToTable("hour_excuses", t =>
        {
            t.HasCheckConstraint("ck_hour_excuses_kind", SnakeCaseEnum.CheckSql<ExcuseKind>("kind"));
            t.HasCheckConstraint("ck_hour_excuses_month_start", "DAYOFMONTH(`month_start`) = 1");

            // Minutes are given exactly for the "minutes" kind, and are positive.
            t.HasCheckConstraint(
                "ck_hour_excuses_minutes",
                "(`kind` = 'minutes') = (`minutes` IS NOT NULL) AND (`minutes` IS NULL OR `minutes` > 0)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Note).IsRequired().HasMaxLength(255);

        builder.HasIndex(x => new { x.UserId, x.MonthStart });

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
            .HasForeignKey(x => x.DeletedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
