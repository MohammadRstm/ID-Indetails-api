using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Execution;

/// <param name="Contributed">What its client payments put into the box, less what was taken out for it.</param>
/// <param name="FromBox">What the box moved to it.</param>
/// <param name="CashProfit">Received + from the box − cost.</param>
public sealed record BoxProjectResponse(
    long Id,
    string Title,
    string? ClientName,
    ProjectStatus Status,
    BoxState BoxState,
    decimal Contributed,
    decimal FromBox,
    decimal CashProfit);

/// <summary>The Execution box: what it holds, who put it there, who needs it and who waits for a decision.</summary>
/// <param name="Contributing">Projects whose payments go into the box, by title.</param>
/// <param name="ContributedTotal">What the contributing projects put in.</param>
/// <param name="InTheRed">Projects that aren't archived and spent more than they got, deepest first.</param>
/// <param name="Pending">Closed by someone who can't see money: remove or keep each.</param>
public sealed record BoxResponse(
    decimal Balance,
    string Currency,
    IReadOnlyList<BoxProjectResponse> Contributing,
    decimal ContributedTotal,
    IReadOnlyList<BoxProjectResponse> InTheRed,
    IReadOnlyList<BoxProjectResponse> Pending);

public sealed record BoxEntryProjectResponse(long Id, string Title);

/// <param name="Amount">USD: positive into the box, negative out of it.</param>
/// <param name="By">Null for what the system wrote.</param>
public sealed record BoxEntryResponse(
    long Id,
    BoxEntryKind Kind,
    decimal Amount,
    BoxEntryProjectResponse Project,
    string? Note,
    DateTime CreatedAt,
    PersonRefResponse? By);
