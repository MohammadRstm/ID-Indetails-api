namespace ID_Indetails.api.DTO.Request.Meetings;

/// <summary>Times are UTC instants in whole minutes, on the same company-time-zone day.</summary>
public sealed record SaveMeetingRequest
{
    public DateTime StartsAt { get; init; }

    public DateTime EndsAt { get; init; }

    public string? Topic { get; init; }

    public string? Description { get; init; }
}

/// <summary>Days are company-time-zone days, both inclusive; a meeting matches when it overlaps them.</summary>
public sealed record MeetingListQuery
{
    public long? ProjectId { get; init; }

    /// <summary>Only meetings the caller set.</summary>
    public bool Mine { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}
