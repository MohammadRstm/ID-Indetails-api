using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Body).IsLongText();
        builder.Property(x => x.Kind).IsRequired().HasMaxLength(32);

        // Paging a thread and counting unread messages after a read watermark.
        builder.HasIndex(x => new { x.ConversationId, x.Id });

        builder.HasOne(x => x.Conversation)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Sender)
            .WithMany()
            .HasForeignKey(x => x.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WorkEntry>()
            .WithMany()
            .HasForeignKey(x => x.RefEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WorkEntryComment>()
            .WithMany()
            .HasForeignKey(x => x.RefCommentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
