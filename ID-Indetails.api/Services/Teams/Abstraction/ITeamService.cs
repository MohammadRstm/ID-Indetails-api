using ID_Indetails.api.DTO.Request.Teams;
using ID_Indetails.api.DTO.Response.Teams;

namespace ID_Indetails.api.Services.Teams.Abstraction;

public interface ITeamService
{
    /// <summary>Teams of the domains the caller can see.</summary>
    Task<IReadOnlyList<TeamResponse>> ListAsync(CancellationToken cancellationToken = default);

    Task<TeamResponse> CreateAsync(CreateTeamRequest request, CancellationToken cancellationToken = default);

    Task<TeamResponse> RenameAsync(long teamId, RenameTeamRequest request, CancellationToken cancellationToken = default);

    /// <summary>Only a team nobody is in (and with no group chat) can be deleted.</summary>
    Task DeleteAsync(long teamId, CancellationToken cancellationToken = default);
}
