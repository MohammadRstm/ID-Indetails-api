using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class PocketEntryConfiguration : IEntityTypeConfiguration<PocketEntry>
{
    public void Configure(EntityTypeBuilder<PocketEntry> builder)
    {
        builder.ToTable("pocket_entries", t =>
        {
            t.HasCheckConstraint("ck_pocket_entries_kind", SnakeCaseEnum.CheckSql<PocketEntryKind>("kind"));
            // a contribution goes in, a payout comes out
            t.HasCheckConstraint("ck_pocket_entries_amount_sign", "(`kind` = 'contribution' AND `amount` > 0) OR (`kind` = 'payout' AND `amount` < 0)");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.Kind, x.CreatedAt });
        builder.HasIndex(x => x.ProjectId);
        builder.HasIndex(x => x.RequestId).IsUnique();

        builder.Property(x => x.Note).HasMaxLength(PocketRules.MaxNoteLength);

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PocketRequest>()
            .WithMany()
            .HasForeignKey(x => x.RequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
