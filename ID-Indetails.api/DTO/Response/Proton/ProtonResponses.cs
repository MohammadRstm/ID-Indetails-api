using ID_Indetails.api.Proton.Tools;

namespace ID_Indetails.api.DTO.Response.Proton;

/// <param name="Refs">The projects, clients and people the reply mentions, for the client to link.</param>
/// <param name="Limited">Proton hit a limit (tool calls, model calls or length) and answered with what it had.</param>
/// <param name="PendingAction">A change Proton proposed in this answer: shown as a card the user confirms or cancels.</param>
/// <param name="Sources">What the analytical figures in the reply rest on, shown under it as "Based on".</param>
/// <param name="UiCommands">What the app opens or refreshes once the reply is shown: at most one page, or a refresh.</param>
/// <param name="Choices">Options to pick from with a click instead of typing; the pick is checked against these.</param>
/// <param name="QuickReplies">Answers to Proton's question as buttons, in the app's words; a click is checked against these.</param>
/// <param name="Suggestions">Replies the user might send next, as buttons; a tap sends only its place in this list.</param>
/// <param name="RolledOver">The chat had grown too long, so Proton carried on from a short recap of it.</param>
public sealed record ProtonReplyResponse(
    Guid ConversationId,
    string Reply,
    IReadOnlyList<ProtonRef> Refs,
    bool Limited,
    ProtonUsageResponse Usage,
    IReadOnlyList<ProtonSourceResponse> Sources,
    IReadOnlyList<ID_Indetails.api.Proton.Ui.ProtonUiCommand> UiCommands,
    IReadOnlyList<ID_Indetails.api.Proton.Ui.ProtonChoice> Choices,
    IReadOnlyList<ID_Indetails.api.Proton.Ui.ProtonQuickReplyOption> QuickReplies,
    IReadOnlyList<string> Suggestions,
    ID_Indetails.api.Proton.Actions.ProtonPendingAction? PendingAction = null,
    bool RolledOver = false);

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
    IReadOnlyList<ProtonUserUsageResponse> ByUser,
    IReadOnlyList<ProtonDomainUsageResponse> ByDomain,
    IReadOnlyList<ProtonToolUsageResponse> ByTool,
    IReadOnlyList<ProtonDayUsageResponse> ByDay,
    IReadOnlyList<ProtonConversationUsageResponse> TopConversations);

/// <param name="Domain">The people's domain as it is now; null for company-wide roles.</param>
public sealed record ProtonDomainUsageResponse(string? Domain, int Questions, decimal CostUsd);

/// <param name="QuestionCostUsd">What the questions that used the tool cost in all; a tool's own share can't be told apart.</param>
public sealed record ProtonToolUsageResponse(string Tool, int Calls, int AverageMs, int Questions, decimal QuestionCostUsd);

public sealed record ProtonDayUsageResponse(DateOnly Day, int Questions, decimal CostUsd);

public sealed record ProtonConversationUsageResponse(Guid ConversationId, long UserId, string Name, int Questions, int ToolCalls, decimal CostUsd);

/// <summary>What became of a confirmed or cancelled action. A failure is an error response instead, with the service's own code.</summary>
/// <param name="UiCommands">What the app opens now: the result, after a successful change whose card said it would.</param>
public sealed record ProtonActionOutcomeResponse(
    Guid ActionId,
    ID_Indetails.api.Enums.ProtonActionStatus Status,
    string Message,
    IReadOnlyList<ProtonRef> Refs,
    IReadOnlyList<ID_Indetails.api.Proton.Ui.ProtonUiCommand> UiCommands);

/// <summary>One analysis behind a reply: what was looked at, over which days, and what it left out. No data, no internals.</summary>
/// <param name="Period">The days, in words ("1–7 Oct 2026").</param>
/// <param name="ComparedWith">The days compared with, in words, when there was a comparison.</param>
/// <param name="Partial">The period is still running, so its figures are "so far".</param>
/// <param name="Omitted">What the analysis left out: what the user can't see, or data the system doesn't hold.</param>
public sealed record ProtonSourceResponse(string Label, string? Period, string? ComparedWith, bool Partial, string? Scope, IReadOnlyList<string> Omitted);
