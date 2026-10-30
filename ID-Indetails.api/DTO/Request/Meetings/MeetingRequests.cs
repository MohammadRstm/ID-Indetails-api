namespace ID_Indetails.api.DTO.Request.Meetings;

/// <summary>Times are UTC instants in whole minutes, on the same company-time-zone day.</summary>
public sealed record SaveMeetingRequest
{
    public DateTime StartsAt { get; init; }

    public DateTime EndsAt { get; init; }

    public string? Topic { get; init; }

    public string? Description { get; init; }

    /// <summary>A general meeting's people, replacing them; null leaves them as they are. A project meeting takes none.</summary>
    public IReadOnlyList<long>? AttendeeIds { get; init; }
}

/// <summary>A meeting about no project. Times are UTC instants in whole minutes, on the same company-time-zone day.</summary>
public sealed record SaveGeneralMeetingRequest
{
    public DateTime StartsAt { get; init; }

    public DateTime EndsAt { get; init; }

    public string? Topic { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// The domain whose meeting managers see it. Left out: the whole company for someone who sees it, otherwise their
    /// own domain.
    /// </summary>
    public string? Domain { get; init; }

    /// <summary>Who it is with: at least one, besides whoever sets it.</summary>
    public IReadOnlyList<long> AttendeeIds { get; init; } = [];
}

/// <summary>Days are company-time-zone days, both inclusive; a meeting matches when it overlaps them.</summary>
public sealed record MeetingListQuery
{
    /// <summary>Only this domain's meetings (design or execution). Every visible domain when not given.</summary>
    public string? Domain { get; init; }

    public long? ProjectId { get; init; }

    /// <summary>Only meetings the caller set.</summary>
    public bool Mine { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}
