using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class DisciplineConfiguration : IEntityTypeConfiguration<Discipline>
{
    public void Configure(EntityTypeBuilder<Discipline> builder)
    {
        builder.ToTable("disciplines");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DomainCode).IsRequired().HasMaxLength(16);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);

        builder.HasIndex(x => new { x.DomainCode, x.Name }).IsUnique();

        builder.HasOne(x => x.Domain)
            .WithMany()
            .HasForeignKey(x => x.DomainCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
