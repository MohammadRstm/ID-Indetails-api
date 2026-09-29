using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.Models;

namespace ID_Indetails.api.Services.Company.Abstraction;

public interface IOverheadService
{
    Task<OverheadResponse> GetAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OverheadMonthResponse>> MonthlyAsync(int months, CancellationToken cancellationToken = default);

    Task<OverheadLineResponse> CreateAsync(CreateOverheadRequest request, CancellationToken cancellationToken = default);

    Task<OverheadLineResponse> UpdateAsync(long id, UpdateOverheadRequest request, CancellationToken cancellationToken = default);

    Task<OverheadLineResponse> ChangeAmountAsync(long id, ChangeOverheadAmountRequest request, CancellationToken cancellationToken = default);

    /// <summary>Switches a line to another currency from a month (see <see cref="Business.Company.OverheadRules"/>), and returns the line charged from then.</summary>
    Task<OverheadLineResponse> ChangeCurrencyAsync(long id, ChangeOverheadCurrencyRequest request, CancellationToken cancellationToken = default);

    /// <summary>Ends the line at the end of this month. A line that hasn't started yet is removed instead (returns null).</summary>
    Task<OverheadLineResponse?> CancelAsync(long id, CancellationToken cancellationToken = default);

    Task<OverheadLineResponse> ResumeAsync(long id, CancellationToken cancellationToken = default);
}
