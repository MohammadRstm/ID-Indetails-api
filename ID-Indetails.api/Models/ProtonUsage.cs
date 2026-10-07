using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// One question asked to Proton and what it cost: the usage ledger behind the monthly budget, and the stored
/// transcript of the turn (question, answer, and which tools ran with which parameters — never the tool results).
/// </summary>
public class ProtonUsage
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public Guid ConversationId { get; set; }

    public DateTime OccurredAt { get; set; }

    public string Model { get; set; } = string.Empty;

    public string PromptVersion { get; set; } = string.Empty;

    public ProtonOutcome Outcome { get; set; }

    public int RoundTrips { get; set; }

    public int ToolCalls { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int CacheReadTokens { get; set; }

    public int CacheWriteTokens { get; set; }

    /// <summary>Estimated provider cost in USD, from the configured prices.</summary>
    public decimal CostUsd { get; set; }

    public string Question { get; set; } = string.Empty;

    public string? Answer { get; set; }

    /// <summary>JSON list of the tool calls: name, parameters with free text replaced by its length, status, duration.</summary>
    public string Tools { get; set; } = "[]";

    public User User { get; set; } = null!;
}
