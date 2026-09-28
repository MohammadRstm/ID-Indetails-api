namespace ID_Indetails.api.DTO.Request.WorkEntries;

/// <summary>Times are UTC instants in whole minutes.</summary>
public sealed record SaveWorkEntryRequest
{
    public long ProjectId { get; init; }

    public string? Description { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime EndedAt { get; init; }

    /// <summary>
    /// Other entries of the caller moved to make room, saved together with this one (the timeline's swap).
    /// Each must still be changeable.
    /// </summary>
    public IReadOnlyList<WorkEntryMoveRequest> Moves { get; init; } = [];
}

public sealed record WorkEntryMoveRequest
{
    public long Id { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime EndedAt { get; init; }
}

/// <summary>Everyone's Design entries. Days are company-time-zone days, both inclusive, on the start time.</summary>
public sealed record WorkEntryListQuery
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public long? ProjectId { get; init; }

    public long? UserId { get; init; }

    public long? ClientId { get; init; }

    /// <summary>Matches the description or project title; every word must match.</summary>
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

/// <summary>The caller's own entries. Days are company-time-zone days, both inclusive, on the start time.</summary>
public sealed record MyWorkEntryListQuery
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public long? ProjectId { get; init; }

    /// <summary>Matches the description or project title; every word must match.</summary>
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

public sealed record AddWorkEntryCommentRequest
{
    public string Body { get; init; } = string.Empty;

    /// <summary>Also post to the author's team group chat. Refused until group chats exist.</summary>
    public bool SendToGroup { get; init; }
}
