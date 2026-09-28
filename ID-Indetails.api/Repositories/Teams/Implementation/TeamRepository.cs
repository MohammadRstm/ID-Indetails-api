using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Teams.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Teams.Implementation;

public sealed class TeamRepository(IdIndetailsDbContext db) : ITeamRepository
{
    public Task<Team?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => db.Teams.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
}
