using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Teams.Abstraction;

public interface ITeamRepository
{
    Task<Team?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
}
