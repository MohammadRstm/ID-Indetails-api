using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Projects;

/// <summary>A Design variation order: added to the price at once (<see cref="Apply"/>), or sent to the client to confirm.</summary>
public sealed record CreateDesignVoRequest
{
    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<DesignVoLineRequest> Lines { get; init; } = [];

    public DesignVoSplit Split { get; init; } = DesignVoSplit.Shares;

    /// <summary>With <c>departments</c>: each department's amount, adding up to the total.</summary>
    public IReadOnlyList<DesignVoAllocationRequest> Allocations { get; init; } = [];

    /// <summary>Show on it the hours logged outside the agreed scope since the last applied VO (or the project's start).</summary>
    public bool ShowOutsideScope { get; init; }

    /// <summary>True: part of the price now. False: pending until the client confirms it.</summary>
    public bool Apply { get; init; }
}

/// <param name="Amount">Before TVA, in USD; below zero for work taken out.</param>
public sealed record DesignVoLineRequest(string Description, decimal Amount);

public sealed record DesignVoAllocationRequest(long DepartmentId, decimal Amount);
