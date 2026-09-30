using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Request.Reports;

/// <summary>Company-time-zone days, both inclusive. Defaults to this quarter so far.</summary>
public sealed record WorkReportQuery
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public long? ProjectId { get; init; }

    public long? UserId { get; init; }

    /// <summary>Only work for this client's projects.</summary>
    public long? ClientId { get; init; }

    /// <summary>Only work on internal projects (no client). Not with <see cref="ClientId"/>.</summary>
    public bool Internal { get; init; }
}

public sealed record OverviewQuery
{
    public OverviewPeriod Period { get; init; } = OverviewPeriod.Quarter;
}
