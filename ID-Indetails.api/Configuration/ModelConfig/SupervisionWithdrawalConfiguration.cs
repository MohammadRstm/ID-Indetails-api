using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class SupervisionWithdrawalConfiguration : IEntityTypeConfiguration<SupervisionWithdrawal>
{
    public void Configure(EntityTypeBuilder<SupervisionWithdrawal> builder)
    {
        builder.ToTable("supervision_withdrawals", t =>
            t.HasCheckConstraint("ck_supervision_withdrawals_amount_positive", "`amount_usd` > 0"));

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.ProjectId);

        builder.Property(x => x.AmountUsd).HasPrecision(14, 2);
        builder.Property(x => x.Note).HasMaxLength(SupervisionRules.MaxNoteLength);

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
