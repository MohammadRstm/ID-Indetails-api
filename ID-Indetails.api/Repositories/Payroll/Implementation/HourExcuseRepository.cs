using ID_Indetails.api.Data;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Repositories.Payroll.Implementation;

public sealed class HourExcuseRepository(IdIndetailsDbContext db) : IHourExcuseRepository
{
    public Task<HourExcuse?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default)
        => db.HourExcuses.Include(x => x.Days).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public void Add(HourExcuse excuse) => db.HourExcuses.Add(excuse);
}
