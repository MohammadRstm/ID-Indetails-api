using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Teams.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Teams.Implementation;

public sealed class TeamRepository(IdIndetailsDbContext db) : ITeamRepository
{
    public Task<Team?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => db.Teams.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Team?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.Teams.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<(Team Team, int MemberCount)>> ListAsync(
        IReadOnlyList<string> domains,
        CancellationToken cancellationToken = default)
    {
        var codes = domains.ToList();
        var rows = await db.Teams
            .AsNoTracking()
            .Where(t => codes.Contains(t.DomainCode))
            .OrderBy(t => t.DomainCode)
            .ThenBy(t => t.Name)
            .Select(t => new
            {
                Team = t,
                Members = db.Users.Count(u => u.TeamId == t.Id && u.Status != UserStatus.Deactivated),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.Team, r.Members)).ToList();
    }

    public Task<bool> NameExistsAsync(string domainCode, string name, long? exceptTeamId, CancellationToken cancellationToken = default)
        => db.Teams.AnyAsync(
            t => t.DomainCode == domainCode && t.Name == name && (exceptTeamId == null || t.Id != exceptTeamId),
            cancellationToken);

    public Task<bool> HasPeopleAsync(long teamId, CancellationToken cancellationToken = default)
        => db.Users.AnyAsync(u => u.TeamId == teamId, cancellationToken);

    public Task<bool> HasChatAsync(long teamId, CancellationToken cancellationToken = default)
        => db.Conversations.AnyAsync(c => c.TeamId == teamId, cancellationToken);

    public void Add(Team team) => db.Teams.Add(team);

    public void Remove(Team team) => db.Teams.Remove(team);
}
