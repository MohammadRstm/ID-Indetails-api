using ID_Indetails.api.Data;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Auth.Implementation;

public sealed class UserRepository(IdIndetailsDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => db.Users.Include(u => u.Team).SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<User?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => (await db.Users
            .FromSql($"SELECT * FROM `users` WHERE `id` = {id} FOR UPDATE")
            .ToListAsync(cancellationToken))
            .SingleOrDefault();

    public Task<UserRoleSnapshot?> GetRoleSnapshotAsync(long id, CancellationToken cancellationToken = default)
        => db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserRoleSnapshot(u.Id, u.RoleCode, u.DomainCode, u.Status, u.SessionsRevokedAt, u.SessionsValidFrom))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => db.Users.Include(u => u.Team).SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => db.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

    public async Task<IReadOnlyList<long>> ActiveIdsInDomainAsync(string domain, CancellationToken cancellationToken = default)
        => await db.Users
            .Where(u => u.DomainCode == domain && u.Status == UserStatus.Active)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UserDomain>> ActiveDomainsAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.ToList();
        return await db.Users.AsNoTracking()
            .Where(u => list.Contains(u.Id) && u.Status == UserStatus.Active)
            .Select(u => new UserDomain(u.Id, u.DomainCode))
            .ToListAsync(cancellationToken);
    }

    public Task SetLastSeenAsync(long id, DateTime at, CancellationToken cancellationToken = default)
        => db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAt, at), cancellationToken);

    public void Add(User user) => db.Users.Add(user);

    public void AddPayHistory(UserPayHistory payHistory) => db.UserPayHistory.Add(payHistory);
}
