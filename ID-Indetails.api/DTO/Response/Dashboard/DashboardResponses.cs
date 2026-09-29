using ID_Indetails.api.DTO.Response.Common;
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

public sealed record DeadlineProjectResponse(long Id, string Title, string? Icon, string? Tint);

/// <param name="DaysLeft">Days from today to the deadline; negative once overdue.</param>
public sealed record DeadlineResponse(
    long RequirementId,
    string Text,
    DateOnly Deadline,
    bool IsOverdue,
    int DaysLeft,
    DeadlineProjectResponse Project,
    IReadOnlyList<PersonRefResponse> Assignees);

/// <summary>Open requirements of active and on-hold projects due in the next 14 days, overdue ones included, soonest first.</summary>
/// <param name="Total">How many there are; <paramref name="Items"/> holds the first 8.</param>
/// <param name="Overdue">How many of them are overdue.</param>
public sealed record UpcomingDeadlinesResponse(int Total, int Overdue, IReadOnlyList<DeadlineResponse> Items);
