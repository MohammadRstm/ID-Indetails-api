using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Execution;

public sealed record PocketProjectResponse(long Id, string Title);

/// <summary>A site's request for pocket money.</summary>
/// <param name="SuggestedAmount">What the system suggested when it was made.</param>
/// <param name="SuggestedNow">For a pending request: what brings the site back to a 10% margin today. Null once decided.</param>
/// <param name="CashProfit">For a pending request: the site's cash profit today (received − cost, pocket money counted).</param>
/// <param name="RequestedBy">Null when the system raised it.</param>
public sealed record PocketRequestResponse(
    long Id,
    PocketProjectResponse Project,
    PocketRequestStatus Status,
    decimal SuggestedAmount,
    decimal? SuggestedNow,
    decimal? CashProfit,
    decimal? ApprovedAmount,
    string? Note,
    DateTime RequestedAt,
    PersonRefResponse? RequestedBy,
    DateTime? DecidedAt,
    PersonRefResponse? DecidedBy,
    string? DecisionNote);

/// <summary>Execution's pocket money: what it holds and the requests waiting for a decision.</summary>
/// <param name="ContributedTotal">Everything moved in so far.</param>
/// <param name="PaidOutTotal">Everything paid out so far.</param>
public sealed record PocketResponse(
    decimal Balance,
    string Currency,
    decimal ContributedTotal,
    decimal PaidOutTotal,
    IReadOnlyList<PocketRequestResponse> Pending);

/// <param name="Amount">USD from the pocket's side: positive in (a contribution), negative out (a payout).</param>
public sealed record PocketEntryResponse(
    long Id,
    PocketEntryKind Kind,
    decimal Amount,
    PocketProjectResponse Project,
    long? RequestId,
    string? Note,
    DateTime CreatedAt,
    PersonRefResponse By);
