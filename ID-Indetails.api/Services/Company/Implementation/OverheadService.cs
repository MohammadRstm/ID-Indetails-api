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
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Company.Abstraction;

namespace ID_Indetails.api.Services.Company.Implementation;

/// <summary>Company overhead (see <c>Business/Company/OverheadRules</c>).</summary>
public sealed class OverheadService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IOverheadRepository overheads,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : IOverheadService
{
    private const string NotFoundMessage = "Company expense not found.";
    private const string GroupNotFoundMessage = "Category not found.";

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
            charged.GroupBy(l => (l.Group.Id, l.Group.Name, l.Currency))
                .Select(g => new OverheadGroupTotalResponse(g.Key.Id, g.Key.Name, g.Key.Currency, g.Sum(l => l.CurrentAmount!.Value)))
                .OrderByDescending(t => t.Total)
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
        var group = await GroupAsync(request.GroupId, cancellationToken);

        var item = new OverheadItem
        {
            Name = request.Name.Trim(),
            Group = group,
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
        if (item.GroupId != request.GroupId)
        {
            item.Group = await GroupAsync(request.GroupId, cancellationToken);
        }

        item.Name = request.Name.Trim();

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
            Group = item.Group,
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

        var others = (await overheads.ListAsync(cancellationToken)).Where(o => o.Id != item.Id).Select(o => (o.Name, o.GroupId, o.StartedOn));
        OverheadRules.CheckResume(item.Name, item.GroupId, item.EndedOn.Value, others).ThrowIfDenied();

        item.EndedOn = null;
        Record(item, ActivityEventKinds.OverheadResumed, null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Describe(item);
    }

    public async Task<IReadOnlyList<OverheadGroupResponse>> GroupsAsync(CancellationToken cancellationToken = default)
        => (await overheads.GroupsAsync(cancellationToken)).Select(g => new OverheadGroupResponse(g.Group.Id, g.Group.Name, g.Lines)).ToList();

    public async Task<OverheadGroupResponse> AddGroupAsync(OverheadGroupRequest request, CancellationToken cancellationToken = default)
    {
        await CheckManageGroupsAsync(cancellationToken);
        var name = request.Name.Trim();
        await EnsureNameFreeAsync(name, null, cancellationToken);

        var group = new OverheadGroup
        {
            Name = name,
            SortOrder = OverheadRules.NextSortOrder((await overheads.GroupsAsync(cancellationToken)).Select(g => g.Group.SortOrder)),
            CreatedAt = clock.UtcNow,
        };
        overheads.AddGroup(group);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new OverheadGroupResponse(group.Id, group.Name, 0);
    }

    public async Task<OverheadGroupResponse> RenameGroupAsync(long id, OverheadGroupRequest request, CancellationToken cancellationToken = default)
    {
        await CheckManageGroupsAsync(cancellationToken);
        var group = await overheads.GetGroupForUpdateAsync(id, cancellationToken) ?? throw new NotFoundException(GroupNotFoundMessage);
        var name = request.Name.Trim();
        await EnsureNameFreeAsync(name, id, cancellationToken);

        group.Name = name;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new OverheadGroupResponse(group.Id, group.Name, await overheads.LinesInGroupAsync(id, cancellationToken));
    }

    public async Task RemoveGroupAsync(long id, CancellationToken cancellationToken = default)
    {
        await CheckManageGroupsAsync(cancellationToken);
        var group = await overheads.GetGroupForUpdateAsync(id, cancellationToken) ?? throw new NotFoundException(GroupNotFoundMessage);
        OverheadRules.CheckRemoveGroup(await overheads.LinesInGroupAsync(id, cancellationToken)).ThrowIfDenied();

        overheads.RemoveGroup(group);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task CheckManageGroupsAsync(CancellationToken cancellationToken)
        => OverheadRules.CheckManageGroups((await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).IsSuperAdmin).ThrowIfDenied();

    private async Task EnsureNameFreeAsync(string name, long? exceptId, CancellationToken cancellationToken)
    {
        if (await overheads.GroupNameTakenAsync(name, exceptId, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.OverheadGroupExists, "There is already a category with this name.");
        }
    }

    /// <summary>The category a line is put in, tracked; a missing one is the request's mistake.</summary>
    private async Task<OverheadGroup> GroupAsync(long groupId, CancellationToken cancellationToken)
        => await overheads.GetGroupForUpdateAsync(groupId, cancellationToken)
            ?? throw new RequestValidationException(ErrorCodes.OverheadGroupNotFound, "groupId", "That category doesn't exist any more. Choose another.");

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
            new OverheadGroupSummaryResponse(item.Group.Id, item.Group.Name),
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
