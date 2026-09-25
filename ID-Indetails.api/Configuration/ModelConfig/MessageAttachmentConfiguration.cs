using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class MessageAttachmentConfiguration : IEntityTypeConfiguration<MessageAttachment>
{
    public void Configure(EntityTypeBuilder<MessageAttachment> builder)
    {
        builder.ToTable("message_attachments");

        builder.HasKey(x => new { x.MessageId, x.FileId });

        builder.HasOne(x => x.Message)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.File)
            .WithMany()
            .HasForeignKey(x => x.FileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
