using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PocketRequestConfiguration : IEntityTypeConfiguration<PocketRequest>
{
    public void Configure(EntityTypeBuilder<PocketRequest> builder)
    {
        builder.ToTable("pocket_requests", t =>
        {
            t.HasCheckConstraint("ck_pocket_requests_status", SnakeCaseEnum.CheckSql<PocketRequestStatus>("status"));
            t.HasCheckConstraint("ck_pocket_requests_suggested_non_negative", "`suggested_amount` >= 0");
            // approved with a positive amount, otherwise none
            t.HasCheckConstraint(
                "ck_pocket_requests_approved_amount",
                "(`status` = 'approved' AND `approved_amount` > 0) OR (`status` <> 'approved' AND `approved_amount` IS NULL)");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.Status, x.RequestedAt });
        builder.HasIndex(x => x.ProjectId);

        builder.Property(x => x.Note).HasMaxLength(PocketRules.MaxNoteLength);
        builder.Property(x => x.DecisionNote).HasMaxLength(PocketRules.MaxNoteLength);

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RequestedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.DecidedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
