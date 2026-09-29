using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Requirements;

public sealed record RequirementPersonResponse(long Id, string Name);

public sealed record RequirementResponse
{
    public long Id { get; init; }

    public string Text { get; init; } = string.Empty;

    public DateOnly? Deadline { get; init; }

    /// <summary>Open with a deadline before today (company time zone).</summary>
    public bool IsOverdue { get; init; }

    public bool IsDone { get; init; }

    public DateTime? DoneAt { get; init; }

    public RequirementPersonResponse? DoneBy { get; init; }

    /// <summary>Ticked by finalising the project rather than by a person; reopening the project reopens it.</summary>
    public bool AutoDone { get; init; }

    public IReadOnlyList<RequirementPersonResponse> Assignees { get; init; } = [];

    public DateTime CreatedAt { get; init; }
}

public sealed record MyRequirementProjectResponse(long Id, string Title, string? Icon, string? Tint, ProjectStatus Status);

/// <param name="DaysLeft">Days from today to the deadline; negative once overdue, null without a deadline.</param>
/// <param name="OtherAssignees">Everyone else it is assigned to.</param>
public sealed record MyRequirementResponse(
    long Id,
    string Text,
    DateOnly? Deadline,
    bool IsOverdue,
    int? DaysLeft,
    MyRequirementProjectResponse Project,
    IReadOnlyList<RequirementPersonResponse> OtherAssignees,
    DateTime CreatedAt);

/// <summary>The caller's open requirements, soonest deadline first (undated last).</summary>
/// <param name="Projects">Projects with at least one of them.</param>
/// <param name="DueSoon">Due from today to a week ahead.</param>
/// <param name="Done">Assigned to the caller and already done; not listed.</param>
public sealed record MyRequirementsResponse(
    IReadOnlyList<MyRequirementResponse> Items,
    int Projects,
    int Overdue,
    int DueSoon,
    int Done);

/// <summary>Open requirements first (oldest first), then done ones (most recently done first).</summary>
public sealed record RequirementListResponse(
    IReadOnlyList<RequirementResponse> Items,
    int Total,
    int Done,
    int Overdue,
    int ProgressPct);
