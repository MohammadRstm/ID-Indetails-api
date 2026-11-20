using ID_Indetails.api.Business.Boqs;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class BoqLineProgressConfiguration : IEntityTypeConfiguration<BoqLineProgress>
{
    public void Configure(EntityTypeBuilder<BoqLineProgress> builder)
    {
        builder.ToTable("boq_line_progress", t =>
            t.HasCheckConstraint("ck_boq_line_progress_executed_quantity_non_negative", "`executed_quantity` >= 0"));

        builder.HasKey(x => x.Id);

        // one record per line and day: a second the same day replaces it
        builder.HasIndex(x => new { x.BoqLineId, x.RecordedOn }).IsUnique();
        builder.HasIndex(x => new { x.ProjectId, x.RecordedOn });

        // the same precision as the line's quantity
        builder.Property(x => x.ExecutedQuantity).HasPrecision(14, 4);
        builder.Property(x => x.Note).HasMaxLength(BoqProgressRules.MaxNoteLength);

        // progress is part of its line, and goes with it when the line is removed from a BOQ (only a draft's lines can be)
        builder.HasOne<BoqLine>().WithMany().HasForeignKey(x => x.BoqLineId).OnDelete(DeleteBehavior.Cascade);
        // and with the project, like its budget, when a project made by mistake is deleted
        builder.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.RecordedById).OnDelete(DeleteBehavior.Restrict);
    }
}
