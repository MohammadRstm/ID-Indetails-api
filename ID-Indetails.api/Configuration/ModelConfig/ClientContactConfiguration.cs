using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ClientContactConfiguration : IEntityTypeConfiguration<ClientContact>
{
    public void Configure(EntityTypeBuilder<ClientContact> builder)
    {
        builder.ToTable("client_contacts", t =>
            t.HasCheckConstraint("ck_client_contacts_kind", SnakeCaseEnum.CheckSql<ContactKind>("kind")));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Value).IsRequired().HasMaxLength(255);
        builder.Property(x => x.NormalizedValue).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Label).HasMaxLength(60);

        // Duplicate-contact lookups (same email or phone on another client).
        builder.HasIndex(x => new { x.Kind, x.NormalizedValue });

        // Contacts are part of the client. A client can only be deleted while it has no
        // projects, and then its contacts go with it.
        builder.HasOne(x => x.Client)
            .WithMany(x => x.Contacts)
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
