using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Company;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Response.Company;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;

namespace ID_Indetails.api.Services.Company.Implementation;

/// <summary>Company overhead (see <c>Business/Company/OverheadRules</c>).</summary>
public sealed class OverheadService(
    ICurrentUser currentUser,
    IOverheadRepository overheads,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : IOverheadService
{
    private const string NotFoundMessage = "Company expense not found.";

    private DateOnly ThisMonth => OverheadRules.MonthStart(clock.CompanyToday);

    public async Task<OverheadResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var lines = (await overheads.ListAsync(cancellationToken)).Select(Describe).ToList();
        var charged = lines.Where(l => l.CurrentAmount is not null).ToList();

        return new OverheadResponse(
            lines.OrderByDescending(l => l.CurrentAmount ?? 0).ThenBy(l => l.Name).ToList(),
            charged.GroupBy(l => l.Currency)
                .Select(g => new CurrencyTotalResponse(g.Key, g.Sum(l => l.CurrentAmount!.Value)))
                .OrderBy(t => t.Currency)
                .ToList(),
            charged.GroupBy(l => (l.CostGroup, l.Currency))
                .Select(g => new OverheadGroupTotalResponse(g.Key.CostGroup, g.Key.Currency, g.Sum(l => l.CurrentAmount!.Value)))
                .ToList());
    }

    public async Task<IReadOnlyList<OverheadMonthResponse>> MonthlyAsync(int months, CancellationToken cancellationToken = default)
    {
        var items = await overheads.ListAsync(cancellationToken);

        return Enumerable.Range(0, months)
            .Select(i => ThisMonth.AddMonths(i - months + 1))
            .Select(month => new OverheadMonthResponse(
                month,
                items.Select(o => (o.Currency, Amount: AmountFor(o, month)))
                    .Where(x => x.Amount is not null)
                    .GroupBy(x => x.Currency)
                    .Select(g => new CurrencyTotalResponse(g.Key, g.Sum(x => x.Amount!.Value)))
                    .OrderBy(t => t.Currency)
                    .ToList()))
            .ToList();
    }

    public async Task<OverheadLineResponse> CreateAsync(CreateOverheadRequest request, CancellationToken cancellationToken = default)
    {
        var start = OverheadRules.MonthStart(request.StartMonth ?? clock.CompanyToday);
        OverheadRules.CheckMonth(start, clock.CompanyToday, null, "startMonth").ThrowIfDenied();

        var item = new OverheadItem
        {
            Name = request.Name.Trim(),
            CostGroup = request.CostGroup,
            Currency = request.Currency.ToUpperInvariant(),
            StartedOn = start,
            CreatedAt = clock.UtcNow,
            CreatedById = currentUser.Id,
        };
        item.Amounts.Add(new OverheadAmount { EffectiveFrom = start, Amount = request.Amount, CreatedAt = clock.UtcNow, CreatedById = currentUser.Id });
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        overheads.Add(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        Record(item, ActivityEventKinds.OverheadAdded, request.Amount);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Describe(item);
    }

    public async Task<OverheadLineResponse> UpdateAsync(long id, UpdateOverheadRequest request, CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(id, cancellationToken);
        item.Name = request.Name.Trim();
        item.CostGroup = request.CostGroup;

        Record(item, ActivityEventKinds.OverheadUpdated, null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Describe(item);
    }

    public async Task<OverheadLineResponse> ChangeAmountAsync(long id, ChangeOverheadAmountRequest request, CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(id, cancellationToken);
        if (OverheadRules.HasEnded(item.EndedOn, clock.CompanyToday))
        {
            throw new BusinessRuleException(ErrorCodes.OverheadEnded, "This expense has ended. Add it again as a new one.");
        }

        var month = OverheadRules.MonthStart(request.FromMonth ?? clock.CompanyToday);
        var latest = item.Amounts.Max(a => (DateOnly?)a.EffectiveFrom);
        OverheadRules.CheckMonth(month, clock.CompanyToday, latest, "fromMonth").ThrowIfDenied();

        // A change from a month that already has one (this month or later) replaces it.
        var existing = item.Amounts.SingleOrDefault(a => a.EffectiveFrom == month);
        if (existing is not null)
        {
            existing.Amount = request.Amount;
        }
        else
        {
            overheads.AddAmount(new OverheadAmount
            {
                OverheadItemId = item.Id,
                EffectiveFrom = month,
                Amount = request.Amount,
                CreatedAt = clock.UtcNow,
                CreatedById = currentUser.Id,
            });
        }

        Record(item, ActivityEventKinds.OverheadAmountChanged, request.Amount, new { fromMonth = month });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Describe(item);
    }

    public async Task<OverheadLineResponse> ChangeCurrencyAsync(long id, ChangeOverheadCurrencyRequest request, CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(id, cancellationToken);
        var month = OverheadRules.MonthStart(request.FromMonth ?? clock.CompanyToday);
        var currency = request.Currency.ToUpperInvariant();
        OverheadRules.CheckCurrencyChange(item.Currency, currency, item.EndedOn, month, clock.CompanyToday).ThrowIfDenied();

        if (!OverheadRules.CurrencyChangeSplits(item.StartedOn, month))
        {
            // Never charged before the change: the line just changes, with the new amount throughout.
            var previous = item.Currency;
            item.Currency = currency;
            item.Amounts.Clear();
            item.Amounts.Add(new OverheadAmount { EffectiveFrom = item.StartedOn, Amount = request.Amount, CreatedAt = clock.UtcNow, CreatedById = currentUser.Id });
            Record(item, ActivityEventKinds.OverheadCurrencyChanged, request.Amount, new { previousCurrency = previous, fromMonth = item.StartedOn });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Describe(item);
        }

        var next = new OverheadItem
        {
            Name = item.Name,
            CostGroup = item.CostGroup,
            Currency = currency,
            StartedOn = month,
            CreatedAt = clock.UtcNow,
            CreatedById = currentUser.Id,
        };
        next.Amounts.Add(new OverheadAmount { EffectiveFrom = month, Amount = request.Amount, CreatedAt = clock.UtcNow, CreatedById = currentUser.Id });

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        item.EndedOn = month.AddDays(-1);
        overheads.Add(next);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        Record(item, ActivityEventKinds.OverheadCurrencyChanged, null, new { endedOn = item.EndedOn, replacedBy = next.Id });
        Record(next, ActivityEventKinds.OverheadAdded, request.Amount, new { replaces = item.Id });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Describe(next);
    }

    public async Task<OverheadLineResponse?> CancelAsync(long id, CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(id, cancellationToken);
        if (item.EndedOn is not null)
        {
            throw new BusinessRuleException(ErrorCodes.OverheadAlreadyCancelled, "This expense is already cancelled.");
        }

        var endOfMonth = OverheadRules.MonthEnd(clock.CompanyToday);
        if (item.StartedOn > endOfMonth)
        {
            // Never charged: nothing to keep.
            Record(item, ActivityEventKinds.OverheadRemoved, null);
            overheads.Remove(item);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return null;
        }

        item.EndedOn = endOfMonth;
        Record(item, ActivityEventKinds.OverheadCancelled, null, new { endedOn = endOfMonth });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Describe(item);
    }

    public async Task<OverheadLineResponse> ResumeAsync(long id, CancellationToken cancellationToken = default)
    {
        var item = await FindAsync(id, cancellationToken);
        if (item.EndedOn is null)
        {
            throw new BusinessRuleException(ErrorCodes.OverheadNotCancelled, "This expense isn't cancelled.");
        }

        if (OverheadRules.HasEnded(item.EndedOn, clock.CompanyToday))
        {
            throw new BusinessRuleException(ErrorCodes.OverheadEnded, "This expense has ended. Add it again as a new one.");
        }

        var others = (await overheads.ListAsync(cancellationToken)).Where(o => o.Id != item.Id).Select(o => (o.Name, o.CostGroup, o.StartedOn));
        OverheadRules.CheckResume(item.Name, item.CostGroup, item.EndedOn.Value, others).ThrowIfDenied();

        item.EndedOn = null;
        Record(item, ActivityEventKinds.OverheadResumed, null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Describe(item);
    }

    private async Task<OverheadItem> FindAsync(long id, CancellationToken cancellationToken)
        => await overheads.GetForUpdateAsync(id, cancellationToken) ?? throw new NotFoundException(NotFoundMessage);

    private static decimal? AmountFor(OverheadItem item, DateOnly month)
        => OverheadRules.AmountFor(month, item.StartedOn, item.EndedOn, item.Amounts.Select(a => new OverheadRules.Amount(a.EffectiveFrom, a.Amount)));

    private OverheadLineResponse Describe(OverheadItem item)
    {
        var current = AmountFor(item, ThisMonth);
        return new OverheadLineResponse(
            item.Id,
            item.Name,
            item.CostGroup,
            item.Currency,
            current,
            item.EndedOn is null && current is not null ? current * 12 : null,
            item.StartedOn,
            item.EndedOn,
            item.EndedOn is not null,
            item.Amounts.OrderBy(a => a.EffectiveFrom).Select(a => new OverheadAmountResponse(a.EffectiveFrom, a.Amount)).ToList());
    }

    private void Record(OverheadItem item, string kind, decimal? amount, object? payload = null)
        => activity.Add(new ActivityEvent
        {
            Kind = kind,
            OccurredAt = clock.UtcNow,
            ActorId = currentUser.Id,
            SubjectType = "overhead_item",
            SubjectId = item.Id,
            Amount = amount,
            Payload = JsonSerializer.Serialize(new { name = item.Name, currency = item.Currency, details = payload }),
        });
}
