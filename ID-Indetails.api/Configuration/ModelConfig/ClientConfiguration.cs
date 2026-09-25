using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("clients");

        builder.HasKey(x => x.Id);

        // Target of the composite foreign key from projects: a project can only point at a
        // client of its own domain.
        builder.HasAlternateKey(x => new { x.Id, x.DomainCode });

        builder.Property(x => x.DomainCode).IsRequired().HasMaxLength(16);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(150);
        builder.Property(x => x.Organisation).HasMaxLength(200);
        builder.Property(x => x.Label).HasMaxLength(60);
        builder.Property(x => x.Note).IsLongText();

        builder.HasOne(x => x.Domain)
            .WithMany()
            .HasForeignKey(x => x.DomainCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
