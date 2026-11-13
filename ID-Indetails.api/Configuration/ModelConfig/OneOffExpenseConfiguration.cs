using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class OneOffExpenseConfiguration : IEntityTypeConfiguration<OneOffExpense>
{
    public void Configure(EntityTypeBuilder<OneOffExpense> builder)
    {
        builder.ToTable("one_off_expenses", t =>
            t.HasCheckConstraint("ck_one_off_expenses_amount_positive", "`amount` > 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
        builder.Property(x => x.Currency).IsCurrencyCode();

        builder.HasIndex(x => x.SpentOn);

        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
