using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Files.Abstraction;

/// <summary>An attached file, as lists show it.</summary>
public sealed record FileView(long Id, string Name, string MimeType, long SizeBytes, DateTime CreatedAt);

/// <summary>Everything a file is attached to, which decides who may download it.</summary>
/// <param name="IsWebsite">A picture or the favicon of the company's public website.</param>
public sealed record FileUses(bool IsPhoto, bool IsLogo, IReadOnlyList<long> ConversationIds, IReadOnlyList<long> EntryIds, bool IsWebsite)
{
    public bool IsUsed => IsPhoto || IsLogo || IsWebsite || ConversationIds.Count > 0 || EntryIds.Count > 0;
}

public interface IFileRepository
{
    Task<StoredFile?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Locks the file's row until the transaction ends, so it can't be attached twice at once. Tracked.</summary>
    Task<StoredFile?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    Task<FileUses> UsesAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Up to <paramref name="limit"/> files uploaded before <paramref name="cutoff"/> and attached to nothing. Tracked.</summary>
    Task<IReadOnlyList<StoredFile>> UnusedBeforeAsync(DateTime cutoff, int limit, CancellationToken cancellationToken = default);

    /// <summary>How many bytes the person has uploaded since <paramref name="since"/>, deleted uploads included.</summary>
    Task<long> BytesUploadedSinceAsync(long userId, DateTime since, CancellationToken cancellationToken = default);

    void Add(StoredFile file);

    void Remove(StoredFile file);
}
