using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Company;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Business.Expenses;
using ID_Indetails.api.Business.SiteExpenses;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.SiteExpenses;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.SiteExpenses;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Repositories.SiteExpenses.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.SiteExpenses.Abstraction;

namespace ID_Indetails.api.Services.SiteExpenses.Implementation;

public sealed class SiteExpenseService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    ISiteExpenseRepository siteExpenses,
    IAccountLabelRepository accountLabels,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IProfitFloor floor,
    IClock clock) : ISiteExpenseService
{
    private const string ProjectNotFound = "Project not found.";
    private const string ExpenseNotFound = "Expense not found.";

    public async Task<SiteExpenseListResponse> ListAsync(long projectId, SiteExpenseListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException(ProjectNotFound);
        }

        if (project.DomainCode != DomainCodes.Execution)
        {
            throw new BusinessRuleException(ErrorCodes.NotForThisDomain, "Only Execution projects have site expenses.");
        }

        var all = await siteExpenses.ListAsync(projectId, cancellationToken);
        var matching = all.Where(r => Matches(r, query)).ToList();
        var page = matching.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize);
        var open = IsOpen(project);

        return new SiteExpenseListResponse(
            page.Select(r => ToResponse(r, actor, open)).ToList(),
            query.Page,
            query.PageSize,
            matching.Count,
            Totals(matching),
            Totals(all),
            all.GroupBy(r => SiteExpenseRules.GroupKey(r.Expense.Name))
                .Select(g => new SiteExpenseTypeResponse(g.Key, g.First().Expense.Name.Trim(), g.Count(), UsdCost(g)))
                .OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            all.Where(r => !string.IsNullOrWhiteSpace(r.Expense.Supplier))
                .GroupBy(r => SiteExpenseRules.GroupKey(r.Expense.Supplier!))
                .Select(g => new SiteExpenseSupplierResponse(g.Key, g.First().Expense.Supplier!.Trim()))
                .OrderBy(s => s.Key, StringComparer.Ordinal)
                .ToList(),
            all.GroupBy(r => r.Expense.AddedById)
                .Select(g => new PersonRefResponse(g.Key, g.First().AddedByName))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            open && CanWrite(actor));
    }

    public async Task<IReadOnlyList<SiteExpenseResponse>> AddAsync(
        long projectId,
        AddSiteExpensesRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await LockedProjectAsync(actor, projectId, cancellationToken);
        var existing = await AlreadyAddedAsync(actor.UserId, project.Id, request.Items, cancellationToken);
        var fresh = request.Items
            .Select((item, index) => (Item: item, Index: index))
            .Where(x => x.Item.ClientRef is not { } clientRef || !existing.ContainsKey(clientRef))
            .ToList();

        // A batch sent again whose expenses were all added already changes nothing, the site's lock included.
        var added = new List<SiteExpense>();
        if (fresh.Count > 0)
        {
            Throw(SiteRules.CheckOpen(project.DomainCode, project.Status, project.RevenueLockedAt is not null));
            await CheckLabelsAsync(fresh.Select(x => (x.Item.AccountLabelId, $"items[{x.Index}].accountLabelId", (long?)null)), cancellationToken);
            var before = await floor.BeforeAsync(project, cancellationToken);
            foreach (var (item, _) in fresh)
            {
                var entry = SiteExpenseRules.Normalise(new SiteExpenseRules.Entry(item.Quantity, item.UnitCost, item.Amount));
                var expense = new SiteExpense
                {
                    ProjectId = project.Id,
                    Currency = PaymentRules.BaseCurrency,
                    SpentOn = clock.CompanyToday,
                    AddedAt = clock.UtcNow,
                    AddedById = actor.UserId,
                    ClientRef = item.ClientRef,
                };
                Apply(expense, item, entry);
                siteExpenses.Add(expense);
                added.Add(expense);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            foreach (var expense in added)
            {
                activity.Add(ExpenseEvent(project, expense, ActivityEventKinds.MaterialAdded, actor.UserId, Cost(expense)));
            }

            await NotifyAddedAsync(project, added.Count, actor.UserId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await floor.CheckAsync(before, BlockedChangeKind.MaterialAdd, new BlockedRequest<AddSiteExpensesRequest>(project.Id, request), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // In request order: each item's new expense, or the one an earlier send of it already added.
        var newOnes = new Queue<SiteExpense>(added);
        var ids = request.Items
            .Select(i => i.ClientRef is { } clientRef && existing.TryGetValue(clientRef, out var earlier) ? earlier.Id : newOnes.Dequeue().Id)
            .ToList();
        var rows = await siteExpenses.GetRowsAsync(ids, cancellationToken);
        var open = IsOpen(project);
        return rows.Select(r => ToResponse(r, actor, open)).ToList();
    }

    public async Task<SiteExpenseResponse> UpdateAsync(
        long projectId,
        long expenseId,
        SiteExpenseEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await OpenProjectAsync(actor, projectId, cancellationToken);
        var expense = await ChangeableExpenseAsync(actor, projectId, expenseId, cancellationToken);
        await CheckLabelsAsync([(request.AccountLabelId, "accountLabelId", expense.AccountLabelId)], cancellationToken);

        var entry = SiteExpenseRules.Normalise(new SiteExpenseRules.Entry(request.Quantity, request.UnitCost, request.Amount));
        var before = await floor.BeforeAsync(project, cancellationToken);
        var previousCost = Cost(expense);
        Apply(expense, request, entry);
        expense.UpdatedAt = clock.UtcNow;
        activity.Add(ExpenseEvent(project, expense, ActivityEventKinds.MaterialUpdated, actor.UserId, Cost(expense), previousCost));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await floor.CheckAsync(before, BlockedChangeKind.MaterialUpdate,
            new BlockedRequest<SiteExpenseEntryRequest>(project.Id, request, ItemId: expense.Id), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(expense.Id, actor, cancellationToken);
    }

    public async Task DeleteAsync(long projectId, long expenseId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await OpenProjectAsync(actor, projectId, cancellationToken);
        var expense = await ChangeableExpenseAsync(actor, projectId, expenseId, cancellationToken);

        expense.DeletedAt = clock.UtcNow;
        activity.Add(ExpenseEvent(project, expense, ActivityEventKinds.MaterialRemoved, actor.UserId, Cost(expense)));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    /// <summary>Locks the project row, so a change can't slip in while it is being finalised.</summary>
    private async Task<Project> OpenProjectAsync(UserAccess actor, long projectId, CancellationToken cancellationToken)
    {
        var project = await LockedProjectAsync(actor, projectId, cancellationToken);
        Throw(SiteRules.CheckOpen(project.DomainCode, project.Status, project.RevenueLockedAt is not null));
        return project;
    }

    /// <summary>The project, locked, if the caller can see it. Two sends of the same batch wait for each other here.</summary>
    private async Task<Project> LockedProjectAsync(UserAccess actor, long projectId, CancellationToken cancellationToken)
    {
        var project = await projects.GetForUpdateAsync(projectId, cancellationToken);
        return project is not null && actor.CanSee(project.DomainCode) ? project : throw new NotFoundException(ProjectNotFound);
    }

    /// <summary>
    /// What the caller already added with the items' client refs, by ref. A ref already used on another site is refused:
    /// it can't be the same expense.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, SiteExpense>> AlreadyAddedAsync(
        long actorId,
        long projectId,
        IReadOnlyList<NewSiteExpenseRequest> items,
        CancellationToken cancellationToken)
    {
        var refs = items.Where(i => i.ClientRef is not null).Select(i => i.ClientRef!.Value).ToList();
        if (refs.Count == 0)
        {
            return new Dictionary<Guid, SiteExpense>();
        }

        var found = await siteExpenses.ByClientRefsAsync(actorId, refs, cancellationToken);
        if (found.FirstOrDefault(e => e.ProjectId != projectId) is { } elsewhere)
        {
            var index = items.ToList().FindIndex(i => i.ClientRef == elsewhere.ClientRef);
            throw new RequestValidationException(
                ErrorCodes.ValidationFailed, $"items[{index}].clientRef", "This client reference was already used for an expense on another site.");
        }

        return found.ToDictionary(e => e.ClientRef!.Value);
    }

    /// <summary>Each label named must exist, and an archived one can't be newly picked: only one the expense already has stays.</summary>
    private async Task CheckLabelsAsync(IEnumerable<(long? LabelId, string Field, long? Current)> picks, CancellationToken cancellationToken)
    {
        var named = picks.Where(p => p.LabelId is not null).ToList();
        if (named.Count == 0)
        {
            return;
        }

        var labels = (await accountLabels.ListAsync(includeArchived: true, cancellationToken)).ToDictionary(l => l.Id);
        foreach (var (labelId, field, current) in named)
        {
            var found = labels.GetValueOrDefault(labelId!.Value);
            Throw(AccountLabelRules.CheckPickable(found is not null, found?.ArchivedAt is not null, current == labelId, field));
        }
    }

    private async Task<SiteExpense> ChangeableExpenseAsync(UserAccess actor, long projectId, long expenseId, CancellationToken cancellationToken)
    {
        var expense = await siteExpenses.GetForUpdateAsync(projectId, expenseId, cancellationToken)
            ?? throw new NotFoundException(ExpenseNotFound);
        Throw(SiteRules.CheckCanChange(expense.AddedById == actor.UserId, actor.Has(PermissionCodes.MaterialManage)));
        return expense;
    }

    private async Task NotifyAddedAsync(Project project, int count, long actorId, CancellationToken cancellationToken)
        => await notifications.SendAsync(
            new NotificationDraft(
                NotificationKinds.MaterialsAdded,
                actorId,
                project.DomainCode,
                project.Id,
                "project",
                project.Id,
                new { count, projectTitle = project.Title }),
            await notifications.HoldersAsync(project.DomainCode, PermissionCodes.MaterialManage, cancellationToken),
            cancellationToken);

    private async Task<SiteExpenseResponse> DescribeAsync(long expenseId, UserAccess actor, CancellationToken cancellationToken)
        => ToResponse((await siteExpenses.GetRowsAsync([expenseId], cancellationToken))[0], actor, open: true);

    private bool Matches(SiteExpenseRecord row, SiteExpenseListQuery query)
    {
        var e = row.Expense;
        if (query.Type is { Length: > 0 } type && SiteExpenseRules.GroupKey(e.Name) != type)
        {
            return false;
        }

        if (query.Supplier is { Length: > 0 } supplier && (e.Supplier is null || SiteExpenseRules.GroupKey(e.Supplier) != supplier))
        {
            return false;
        }

        if (query.AddedById is { } addedBy && e.AddedById != addedBy)
        {
            return false;
        }

        if (query.Category is { } category && e.Category != category)
        {
            return false;
        }

        if (query.AccountLabelId is { } label && e.AccountLabelId != label)
        {
            return false;
        }

        if (e.SpentOn < query.From || e.SpentOn > query.To)
        {
            return false;
        }

        var search = query.Search?.Trim();
        return string.IsNullOrEmpty(search)
            || new[] { e.Name, e.Supplier, e.Note, e.Unit, row.AddedByName }
                .Any(text => text?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
    }

    private SiteExpenseResponse ToResponse(SiteExpenseRecord row, UserAccess actor, bool open)
    {
        var e = row.Expense;
        return new SiteExpenseResponse
        {
            Id = e.Id,
            Name = e.Name,
            Quantity = e.Quantity,
            Unit = e.Unit,
            UnitCost = e.UnitCost,
            Cost = Cost(e),
            Currency = e.Currency,
            Supplier = e.Supplier,
            Note = e.Note,
            SpentOn = e.SpentOn,
            Category = e.Category,
            AccountLabel = e.AccountLabelId is { } labelId && row.AccountLabelName is { } labelName
                ? new AccountLabelRefResponse(labelId, labelName)
                : null,
            AddedAt = e.AddedAt,
            AddedOn = clock.ToCompanyDate(e.AddedAt),
            AddedBy = new PersonRefResponse(e.AddedById, row.AddedByName),
            UpdatedAt = e.UpdatedAt,
            CanEdit = open && CanWrite(actor) && SiteRules.CanChange(e.AddedById == actor.UserId, actor.Has(PermissionCodes.MaterialManage)),
        };
    }

    /// <summary>Amounts in different currencies are never added together: <c>Cost</c> is the USD rows, <c>ByCurrency</c> every currency.</summary>
    private static SiteExpenseTotalsResponse Totals(IReadOnlyList<SiteExpenseRecord> rows)
        => new(
            rows.Count,
            UsdCost(rows),
            ExpenseRules.Totals(rows.Select(r => (r.Expense.Currency, Cost(r.Expense))))
                .Select(t => new CurrencyTotalResponse(t.Currency, t.Total))
                .ToList());

    private static decimal UsdCost(IEnumerable<SiteExpenseRecord> rows)
        => rows.Where(r => r.Expense.Currency == PaymentRules.BaseCurrency).Sum(r => Cost(r.Expense));

    private static bool CanWrite(UserAccess actor)
        => actor.Has(PermissionCodes.MaterialCreate) || actor.Has(PermissionCodes.MaterialManage);

    private static bool IsOpen(Project project)
        => SiteRules.CheckOpen(project.DomainCode, project.Status, project.RevenueLockedAt is not null) is null;

    /// <summary>
    /// Sets what the request names. Currency, date and category left out stay as they are (a new expense starts with
    /// USD, today and materials); the label is set to what the request says, none when it says none.
    /// </summary>
    private static void Apply(SiteExpense expense, SiteExpenseEntryRequest request, SiteExpenseRules.Entry entry)
    {
        expense.Name = request.Name.Trim();
        expense.Quantity = entry.Quantity;
        expense.Unit = Trimmed(request.Unit);
        expense.UnitCost = entry.UnitCost;
        expense.Amount = entry.Amount;
        expense.Supplier = Trimmed(request.Supplier);
        expense.Note = Trimmed(request.Note);
        expense.Currency = request.Currency?.ToUpperInvariant() ?? expense.Currency;
        expense.SpentOn = request.SpentOn ?? expense.SpentOn;
        expense.Category = request.Category ?? expense.Category;
        expense.AccountLabelId = request.AccountLabelId;
    }

    /// <summary>Worked out here rather than read from the generated column, which is only filled in after a save.</summary>
    private static decimal Cost(SiteExpense e) => SiteExpenseRules.Cost(e.Quantity, e.UnitCost, e.Amount);

    private static string? Trimmed(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private ActivityEvent ExpenseEvent(Project project, SiteExpense expense, string kind, long actorId, decimal amount, decimal? prevAmount = null) => new()
    {
        DomainCode = project.DomainCode,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        ProjectId = project.Id,
        SubjectType = "material",
        SubjectId = expense.Id,
        Amount = amount,
        PrevAmount = prevAmount,
        Payload = JsonSerializer.Serialize(new { name = expense.Name }),
    };

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
