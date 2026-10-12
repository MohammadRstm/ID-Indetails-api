using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProtonUsageConfiguration : IEntityTypeConfiguration<ProtonUsage>
{
    public void Configure(EntityTypeBuilder<ProtonUsage> builder)
    {
        builder.ToTable("proton_usage", t =>
        {
            t.HasCheckConstraint("ck_proton_usage_outcome", SnakeCaseEnum.CheckSql<ProtonOutcome>("outcome"));
            t.HasCheckConstraint("ck_proton_usage_channel", SnakeCaseEnum.CheckSql<ProtonChannel>("channel"));
            t.HasCheckConstraint("ck_proton_usage_voice_seconds_non_negative", "`voice_seconds` IS NULL OR `voice_seconds` >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Model).IsRequired().HasMaxLength(64);
        builder.Property(x => x.PromptVersion).IsRequired().HasMaxLength(16);
        builder.Property(x => x.CostUsd).HasPrecision(12, 6);
        builder.Property(x => x.Question).IsRequired().IsLongText();
        builder.Property(x => x.Answer).IsLongText();
        builder.Property(x => x.Tools).IsRequired().IsJson();

        // The month's spend (budget) and one person's questions this month (allowance).
        builder.HasIndex(x => x.OccurredAt);
        builder.HasIndex(x => new { x.UserId, x.OccurredAt });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
