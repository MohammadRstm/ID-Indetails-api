namespace ID_Indetails.api.DTO.Request.WorkEntries;

/// <summary>Times are UTC instants in whole minutes.</summary>
public sealed record SaveWorkEntryRequest
{
    public long ProjectId { get; init; }

    public string? Description { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime EndedAt { get; init; }

    /// <summary>A photo or video the caller uploaded (<c>POST /api/files</c>). Null removes it.</summary>
    public long? AttachmentFileId { get; init; }

    /// <summary>Work outside the project's original scope. Left out, a new entry is within it and an edited one keeps what it had.</summary>
    public bool? OutsideScope { get; init; }

    /// <summary>
    /// Other entries of the caller moved to make room, saved together with this one (the timeline's swap).
    /// Each must still be changeable.
    /// </summary>
    public IReadOnlyList<WorkEntryMoveRequest> Moves { get; init; } = [];

    /// <summary>
    /// Other entries of the caller deleted on the timeline, deleted together with this save, so this one can take
    /// their time. Each must still be changeable.
    /// </summary>
    public IReadOnlyList<long> Deletes { get; init; } = [];
}

/// <summary>An admin's correction of whether an entry is outside the project's original scope.</summary>
public sealed record SetWorkEntryScopeRequest
{
    public bool OutsideScope { get; init; }
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

    /// <summary>Also post to the entry author's team chat, as a card mentioning everyone in it.</summary>
    public bool SendToGroup { get; init; }
}

/// <summary>Narrows only the summary's time-per-project breakdown. Days are company-time-zone days, both inclusive.</summary>
public sealed record WorkSummaryQuery
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public long? ProjectId { get; init; }

    /// <summary>Matches the description or project title; every word must match.</summary>
    public string? Search { get; init; }
}

/// <summary>Narrows only the contributors table and a contributor's work. Days are company-time-zone days, both inclusive.</summary>
public sealed record ProjectWorkQuery
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    /// <summary>Matches the work description; every word must match.</summary>
    public string? Search { get; init; }
}

/// <summary>A range of company days, both included: a day, week, month or year of the Timeline tab.</summary>
public sealed record TimelineEntriesQuery
{
    public DateOnly From { get; init; }

    public DateOnly To { get; init; }
}

public sealed record PeopleWorkQuery
{
    /// <summary>Matches the name, email or position; every word must match.</summary>
    public string? Search { get; init; }

    public long? TeamId { get; init; }
}
