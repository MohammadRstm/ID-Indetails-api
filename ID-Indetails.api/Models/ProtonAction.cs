using ID_Indetails.api.Enums;

namespace ID_Indetails.api.Models;

/// <summary>
/// A change Proton proposed and what became of it: the record the user confirms (exactly these arguments, nothing
/// else), the guard against running it twice, and the audit of who had Proton change what.
/// </summary>
public class ProtonAction
{
    public long Id { get; set; }

    /// <summary>What the client and URLs use, so ids can't be guessed in sequence.</summary>
    public Guid PublicId { get; set; }

    public long UserId { get; set; }

    /// <summary>The proposer's role and home domain when proposing.</summary>
    public string Role { get; set; } = string.Empty;

    public string? DomainCode { get; set; }

    public Guid ConversationId { get; set; }

    public string Tool { get; set; } = string.Empty;

    public ProtonActionRisk Risk { get; set; }

    /// <summary>JSON: the exact validated request the confirmation sends to the existing service.</summary>
    public string Arguments { get; set; } = "{}";

    /// <summary>JSON: the title and before/after lines the user was shown.</summary>
    public string Preview { get; set; } = "{}";

    public string? TargetType { get; set; }

    public long? TargetId { get; set; }

    public ProtonActionStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>When it was confirmed, cancelled, superseded or found expired.</summary>
    public DateTime? DecidedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public string? ResultType { get; set; }

    public long? ResultId { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string Model { get; set; } = string.Empty;

    public string PromptVersion { get; set; } = string.Empty;

    public User User { get; set; } = null!;
}
