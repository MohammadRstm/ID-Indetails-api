using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProjectMaterialConfiguration : IEntityTypeConfiguration<ProjectMaterial>
{
    public void Configure(EntityTypeBuilder<ProjectMaterial> builder)
    {
        builder.ToTable("project_materials", t =>
        {
            t.HasCheckConstraint("ck_project_materials_quantity_positive", "`quantity` > 0");
            t.HasCheckConstraint("ck_project_materials_unit_cost_non_negative", "`unit_cost` >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Quantity).HasPrecision(14, 3);
        builder.Property(x => x.Unit).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Currency).IsCurrencyCode();
        builder.Property(x => x.Category).IsRequired().HasMaxLength(40);
        builder.Property(x => x.Supplier).HasMaxLength(200);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.Property(x => x.Total)
            .HasComputedColumnSql("ROUND(`quantity` * `unit_cost`, 2)", stored: true);

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AddedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
