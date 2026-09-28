using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ClientStatementLineConfiguration : IEntityTypeConfiguration<ClientStatementLine>
{
    public void Configure(EntityTypeBuilder<ClientStatementLine> builder)
    {
        builder.ToTable("client_statement_lines", t =>
            t.HasCheckConstraint("ck_client_statement_lines_price_non_negative", "`price` >= 0"));

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.StatementId, x.MaterialId }).IsUnique();
        builder.HasIndex(x => new { x.StatementId, x.Position }).IsUnique();

        builder.Property(x => x.Description).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Quantity).HasPrecision(14, 3);
        builder.Property(x => x.Unit).HasMaxLength(20);

        // Lines are part of their statement.
        builder.HasOne(x => x.Statement)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.StatementId)
            .OnDelete(DeleteBehavior.Cascade);

        // A stated material can never be removed from under a statement.
        builder.HasOne(x => x.Material)
            .WithMany()
            .HasForeignKey(x => x.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
