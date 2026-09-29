using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Dashboard;

public sealed record StatusTotalResponse(ProjectStatus Status, int Count);

public sealed record RunningProjectResponse(long ProjectId, string Title, DateOnly StartDate, int Days);

/// <summary>The cards of a domain dashboard that hold no money or hours: the key figures and projects by status.</summary>
/// <param name="Clients">Clients that aren't archived.</param>
/// <param name="LongestRunning">Active and on-hold projects that started earliest (3 at most).</param>
public sealed record DashboardResponse(
    string Domain,
    int ActiveProjects,
    int Clients,
    IReadOnlyList<StatusTotalResponse> ProjectsByStatus,
    IReadOnlyList<RunningProjectResponse> LongestRunning);
