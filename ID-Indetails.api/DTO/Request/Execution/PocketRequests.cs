using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Execution;

/// <summary>Part of a site's money moved into the pocket money by a super admin.</summary>
public sealed record PocketContributionRequest
{
    public long ProjectId { get; init; }

    /// <summary>USD.</summary>
    public decimal Amount { get; init; }

    public string? Note { get; init; }
}

/// <summary>A site asking the pocket money for money.</summary>
public sealed record CreatePocketRequestRequest
{
    public long ProjectId { get; init; }

    public string? Note { get; init; }
}

/// <summary>Approving a request with the amount the super admin chose (the suggestion, or another).</summary>
public sealed record ApprovePocketRequestRequest
{
    /// <summary>USD.</summary>
    public decimal Amount { get; init; }

    public string? Note { get; init; }
}

public sealed record DenyPocketRequestRequest
{
    public string? Note { get; init; }
}

/// <summary>Pocket movements: in (contributions) or out (payouts), of a project, between company days, with words.</summary>
public sealed record PocketEntryListQuery
{
    public PocketEntryKind? Kind { get; init; }

    public long? ProjectId { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    /// <summary>Words of the project's title, the note or the name of who made the movement.</summary>
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 30;
}
