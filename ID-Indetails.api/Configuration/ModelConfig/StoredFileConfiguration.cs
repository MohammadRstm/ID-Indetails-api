using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("files", t =>
            t.HasCheckConstraint("ck_files_size_non_negative", "`size_bytes` >= 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StorageKey).IsRequired().HasMaxLength(255);
        builder.HasIndex(x => x.StorageKey).IsUnique();

        builder.Property(x => x.OriginalName).IsRequired().HasMaxLength(255);
        builder.Property(x => x.MimeType).IsRequired().HasMaxLength(127);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
