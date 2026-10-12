using ID_Indetails.api.Business.Proton;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProtonMemoryConfiguration : IEntityTypeConfiguration<ProtonMemory>
{
    public void Configure(EntityTypeBuilder<ProtonMemory> builder)
    {
        builder.ToTable("proton_memories");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Text).IsRequired().HasMaxLength(ProtonMemoryRules.MaxLength);

        // a person's memories, oldest first
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
