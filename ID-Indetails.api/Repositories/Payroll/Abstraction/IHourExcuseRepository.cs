using ID_Indetails.api.Models;

namespace ID_Indetails.api.Repositories.Payroll.Abstraction;

public interface IHourExcuseRepository
{
    /// <summary>Tracked, with its days.</summary>
    Task<HourExcuse?> GetForUpdateAsync(long id, CancellationToken cancellationToken = default);

    void Add(HourExcuse excuse);
}
