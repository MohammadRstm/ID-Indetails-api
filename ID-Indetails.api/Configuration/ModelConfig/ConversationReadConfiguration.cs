using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ConversationReadConfiguration : IEntityTypeConfiguration<ConversationRead>
{
    public void Configure(EntityTypeBuilder<ConversationRead> builder)
    {
        builder.ToTable("conversation_reads");

        builder.HasKey(x => new { x.UserId, x.ConversationId });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Conversation)
            .WithMany()
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(x => x.LastReadMessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
