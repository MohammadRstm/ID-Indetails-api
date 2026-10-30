using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class BlockedChangeConfiguration : IEntityTypeConfiguration<BlockedChange>
{
    public void Configure(EntityTypeBuilder<BlockedChange> builder)
    {
        builder.ToTable("blocked_changes", t =>
        {
            t.HasCheckConstraint("ck_blocked_changes_kind", SnakeCaseEnum.CheckSql<BlockedChangeKind>("kind"));
            t.HasCheckConstraint("ck_blocked_changes_status", SnakeCaseEnum.CheckSql<BlockedChangeStatus>("status"));
            // decided once: who and when go with any status but pending
            t.HasCheckConstraint("ck_blocked_changes_decided", "(`status` = 'pending') = (`decided_at` IS NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ProjectId, x.Status });

        builder.Property(x => x.Kind).HasMaxLength(30);
        builder.Property(x => x.Payload).IsRequired().HasColumnType("text");

        builder.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.DecidedById).OnDelete(DeleteBehavior.Restrict);
    }
}
