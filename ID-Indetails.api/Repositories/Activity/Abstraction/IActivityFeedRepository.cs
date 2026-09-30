namespace ID_Indetails.api.Repositories.Activity.Abstraction;

/// <summary>
/// Events of <paramref name="Domains"/>, plus company-level events (no domain) when <paramref name="WithCompany"/>.
/// <paramref name="Kinds"/> keeps only those kinds; <paramref name="ExcludedKinds"/> leaves those out.
/// <paramref name="ClientId"/> matches the event's client or its project's client.
/// </summary>
public sealed record ActivityFeedSearch(
    IReadOnlyList<string> Domains,
    bool WithCompany,
    IReadOnlyList<string>? Kinds,
    IReadOnlyList<string> ExcludedKinds,
    bool CompanyOnly,
    long? ProjectId,
    long? ClientId,
    long? ActorId,
    int Page,
    int PageSize,
    bool InternalOnly = false);

public sealed record FeedEvent(
    long Id,
    string Kind,
    string? DomainCode,
    DateTime OccurredAt,
    long? ActorId,
    string? ActorName,
    long? ProjectId,
    string? ProjectTitle,
    long? ClientId,
    string? ClientName,
    string? SubjectType,
    long? SubjectId,
    decimal? Amount,
    decimal? PrevAmount,
    string? Payload,
    bool IsDemo);

public interface IActivityFeedRepository
{
    /// <summary>Newest first. Not tracked.</summary>
    Task<(IReadOnlyList<FeedEvent> Items, int TotalCount)> SearchAsync(ActivityFeedSearch search, CancellationToken cancellationToken = default);
}
