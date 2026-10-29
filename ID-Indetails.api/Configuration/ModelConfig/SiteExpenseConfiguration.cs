using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class SiteExpenseConfiguration : IEntityTypeConfiguration<SiteExpense>
{
    public void Configure(EntityTypeBuilder<SiteExpense> builder)
    {
        builder.ToTable("site_expenses", t =>
        {
            // A cost must be knowable: either quantity × unit cost, or a lump-sum amount.
            // COALESCE because a CHECK only rejects FALSE: a comparison with NULL is UNKNOWN
            // and would let a row with neither form through.
            t.HasCheckConstraint(
                "ck_site_expenses_has_cost",
                "(COALESCE(`quantity`, 0) > 0 AND COALESCE(`unit_cost`, 0) > 0) OR COALESCE(`amount`, 0) > 0");

            t.HasCheckConstraint("ck_site_expenses_quantity_positive", "`quantity` IS NULL OR `quantity` > 0");
            t.HasCheckConstraint("ck_site_expenses_unit_cost_non_negative", "`unit_cost` IS NULL OR `unit_cost` >= 0");
            t.HasCheckConstraint("ck_site_expenses_amount_positive", "`amount` IS NULL OR `amount` > 0");
            t.HasCheckConstraint("ck_site_expenses_category", SnakeCaseEnum.CheckSql<SiteExpenseCategory>("category"));
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Quantity).HasPrecision(14, 3);
        builder.Property(x => x.Unit).HasMaxLength(20);
        builder.Property(x => x.UnitCost).HasPrecision(18, 4);
        builder.Property(x => x.Currency).IsCurrencyCode();
        builder.Property(x => x.Supplier).HasMaxLength(200);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.Category).HasDefaultValue(SiteExpenseCategory.Materials);

        builder.Property(x => x.Total)
            .HasComputedColumnSql(
                "CASE WHEN `quantity` > 0 AND `unit_cost` > 0 THEN ROUND(`quantity` * `unit_cost`, 2) ELSE `amount` END",
                stored: true);

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AddedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountLabel>()
            .WithMany()
            .HasForeignKey(x => x.AccountLabelId)
            .OnDelete(DeleteBehavior.Restrict);

        // MySQL allows any number of NULLs in a unique index, so the website's expenses (no ref) are unaffected.
        builder.HasIndex(x => new { x.AddedById, x.ClientRef }).IsUnique();
    }
}
