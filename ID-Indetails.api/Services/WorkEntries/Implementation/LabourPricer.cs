using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

/// <summary>
/// Prices work entries with the shared finance calculator, so a person's labour cost here always matches
/// the project cost: each hour at the hourly cost in force that day, plus the paid daily breaks, converted
/// at that day's exchange rate.
/// </summary>
internal sealed class LabourPricer
{
    private readonly IReadOnlyList<ProjectFinance.PayRate> _payRates;
    private readonly ILookup<(long UserId, long ProjectId, DateOnly Day), ProjectFinance.CostItem> _breaks;
    private readonly ExchangeRateTable _exchangeRates;
    private readonly IClock _clock;

    private LabourPricer(
        IReadOnlyList<ProjectFinance.PayRate> payRates,
        ILookup<(long, long, DateOnly), ProjectFinance.CostItem> breaks,
        ExchangeRateTable exchangeRates,
        IClock clock)
    {
        _payRates = payRates;
        _breaks = breaks;
        _exchangeRates = exchangeRates;
        _clock = clock;
    }

    /// <param name="rows">Every entry that will be priced, so the paid breaks of their days are loaded once.</param>
    public static async Task<LabourPricer> LoadAsync(
        IProjectFinanceRepository sources,
        IProjectFinanceService finance,
        IReadOnlyCollection<WorkRow> rows,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var payRates = await finance.PayRatesAsync(rows.Select(r => r.UserId).Distinct().ToList(), cancellationToken);
        var breaks = await finance.PaidBreaksAsync(
            rows.Select(r => new WorkEntryRow(r.ProjectId, r.UserId, r.StartedAt, r.Minutes)).ToList(), payRates, cancellationToken);

        return new(
            payRates,
            breaks.ToLookup(b => (b.UserId, b.ProjectId, b.Cost.Day), b => b.Cost),
            new ExchangeRateTable(await sources.ExchangeRatesAsync(cancellationToken)),
            clock);
    }

    /// <summary>The entries' labour cost, with the paid breaks of the people, projects and days they cover.</summary>
    public LabourCostResponse Price(IEnumerable<WorkRow> rows)
    {
        var list = rows.ToList();
        var (items, unpriced) = ProjectFinance.PriceLabour(
            list.Select(r => new ProjectFinance.WorkedTime(r.UserId, _clock.ToCompanyDate(r.StartedAt), r.Minutes)),
            _payRates);
        var breaks = list
            .Select(r => (r.UserId, r.ProjectId, _clock.ToCompanyDate(r.StartedAt)))
            .Distinct()
            .SelectMany(key => _breaks[key]);
        var costs = ProjectFinance.ToUsd(items.Concat(breaks), _exchangeRates);

        return new LabourCostResponse(
            costs.Labour,
            costs.Unconverted.Select(u => new CurrencyAmountResponse(u.Currency, u.Amount)).ToList(),
            unpriced);
    }
}
