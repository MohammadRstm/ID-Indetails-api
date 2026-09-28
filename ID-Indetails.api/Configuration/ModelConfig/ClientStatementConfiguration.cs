using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ClientStatementConfiguration : IEntityTypeConfiguration<ClientStatement>
{
    public void Configure(EntityTypeBuilder<ClientStatement> builder)
    {
        builder.ToTable("client_statements", t =>
        {
            t.HasCheckConstraint("ck_client_statements_mode", SnakeCaseEnum.CheckSql<StatementMode>("mode"));
            t.HasCheckConstraint("ck_client_statements_amounts_non_negative", "`received_usd` >= 0 AND `agreed_usd` >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Number).IsRequired().HasMaxLength(32);
        builder.HasIndex(x => x.Number).IsUnique();

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Client)
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
