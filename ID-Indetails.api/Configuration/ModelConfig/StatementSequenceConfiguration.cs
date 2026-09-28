using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class StatementSequenceConfiguration : IEntityTypeConfiguration<StatementSequence>
{
    public void Configure(EntityTypeBuilder<StatementSequence> builder)
    {
        builder.ToTable("statement_sequences", t =>
            t.HasCheckConstraint("ck_statement_sequences_number_non_negative", "`last_number` >= 0"));

        builder.HasKey(x => x.Year);

        builder.Property(x => x.Year).ValueGeneratedNever();
    }
}
