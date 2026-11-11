namespace ID_Indetails.api.DTO.Request.Projects;

/// <summary>A new version of a project's cost budget, why it is set, and from when.</summary>
public sealed record SetProjectBudgetRequest
{
    public string Reason { get; init; } = string.Empty;

    /// <summary>The company day it counts from; left out, today. Never a day still to come.</summary>
    public DateOnly? EffectiveFrom { get; init; }

    public IReadOnlyList<ProjectBudgetLineRequest> Lines { get; init; } = [];
}

/// <summary>A department (Design) or a category, never both, and what it may cost in USD net of TVA.</summary>
public sealed record ProjectBudgetLineRequest(long? TeamId, string? Category, decimal Amount);
