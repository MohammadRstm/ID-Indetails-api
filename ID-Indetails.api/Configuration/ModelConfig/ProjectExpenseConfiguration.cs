using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProjectExpenseConfiguration : IEntityTypeConfiguration<ProjectExpense>
{
    public void Configure(EntityTypeBuilder<ProjectExpense> builder)
    {
        builder.ToTable("project_expenses", t =>
            t.HasCheckConstraint("ck_project_expenses_amount_non_negative", "`amount` >= 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Label).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Currency).IsCurrencyCode();
        builder.Property(x => x.Category).IsRequired().HasMaxLength(40);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasOne(x => x.Project)
            .WithMany(x => x.Expenses)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
