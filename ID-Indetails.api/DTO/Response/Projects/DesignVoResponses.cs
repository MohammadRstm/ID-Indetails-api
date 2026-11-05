using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Projects;

/// <summary>A Design variation order, in USD.</summary>
public sealed record DesignVoResponse
{
    public long Id { get; init; }

    public long ProjectId { get; init; }

    public string Number { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public DesignVoStatus Status { get; init; }

    public DesignVoSplit Split { get; init; }

    public required IReadOnlyList<DesignVoLineResponse> Lines { get; init; }

    /// <summary>Each department's part: chosen by hand, or by the shares once applied. Empty for a project with no departments.</summary>
    public required IReadOnlyList<DesignVoAllocationResponse> Allocations { get; init; }

    /// <summary>Before TVA; below zero for less work.</summary>
    public decimal TotalUsd { get; init; }

    public decimal TvaRate { get; init; }

    public decimal TvaUsd { get; init; }

    public decimal TotalWithTvaUsd { get; init; }

    /// <summary>Hours logged outside the agreed scope since <see cref="OutsideScopeFrom"/>, when the VO shows them.</summary>
    public int? OutsideScopeMinutes { get; init; }

    public DateOnly? OutsideScopeFrom { get; init; }

    public required PersonRefResponse CreatedBy { get; init; }

    public DateTime CreatedAt { get; init; }

    public PersonRefResponse? SentBy { get; init; }

    public DateTime? SentAt { get; init; }

    public PersonRefResponse? AppliedBy { get; init; }

    public DateTime? AppliedAt { get; init; }

    public PersonRefResponse? CancelledBy { get; init; }

    public DateTime? CancelledAt { get; init; }
}

public sealed record DesignVoLineResponse(long Id, string Description, decimal Amount);

public sealed record DesignVoAllocationResponse(long DepartmentId, string Name, decimal Amount);

/// <summary>The hours logged outside the agreed scope since the last applied VO, or the project's start.</summary>
public sealed record OutsideScopeResponse(DateOnly From, int Minutes);
