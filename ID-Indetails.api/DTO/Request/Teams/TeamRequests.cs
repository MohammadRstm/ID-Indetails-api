namespace ID_Indetails.api.DTO.Request.Teams;

public sealed record CreateTeamRequest
{
    public string Name { get; init; } = string.Empty;

    /// <summary>May be empty when the caller works in only one domain.</summary>
    public string? DomainCode { get; init; }
}

public sealed record RenameTeamRequest
{
    public string Name { get; init; } = string.Empty;
}
