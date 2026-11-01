using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Search;

public sealed record SearchProjectResponse(long Id, string Domain, string Title, string? Icon, string? Tint, ProjectStatus Status);

/// <param name="Secondary">The person's name when the display name is the organisation, otherwise the label.</param>
public sealed record SearchClientResponse(long Id, string Domain, string DisplayName, string? Secondary);

public sealed record SearchPersonResponse(long Id, string? Domain, string Name, string? Position);

/// <summary>The command palette's results: projects, then clients, then people, 12 in all.</summary>
public sealed record SearchResponse(
    IReadOnlyList<SearchProjectResponse> Projects,
    IReadOnlyList<SearchClientResponse> Clients,
    IReadOnlyList<SearchPersonResponse> People);

/// <summary>
/// The numbers beside Projects, Clients and Employees in the sidebar: active projects, clients that aren't archived and
/// active people, each what its list's total would be. Null where the caller may not list them.
/// </summary>
public sealed record NavCountsResponse(int? Projects, int? Clients, int? People);
