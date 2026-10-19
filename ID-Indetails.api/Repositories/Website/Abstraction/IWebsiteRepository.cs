using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Website.Abstraction;

/// <summary>What the website takes from a system project: its status and year follow it; the rest fills a new form.</summary>
public sealed record SystemProjectFacts(
    long Id,
    string Title,
    string DomainCode,
    ProjectStatus Status,
    DateTime? CompletedAt,
    DateOnly? TargetDate,
    DateOnly StartDate,
    string? ClientName,
    string? Description,
    long? WebsiteProjectId);

/// <summary>The website project or office whose place is being worked out, so its own point doesn't count as taken.</summary>
public sealed record PlaceOwner(long? ProjectId, long? LocationId);

public interface IWebsiteRepository
{
    /// <summary>Whether another website project or office already sits at exactly this point.</summary>
    Task<bool> PositionTakenAsync(decimal latitude, decimal longitude, PlaceOwner owner, CancellationToken cancellationToken = default);

    /// <summary>Every website project with its gallery, newest first; only the published ones when asked. Untracked.</summary>
    Task<IReadOnlyList<WebsiteProject>> ProjectsAsync(bool publishedOnly, CancellationToken cancellationToken = default);

    /// <summary>One project with its gallery. Tracked.</summary>
    Task<WebsiteProject?> ProjectAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>A published project by its page address, with its gallery. Untracked.</summary>
    Task<WebsiteProject?> PublishedProjectAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>The projects in the moving strip. Tracked.</summary>
    Task<IReadOnlyList<WebsiteProject>> ConveyorAsync(CancellationToken cancellationToken = default);

    /// <summary>These projects. Tracked.</summary>
    Task<IReadOnlyList<WebsiteProject>> ProjectsByIdAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default);

    Task<bool> SlugTakenAsync(string slug, long? exceptId, CancellationToken cancellationToken = default);

    /// <summary>The website project showing this system project, if any.</summary>
    Task<long?> LinkedToAsync(long projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<long, SystemProjectFacts>> SystemProjectsAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default);

    /// <summary>Up to <paramref name="take"/> system projects whose title contains <paramref name="search"/>, newest first.</summary>
    Task<IReadOnlyList<SystemProjectFacts>> CandidatesAsync(string? search, int take, CancellationToken cancellationToken = default);

    /// <summary>Whether the file is one the public website shows: the favicon, or a picture of a published project.</summary>
    Task<bool> IsPublicFileAsync(long fileId, CancellationToken cancellationToken = default);

    /// <summary>A list's rows in order. Tracked when <paramref name="track"/>.</summary>
    Task<List<T>> ListAsync<T>(bool track = false, CancellationToken cancellationToken = default) where T : class, IWebsiteListItem;

    Task<T?> FindAsync<T>(long id, CancellationToken cancellationToken = default) where T : class, IWebsiteListItem;

    /// <summary>The position after a list's last row.</summary>
    Task<int> NextPositionAsync<T>(CancellationToken cancellationToken = default) where T : class, IWebsiteListItem;

    void Add<T>(T row) where T : class;

    void Remove<T>(T row) where T : class;
}
