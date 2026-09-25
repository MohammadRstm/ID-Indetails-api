using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class MessageMentionConfiguration : IEntityTypeConfiguration<MessageMention>
{
    public void Configure(EntityTypeBuilder<MessageMention> builder)
    {
        builder.ToTable("message_mentions");

        builder.HasKey(x => new { x.MessageId, x.UserId });

        builder.HasOne(x => x.Message)
            .WithMany(x => x.Mentions)
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
