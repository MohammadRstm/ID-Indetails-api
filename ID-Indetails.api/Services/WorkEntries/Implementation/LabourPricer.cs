using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Repositories.Finance.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;

namespace ID_Indetails.api.Services.WorkEntries.Implementation;

/// <summary>
/// Prices work entries with the shared finance calculator, so a person's labour cost here always matches
/// the project cost: each hour at the hourly cost in force that day (the part paid as overtime at the overtime rate
/// × the multiplier), plus the paid daily breaks, converted at that day's exchange rate.
/// </summary>
internal sealed class LabourPricer
{
    private readonly IReadOnlyList<ProjectFinance.PayRate> _payRates;
    private readonly IReadOnlyDictionary<long, PaidOvertime> _overtime;
    private readonly ILookup<(long UserId, long ProjectId, DateOnly Day), ProjectFinance.CostItem> _breaks;
    private readonly ExchangeRateTable _exchangeRates;
    private readonly IClock _clock;

    private LabourPricer(
        IReadOnlyList<ProjectFinance.PayRate> payRates,
        IReadOnlyDictionary<long, PaidOvertime> overtime,
        ILookup<(long, long, DateOnly), ProjectFinance.CostItem> breaks,
        ExchangeRateTable exchangeRates,
        IClock clock)
    {
        _payRates = payRates;
        _overtime = overtime;
        _breaks = breaks;
        _exchangeRates = exchangeRates;
        _clock = clock;
    }

    /// <param name="rows">Every entry that will be priced, so the overtime and paid breaks of their days are loaded once.</param>
    public static async Task<LabourPricer> LoadAsync(
        IProjectFinanceRepository sources,
        IProjectFinanceService finance,
        IReadOnlyCollection<WorkRow> rows,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var payRates = await finance.PayRatesAsync(rows.Select(r => r.UserId).Distinct().ToList(), cancellationToken);
        var work = rows.Select(r => new WorkEntryRow(r.Id, r.ProjectId, r.UserId, r.StartedAt, r.Minutes)).ToList();
        var breaks = await finance.PaidBreaksAsync(work, payRates, cancellationToken);

        return new(
            payRates,
            await finance.PaidOvertimeAsync(work, payRates, cancellationToken),
            breaks.ToLookup(b => (b.UserId, b.ProjectId, b.Cost.Day), b => b.Cost),
            new ExchangeRateTable(await sources.ExchangeRatesAsync(cancellationToken)),
            clock);
    }

    /// <summary>The entries' labour cost, with the paid breaks of the people, projects and days they cover.</summary>
    public LabourCostResponse Price(IEnumerable<WorkRow> rows)
    {
        var list = rows.ToList();
        var (items, unpriced) = ProjectFinance.PriceLabour(
            list.Select(r => PaidOvertime.Of(r.UserId, _clock.ToCompanyDate(r.StartedAt), r.Minutes, _overtime.GetValueOrDefault(r.Id))),
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
