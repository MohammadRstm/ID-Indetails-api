namespace ID_Indetails.api.DTO.Response.Teams;

/// <param name="MemberCount">People in the team who are not deactivated.</param>
public sealed record TeamResponse(long Id, string Name, string Domain, int MemberCount);
