using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class ProtonStoredConversationConfiguration : IEntityTypeConfiguration<ProtonStoredConversation>
{
    public const int TitleLength = 120;

    public void Configure(EntityTypeBuilder<ProtonStoredConversation> builder)
    {
        builder.ToTable("proton_conversations", t =>
            t.HasCheckConstraint("ck_proton_conversations_message_count_non_negative", "`message_count` >= 0"));

        builder.HasKey(x => x.Id);
        // the id is the one the chat already had in memory: never generated here
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).IsRequired().HasMaxLength(TitleLength);
        builder.Property(x => x.AccessFingerprint).IsRequired().HasMaxLength(128);
        builder.Property(x => x.AccessPermissions).IsRequired().IsJson();
        builder.Property(x => x.AccessDomains).IsRequired().IsJson();

        // a person's chats, most recent first
        builder.HasIndex(x => new { x.UserId, x.UpdatedAt });
        // the retention job's scan
        builder.HasIndex(x => x.UpdatedAt);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProtonStoredMessageConfiguration : IEntityTypeConfiguration<ProtonStoredMessage>
{
    public void Configure(EntityTypeBuilder<ProtonStoredMessage> builder)
    {
        builder.ToTable("proton_conversation_messages", t =>
        {
            t.HasCheckConstraint("ck_proton_conversation_messages_role", SnakeCaseEnum.CheckSql<ProtonMessageRole>("role"));
            t.HasCheckConstraint("ck_proton_conversation_messages_origin", $"`origin` IS NULL OR {SnakeCaseEnum.CheckSql<ProtonMessageOrigin>("origin")}");
            t.HasCheckConstraint("ck_proton_conversation_messages_seq_positive", "`seq` > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Text).IsRequired().IsLongText();
        builder.Property(x => x.Refs).IsJson();
        builder.Property(x => x.Sources).IsJson();
        builder.Property(x => x.ActionIds).IsJson();
        builder.Property(x => x.Blocks).IsJson();

        builder.HasIndex(x => new { x.ConversationId, x.Seq }).IsUnique();

        builder.HasOne(x => x.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
