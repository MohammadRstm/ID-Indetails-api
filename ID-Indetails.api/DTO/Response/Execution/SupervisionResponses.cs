using ID_Indetails.api.DTO.Response.Common;

namespace ID_Indetails.api.DTO.Response.Execution;

/// <summary>Some of a site's supervision fee the company took out of the project account.</summary>
/// <param name="CreatedBy">Who recorded it.</param>
public sealed record SupervisionWithdrawalResponse(
    long Id,
    decimal AmountUsd,
    DateOnly TakenOn,
    string? Note,
    PersonRefResponse? CreatedBy,
    DateTime CreatedAt);
