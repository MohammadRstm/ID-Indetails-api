using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.TimeOff;

/// <summary>An employee asking for whole days off.</summary>
public sealed record AskTimeOffRequest
{
    public DateOnly FirstDay { get; init; }

    public DateOnly LastDay { get; init; }

    /// <summary>Why: required, and seen only by the super admins.</summary>
    public string Reason { get; init; } = string.Empty;
}

public sealed record ApproveTimeOffRequest
{
    /// <summary>Whether the days off are paid.</summary>
    public bool IsPaid { get; init; }

    public string? Note { get; init; }
}

/// <summary>Declining a request, or rejecting approved time off: the note is optional.</summary>
public sealed record TimeOffNoteRequest
{
    public string? Note { get; init; }
}

public sealed record TimeOffListQuery
{
    public TimeOffView View { get; init; } = TimeOffView.Pending;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

/// <summary>Who is off on any day from <see cref="From"/> to <see cref="To"/>; today when both are left out.</summary>
public sealed record AwayQuery
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}
