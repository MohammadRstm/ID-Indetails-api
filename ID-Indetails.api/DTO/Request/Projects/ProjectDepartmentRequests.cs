namespace ID_Indetails.api.DTO.Request.Projects;

/// <summary>The departments working on a Design project. Any change puts the shares back to equal.</summary>
public sealed record SetProjectDepartmentsRequest
{
    public IReadOnlyList<long> DepartmentIds { get; init; } = [];
}

/// <summary>Each department's share of a Design project's price, adding up to 1, and why they change if it's said.</summary>
public sealed record SetDepartmentSharesRequest
{
    public IReadOnlyList<DepartmentShareRequest> Shares { get; init; } = [];

    public string? Reason { get; init; }
}

/// <param name="Share">A fraction, 0.25 is 25%, to four places.</param>
public sealed record DepartmentShareRequest(long DepartmentId, decimal Share);

/// <summary>The hours planned for some of a Design project's departments; the departments left out keep theirs.</summary>
public sealed record SetDepartmentPlanRequest
{
    public IReadOnlyList<DepartmentPlanRequest> Departments { get; init; } = [];
}

/// <param name="PlannedHours">Above 0 and at most 100,000, to two places; null clears the department's plan.</param>
public sealed record DepartmentPlanRequest(long TeamId, decimal? PlannedHours);
