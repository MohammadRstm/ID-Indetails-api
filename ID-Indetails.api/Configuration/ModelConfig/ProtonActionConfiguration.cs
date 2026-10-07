using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProtonActionConfiguration : IEntityTypeConfiguration<ProtonAction>
{
    public void Configure(EntityTypeBuilder<ProtonAction> builder)
    {
        builder.ToTable("proton_actions", t =>
        {
            t.HasCheckConstraint("ck_proton_actions_status", SnakeCaseEnum.CheckSql<ProtonActionStatus>("status"));
            t.HasCheckConstraint("ck_proton_actions_risk", SnakeCaseEnum.CheckSql<ProtonActionRisk>("risk"));
        });

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.PublicId).IsUnique();

        builder.Property(x => x.Role).IsRequired().HasMaxLength(32);
        builder.Property(x => x.DomainCode).HasMaxLength(16);
        builder.Property(x => x.Tool).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Arguments).IsRequired().IsJson();
        builder.Property(x => x.Preview).IsRequired().IsJson();
        builder.Property(x => x.TargetType).HasMaxLength(32);
        builder.Property(x => x.ResultType).HasMaxLength(32);
        builder.Property(x => x.ErrorCode).HasMaxLength(64);
        builder.Property(x => x.ErrorMessage).HasMaxLength(500);
        builder.Property(x => x.Model).IsRequired().HasMaxLength(64);
        builder.Property(x => x.PromptVersion).IsRequired().HasMaxLength(16);

        // A conversation's pending proposal (one at a time), and the audit by person and by record.
        builder.HasIndex(x => new { x.ConversationId, x.Status });
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });
        builder.HasIndex(x => new { x.TargetType, x.TargetId });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
