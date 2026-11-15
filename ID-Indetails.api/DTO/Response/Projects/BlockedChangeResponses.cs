using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Projects;

/// <summary>A change the profit floor refused, and where it stands.</summary>
/// <param name="Summary">What it was, in words ("Expense: Tiles, 1,200 USD"), for whoever sees the money.</param>
/// <param name="ProfitNow">The project's profit today: what forcing would cut into.</param>
/// <param name="Person">A held pay change: whose pay it is.</param>
/// <param name="Projects">A held pay change: every project it takes to zero profit, hardest hit first.</param>
public sealed record BlockedChangeResponse(
    long Id,
    long ProjectId,
    string ProjectTitle,
    string Domain,
    BlockedChangeKind Kind,
    string Summary,
    BlockedChangeStatus Status,
    PersonRefResponse RequestedBy,
    DateTime RequestedAt,
    PersonRefResponse? DecidedBy,
    DateTime? DecidedAt,
    decimal ProfitNow,
    PersonRefResponse? Person = null,
    IReadOnlyList<BlockedProjectResponse>? Projects = null);

/// <summary>A project a held pay change reaches: its profit today, and what forcing the change now would leave (null once decided).</summary>
public sealed record BlockedProjectResponse(long ProjectId, string Title, decimal ProfitNow, decimal? ProfitIfForced);
