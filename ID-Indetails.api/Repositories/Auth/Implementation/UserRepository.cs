using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Auth.Implementation;

public sealed class UserRepository(IdIndetailsDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => db.Users.Include(u => u.Team).SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => db.Users.Include(u => u.Team).SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => db.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

    public void Add(User user) => db.Users.Add(user);

    public void AddPayHistory(UserPayHistory payHistory) => db.UserPayHistory.Add(payHistory);
}
