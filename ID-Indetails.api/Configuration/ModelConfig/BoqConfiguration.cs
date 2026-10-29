using ID_Indetails.api.Business.Boqs;
using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class BoqConfiguration : IEntityTypeConfiguration<Boq>
{
    public void Configure(EntityTypeBuilder<Boq> builder)
    {
        builder.ToTable("boqs", t =>
        {
            t.HasCheckConstraint("ck_boqs_kind", SnakeCaseEnum.CheckSql<BoqKind>("kind"));
            t.HasCheckConstraint("ck_boqs_status", SnakeCaseEnum.CheckSql<BoqStatus>("status"));
            t.HasCheckConstraint("ck_boqs_adjustment_type", SnakeCaseEnum.CheckSql<BoqAdjustmentType>("adjustment_type"));
            // an internal draft has no number and is never agreed; a reason is a VO's
            t.HasCheckConstraint("ck_boqs_number_by_kind", "(`kind` = 'internal') = (`number` IS NULL)");
            t.HasCheckConstraint("ck_boqs_internal_is_draft", "`kind` <> 'internal' OR `status` = 'draft'");
            t.HasCheckConstraint("ck_boqs_reason_only_vo", "`reason` IS NULL OR `kind` = 'vo'");
            t.HasCheckConstraint("ck_boqs_agreed", "(`status` = 'agreed') = (`agreed_at` IS NOT NULL)");
            t.HasCheckConstraint("ck_boqs_adjustment_value", "(`adjustment_type` = 'none') = (`adjustment_value` IS NULL)");
            t.HasCheckConstraint("ck_boqs_adjustment_percent_range", "`adjustment_type` <> 'percent' OR `adjustment_value` BETWEEN -0.99 AND 1");
            // only a VO can lower the price: any other BOQ never goes below zero
            t.HasCheckConstraint("ck_boqs_amounts_by_kind",
                "`kind` = 'vo' OR (`subtotal_usd` >= 0 AND `total_usd` >= 0 AND (`adjustment_type` <> 'total' OR `adjustment_value` >= 0))");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.Number).IsUnique();
        builder.HasIndex(x => new { x.ProjectId, x.Kind, x.Status });

        builder.Property(x => x.Number).HasMaxLength(20);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(BoqRules.MaxTitleLength);
        builder.Property(x => x.Reason).HasMaxLength(BoqRules.MaxReasonLength);
        builder.Property(x => x.AdjustmentValue).HasPrecision(14, 4);
        builder.Property(x => x.SubtotalUsd).HasPrecision(14, 2);
        builder.Property(x => x.TotalUsd).HasPrecision(14, 2);

        builder.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.AgreedById).OnDelete(DeleteBehavior.Restrict);
    }
}

public class BoqLineConfiguration : IEntityTypeConfiguration<BoqLine>
{
    public void Configure(EntityTypeBuilder<BoqLine> builder)
    {
        builder.ToTable("boq_lines", t =>
        {
            t.HasCheckConstraint("ck_boq_lines_quantity_non_negative", "`quantity` >= 0");
            t.HasCheckConstraint("ck_boq_lines_waste_rate_range", "`waste_rate` BETWEEN 0 AND 1");
        });

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.BoqId, x.Position });

        builder.Property(x => x.Code).HasMaxLength(BoqRules.MaxCodeLength);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(BoqRules.MaxDescriptionLength);
        builder.Property(x => x.Quantity).HasPrecision(14, 4);
        builder.Property(x => x.WasteRate).HasPrecision(6, 4);
        builder.Property(x => x.Unit).HasMaxLength(BoqRules.MaxUnitLength);
        builder.Property(x => x.Rate).HasPrecision(14, 4);

        // Lines are part of their BOQ.
        builder.HasOne(x => x.Boq).WithMany(x => x.Lines).HasForeignKey(x => x.BoqId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BoqDeliveryConfiguration : IEntityTypeConfiguration<BoqDelivery>
{
    public void Configure(EntityTypeBuilder<BoqDelivery> builder)
    {
        builder.ToTable("boq_deliveries", t =>
            t.HasCheckConstraint("ck_boq_deliveries_channel", SnakeCaseEnum.CheckSql<DeliveryChannel>("channel")));

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.BoqId);

        builder.Property(x => x.Recipient).HasMaxLength(255);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.TotalUsd).HasPrecision(14, 2);

        builder.HasOne(x => x.Boq).WithMany(x => x.Deliveries).HasForeignKey(x => x.BoqId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.SentById).OnDelete(DeleteBehavior.Restrict);
    }
}

public class BoqSequenceConfiguration : IEntityTypeConfiguration<BoqSequence>
{
    public void Configure(EntityTypeBuilder<BoqSequence> builder)
    {
        builder.ToTable("boq_sequences", t => t.HasCheckConstraint("ck_boq_sequences_number_non_negative", "`last_number` >= 0"));

        builder.HasKey(x => new { x.Prefix, x.Year });

        builder.Property(x => x.Prefix).HasMaxLength(4);
        builder.Property(x => x.Year).ValueGeneratedNever();
    }
}
