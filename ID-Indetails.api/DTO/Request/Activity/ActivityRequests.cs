using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Activity;

public sealed record ActivityListQuery
{
    /// <summary>One filter chip: work, money, billing, projects, people or company. Everything when not given.</summary>
    public ActivityGroup? Group { get; init; }

    /// <summary>Only this domain's events (design or execution), without company-level ones. Every visible domain when not given.</summary>
    public string? Domain { get; init; }

    public long? ProjectId { get; init; }

    /// <summary>The event's client, or its project's client.</summary>
    public long? ClientId { get; init; }

    /// <summary>Only events of internal projects (no client). Not with <see cref="ClientId"/>.</summary>
    public bool Internal { get; init; }

    /// <summary>Who did it.</summary>
    public long? UserId { get; init; }

    /// <summary>Only events on or after this company day.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Only events on or before this company day.</summary>
    public DateOnly? To { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 40;
}
