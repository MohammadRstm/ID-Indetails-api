using ID_Indetails.api.Proton.Tools;

namespace ID_Indetails.api.DTO.Response.Proton;

/// <param name="Refs">The projects, clients and people the reply mentions, for the client to link.</param>
/// <param name="Limited">Proton hit a limit (tool calls, model calls or length) and answered with what it had.</param>
/// <param name="PendingAction">A change Proton proposed in this answer: shown as a card the user confirms or cancels.</param>
public sealed record ProtonReplyResponse(
    Guid ConversationId,
    string Reply,
    IReadOnlyList<ProtonRef> Refs,
    bool Limited,
    ProtonUsageResponse Usage,
    ID_Indetails.api.Proton.Actions.ProtonPendingAction? PendingAction = null);

/// <summary>
/// A person's own Proton allowance this month, in questions — never in money.
/// </summary>
/// <param name="Available">Proton is switched on and neither this allowance nor the company's budget is used up.</param>
/// <param name="CompanyLimitReached">The company's monthly budget is used up, for everyone.</param>
public sealed record ProtonUsageResponse(
    DateOnly Month,
    bool Available,
    int QuestionsAllowed,
    int QuestionsUsed,
    int QuestionsRemaining,
    int UsedPct,
    bool CompanyLimitReached);

public sealed record ProtonTopUpResponse(long Id, decimal AmountUsd, string? Note, string AddedBy, DateTime AddedAt);

public sealed record ProtonUserUsageResponse(long UserId, string Name, int Questions, decimal CostUsd, int Tokens);

/// <summary>Proton's money this month, for super admins: the estimated provider cost against the budget.</summary>
public sealed record ProtonBudgetResponse(
    DateOnly Month,
    string Model,
    decimal MonthlyBudgetUsd,
    decimal TopUpsUsd,
    decimal BudgetUsd,
    decimal SpentUsd,
    decimal RemainingUsd,
    int UsedPct,
    int Questions,
    IReadOnlyList<ProtonTopUpResponse> TopUps,
    IReadOnlyList<ProtonUserUsageResponse> ByUser);

/// <summary>What became of a confirmed or cancelled action. A failure is an error response instead, with the service's own code.</summary>
public sealed record ProtonActionOutcomeResponse(Guid ActionId, ID_Indetails.api.Enums.ProtonActionStatus Status, string Message, IReadOnlyList<ProtonRef> Refs);
