namespace ID_Indetails.api.DTO.Request.Projects;

/// <summary>The departments working on a Design project. Any change puts the shares back to equal.</summary>
public sealed record SetProjectDepartmentsRequest
{
    public IReadOnlyList<long> DepartmentIds { get; init; } = [];
}

/// <summary>Each department's share of a Design project's price, adding up to 1, and why they change.</summary>
public sealed record SetDepartmentSharesRequest
{
    public IReadOnlyList<DepartmentShareRequest> Shares { get; init; } = [];

    public string Reason { get; init; } = string.Empty;
}

/// <param name="Share">A fraction, 0.25 is 25%, to four places.</param>
public sealed record DepartmentShareRequest(long DepartmentId, decimal Share);
