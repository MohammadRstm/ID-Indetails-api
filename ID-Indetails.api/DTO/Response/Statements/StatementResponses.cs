using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Statements;

public sealed record StatementTypeResponse(string Key, string Label, string? Quantity, int Deliveries, decimal Earlier, decimal New, decimal Total);

/// <param name="Count">Earlier material types left unlisted, shown as one line.</param>
public sealed record StatementHiddenTypesResponse(int Count, decimal Total);

public sealed record StatementDeliveryResponse(long Id, DeliveryChannel Channel, string? Recipient, DateTime SentAt, PersonRefResponse? SentBy, string? Note);

/// <summary>A statement as the client reads it. A preview has no id and a provisional number.</summary>
public sealed record StatementResponse
{
    public long? Id { get; init; }

    public string Number { get; init; } = string.Empty;

    public bool IsPreview { get; init; }

    public long ProjectId { get; init; }

    public string ProjectTitle { get; init; } = string.Empty;

    /// <summary>The site's location, when the project has one.</summary>
    public string? ProjectLocation { get; init; }

    public ProjectClientResponse Client { get; init; } = null!;

    public StatementMode Mode { get; init; }

    public bool IncludesEarlier { get; init; }

    /// <summary>Day of the previous statement, for "supplied since …". Null on the first one.</summary>
    public DateOnly? Since { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateOnly Date { get; init; }

    public PersonRefResponse? CreatedBy { get; init; }

    /// <summary>Listed material types, by name. Empty for a total-only statement.</summary>
    public IReadOnlyList<StatementTypeResponse> Types { get; init; } = [];

    public StatementHiddenTypesResponse? HiddenTypes { get; init; }

    public decimal EarlierTotal { get; init; }

    public decimal NewTotal { get; init; }

    public decimal Total { get; init; }

    /// <summary>Materials on the statement.</summary>
    public int Count { get; init; }

    public string Currency { get; init; } = "USD";

    /// <summary>Payments received when the statement was made.</summary>
    public decimal Received { get; init; }

    /// <summary>The agreed amount (material client prices plus the fee) when it was made.</summary>
    public decimal Agreed { get; init; }

    public decimal Remaining { get; init; }

    public decimal PaidAhead { get; init; }

    public IReadOnlyList<StatementDeliveryResponse> Deliveries { get; init; } = [];
}

public sealed record StatementSummaryResponse(
    long Id,
    string Number,
    DateTime CreatedAt,
    StatementMode Mode,
    int Count,
    decimal Total,
    int DeliveryCount,
    StatementDeliveryResponse? LastDelivery);
