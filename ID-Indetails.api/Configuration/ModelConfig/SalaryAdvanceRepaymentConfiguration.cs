using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class SalaryAdvanceRepaymentConfiguration : IEntityTypeConfiguration<SalaryAdvanceRepayment>
{
    public void Configure(EntityTypeBuilder<SalaryAdvanceRepayment> builder)
    {
        builder.ToTable("salary_advance_repayments", t =>
        {
            t.HasCheckConstraint("ck_salary_advance_repayments_amount_positive", "`amount` > 0");
            t.HasCheckConstraint("ck_salary_advance_repayments_month", "DAY(`month`) = 1");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.AdvanceId, x.Month }).IsUnique();
    }
}
