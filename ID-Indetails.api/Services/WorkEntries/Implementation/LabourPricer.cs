using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

/// <summary>
/// Prices work entries with the shared finance calculator, so a person's labour cost here always matches
/// the project cost: each hour at the hourly cost in force that day, converted at that day's exchange rate.
/// </summary>
internal sealed class LabourPricer
{
    private readonly IReadOnlyList<ProjectFinance.PayRate> _payRates;
    private readonly ExchangeRateTable _exchangeRates;
    private readonly IClock _clock;

    private LabourPricer(IReadOnlyList<ProjectFinance.PayRate> payRates, ExchangeRateTable exchangeRates, IClock clock)
    {
        _payRates = payRates;
        _exchangeRates = exchangeRates;
        _clock = clock;
    }

    public static async Task<LabourPricer> LoadAsync(
        IProjectFinanceRepository sources,
        IReadOnlyCollection<long> userIds,
        IClock clock,
        CancellationToken cancellationToken)
        => new(
            await sources.PayRatesAsync(userIds, cancellationToken),
            new ExchangeRateTable(await sources.ExchangeRatesAsync(cancellationToken)),
            clock);

    public LabourCostResponse Price(IEnumerable<WorkRow> rows)
    {
        var (items, unpriced) = ProjectFinance.PriceLabour(
            rows.Select(r => new ProjectFinance.WorkedTime(r.UserId, _clock.ToCompanyDate(r.StartedAt), r.Minutes)),
            _payRates);
        var costs = ProjectFinance.ToUsd(items, _exchangeRates);

        return new LabourCostResponse(
            costs.Labour,
            costs.Unconverted.Select(u => new CurrencyAmountResponse(u.Currency, u.Amount)).ToList(),
            unpriced);
    }
}
