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

/// <summary>Open requirements first (oldest first), then done ones (most recently done first).</summary>
public sealed record RequirementListResponse(
    IReadOnlyList<RequirementResponse> Items,
    int Total,
    int Done,
    int Overdue,
    int ProgressPct);
