using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class BoxEntryConfiguration : IEntityTypeConfiguration<BoxEntry>
{
    public void Configure(EntityTypeBuilder<BoxEntry> builder)
    {
        builder.ToTable("box_entries", t =>
        {
            t.HasCheckConstraint("ck_box_entries_kind", SnakeCaseEnum.CheckSql<BoxEntryKind>("kind"));
            // a removal is recorded even when nothing was left to take out
            t.HasCheckConstraint("ck_box_entries_amount_not_zero", "`amount` <> 0 OR `kind` = 'removed'");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => new { x.ProjectId, x.Kind });
        builder.HasIndex(x => x.PaymentId);

        builder.Property(x => x.Note).HasMaxLength(BoxRules.MaxNoteLength);

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
