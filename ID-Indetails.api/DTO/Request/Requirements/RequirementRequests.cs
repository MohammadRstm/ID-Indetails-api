namespace ID_Indetails.api.DTO.Request.Requirements;

/// <summary>Creates a requirement, or replaces its text, deadline and assignees.</summary>
public sealed record SaveRequirementRequest
{
    public string Text { get; init; } = string.Empty;

    public DateOnly? Deadline { get; init; }

    /// <summary>Employees of the project's domain. Empty for nobody.</summary>
    public IReadOnlyList<long> AssigneeIds { get; init; } = [];
}
