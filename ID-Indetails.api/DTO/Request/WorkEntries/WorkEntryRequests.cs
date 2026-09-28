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
