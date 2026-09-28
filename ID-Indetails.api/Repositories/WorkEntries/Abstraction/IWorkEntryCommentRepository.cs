using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.WorkEntries.Abstraction;

public sealed record WorkEntryCommentView(
    long Id,
    long AuthorId,
    string AuthorName,
    string AuthorRole,
    string Body,
    bool SentToGroup,
    DateTime CreatedAt);

public interface IWorkEntryCommentRepository
{
    /// <summary>Oldest first. Not tracked.</summary>
    Task<IReadOnlyList<WorkEntryCommentView>> ListAsync(long workEntryId, CancellationToken cancellationToken = default);

    void Add(WorkEntryComment comment);
}
