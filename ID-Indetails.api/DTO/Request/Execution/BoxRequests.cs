using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Execution;

/// <summary>Money moved from the Execution box to a project in the red.</summary>
public sealed record BoxTransferRequest
{
    public long ProjectId { get; init; }

    /// <summary>USD.</summary>
    public decimal Amount { get; init; }

    public string? Note { get; init; }
}

/// <summary>What happens to a closed project's money in the box: <c>remove</c> or <c>keep</c>.</summary>
public sealed record BoxDecisionRequest
{
    public BoxChoice Decision { get; init; }
}

public sealed record BoxEntryListQuery
{
    public long? ProjectId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}
