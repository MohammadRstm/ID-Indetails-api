namespace ID_Indetails.api.DTO.Request.Proton;

public sealed record AskProtonRequest
{
    /// <summary>Leave out to start a new conversation; the reply says which one it is.</summary>
    public Guid? ConversationId { get; init; }

    public string Text { get; init; } = string.Empty;
}

public sealed record TopUpProtonBudgetRequest
{
    /// <summary>Added to this month's budget, in USD.</summary>
    public decimal AmountUsd { get; init; }

    public string? Note { get; init; }
}

/// <summary>Confirming or cancelling a proposed action: only its id (in the route) and the conversation it belongs to.</summary>
public sealed record DecideProtonActionRequest
{
    public Guid ConversationId { get; init; }
}
