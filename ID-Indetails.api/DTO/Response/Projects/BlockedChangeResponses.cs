using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.Enums;

namespace ID_Indetails.api.DTO.Response.Projects;

/// <summary>A change the profit floor refused, and where it stands.</summary>
/// <param name="Summary">What it was, in words ("Expense: Tiles, 1,200 USD"), for whoever sees the money.</param>
/// <param name="ProfitNow">The project's profit today, counting materials not sent yet: what forcing would cut into.</param>
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
    decimal ProfitNow);
