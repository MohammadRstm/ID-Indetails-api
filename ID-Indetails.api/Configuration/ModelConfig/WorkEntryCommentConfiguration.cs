using ID_Indetails.api.Extensions;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ID_Indetails.api.Configuration.ModelConfig;

public class WorkEntryCommentConfiguration : IEntityTypeConfiguration<WorkEntryComment>
{
    public void Configure(EntityTypeBuilder<WorkEntryComment> builder)
    {
        builder.ToTable("work_entry_comments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Body).IsRequired().IsLongText();

        builder.HasOne(x => x.WorkEntry)
            .WithMany(x => x.Comments)
            .HasForeignKey(x => x.WorkEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Author)
            .WithMany()
            .HasForeignKey(x => x.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
