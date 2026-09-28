using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ClientStatementDeliveryConfiguration : IEntityTypeConfiguration<ClientStatementDelivery>
{
    public void Configure(EntityTypeBuilder<ClientStatementDelivery> builder)
    {
        builder.ToTable("client_statement_deliveries", t =>
            t.HasCheckConstraint("ck_client_statement_deliveries_channel", SnakeCaseEnum.CheckSql<DeliveryChannel>("channel")));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Recipient).HasMaxLength(255);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasOne(x => x.Statement)
            .WithMany(x => x.Deliveries)
            .HasForeignKey(x => x.StatementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.SentById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
