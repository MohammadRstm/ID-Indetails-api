using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Expenses;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Business.WorkEntries;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.DTO.Response.Finance;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Clients.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Company.Abstraction;
using ID_Indetails.api.Repositories.Expenses.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.MoneySummaries.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Implementation;

namespace ID_Indetails.api.Services.Projects.Implementation;

/// <summary>Projects of the caller's visible domains. Projects of any other domain read as not found.</summary>
public sealed class ProjectService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IClientRepository clients,
    IUserRepository users,
    IExpenseRepository expenses,
    IWorkEntryRepository entries,
    ICompanyRepository company,
    IPaymentService paymentService,
    IMoneyRollupService moneyRollups,
    IActivityEventRepository activity,
    IProjectNotifier notifier,
    IUnitOfWork unitOfWork,
    IClock clock) : IProjectService
{
    private const string NotFoundMessage = "Project not found.";
    public const int ListSparkWeeks = 8;
    public const int ListContributors = 4;

    public async Task<PagedResponse<ProjectSummaryResponse>> ListAsync(ProjectListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var period = OpenPeriodOf(query.Year, query.Quarter);
        if (period is { } future && future.From > clock.CompanyToday)
        {
            // Nothing can have been open during a period that hasn't started.
            return new PagedResponse<ProjectSummaryResponse>([], query.Page, query.PageSize, 0);
        }

        var (items, total) = await projects.SearchAsync(SearchOf(actor, query, period, query.Page, query.PageSize), cancellationToken);

        // One batched load of each kind for the whole page, never a query per project.
        var work = actor.Has(PermissionCodes.WorkEntryViewAll) ? await ListWorkAsync(items, cancellationToken) : null;
        var rollup = actor.Has(PermissionCodes.FinancialView) ? await moneyRollups.LoadAsync(items, cancellationToken) : null;
        return new PagedResponse<ProjectSummaryResponse>(
            items.Select(p => ToSummary(p) with { Money = rollup?.ListMoney(p), Work = work?.GetValueOrDefault(p.Id) }).ToList(),
            query.Page,
            query.PageSize,
            total);
    }

    public async Task<ProjectListTotalsResponse> TotalsAsync(ProjectListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var period = OpenPeriodOf(query.Year, query.Quarter);
        IReadOnlyList<Project> matching = period is { } future && future.From > clock.CompanyToday
            ? []
            : (await projects.SearchAsync(SearchOf(actor, query, period, 1, int.MaxValue), cancellationToken)).Items;

        var rollup = await moneyRollups.LoadAsync(matching, cancellationToken);
        var client = matching.Where(p => p.BillingType == BillingType.Client).ToList();
        var inHouse = matching.Where(p => p.BillingType != BillingType.Client).ToList();
        decimal Sum(IEnumerable<Project> list, Func<ProjectFinance.Summary, decimal> of)
            => PeriodMoney.Round(list.Sum(p => of(rollup.Money(p.Id).Summary)));

        return new ProjectListTotalsResponse(
            matching.Count,
            client.Count,
            inHouse.Count,
            Sum(matching, s => s.Revenue),
            Sum(matching, s => s.Cost),
            Sum(client, s => s.Profit),
            Sum(inHouse, s => s.Cost),
            rollup.HasUnconverted(matching),
            PaymentRules.BaseCurrency);
    }

    private ProjectSearch SearchOf(UserAccess actor, ProjectListQuery query, (DateOnly From, DateOnly To)? period, int page, int pageSize)
        => new(
            actor.VisibleDomains,
            query.Search?.Trim(),
            query.Status,
            query.ClientId,
            query.BillingType,
            query.Domain,
            page,
            pageSize,
            query.OwnerId,
            period is { } open ? new OpenPeriod(clock.CompanyDayStartUtc(open.From), open.To) : null,
            query.Sort);

    public async Task<ProjectResponse> GetAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        return await DescribeAsync(actor, projectId, cancellationToken);
    }

    public async Task<ProjectResponse> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var domain = request.DomainCode ?? (actor.VisibleDomains.Count == 1 ? actor.VisibleDomains[0] : null)
            ?? throw new RequestValidationException(ErrorCodes.ValidationFailed, "domainCode", "Choose the domain the project belongs to.");

        if (!actor.CanSee(domain))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "You can only create projects in a domain you work in.");
        }

        Throw(ProjectRules.CheckSetup(new ProjectRules.Setup(
            domain, request.BillingType, request.ClientId, request.Revenue, request.ProjectFee, request.MaterialMarkup)));
        Throw(ProjectRules.CheckInitialPayment(domain, request.InitialPayment is not null));
        if (request.InitialPayment is not null && !actor.Has(PermissionCodes.PaymentRecord))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Recording the initial payment needs the permission to record payments.");
        }

        if (request.LabourDefaults is not null)
        {
            if (domain != DomainCodes.Execution)
            {
                throw new RequestValidationException(ErrorCodes.ValidationFailed, "labourDefaults", "Only Execution projects have labour defaults.");
            }

            if (!actor.Has(PermissionCodes.LabourReportManage))
            {
                throw new ForbiddenException(ErrorCodes.Forbidden, "Setting labour defaults needs the permission to manage labour reports.");
            }
        }

        await EnsureClientAsync(request.ClientId, domain, cancellationToken);
        var ownerId = request.OwnerId ?? actor.UserId;
        await EnsureOwnerAsync(ownerId, domain, cancellationToken);

        var now = clock.UtcNow;
        var project = new Project
        {
            DomainCode = domain,
            Title = request.Title.Trim(),
            Description = Clean(request.Description),
            Icon = Clean(request.Icon),
            Tint = Clean(request.Tint),
            Status = ProjectStatus.Active,
            BillingType = request.BillingType,
            ClientId = request.ClientId,
            OwnerId = ownerId,
            StartDate = clock.CompanyToday,
            TargetDate = request.TargetDate,
            Revenue = request.Revenue ?? 0,
            ProjectFee = request.ProjectFee,
            MaterialMarkup = request.MaterialMarkup,
            CreatedAt = now,
            CreatedById = actor.UserId,
        };

        if (request.LabourDefaults is { } labour)
        {
            project.LabourDefaults.Add(new ProjectLabourDefault
            {
                EffectiveFrom = project.StartDate,
                Workers = labour.Workers,
                DailyPay = labour.DailyPay,
                Currency = PaymentRules.BaseCurrency,
                CreatedAt = now,
                CreatedById = actor.UserId,
            });
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        projects.Add(project);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity.Add(ProjectEvent(project, ActivityEventKinds.ProjectCreated, actor.UserId));
        await notifier.CreatedAsync(project, actor.UserId, cancellationToken);
        if (project.Revenue > 0)
        {
            activity.Add(ProjectEvent(project, ActivityEventKinds.RevenueSet, actor.UserId, amount: project.Revenue));
        }

        if (project.ProjectFee is > 0)
        {
            activity.Add(ProjectEvent(project, ActivityEventKinds.ProjectFeeSet, actor.UserId, amount: project.ProjectFee));
        }

        if (request.LabourDefaults is { } defaults)
        {
            activity.Add(ProjectEvent(project, ActivityEventKinds.LabourDefaultsSet, actor.UserId,
                new { workers = defaults.Workers, previousWorkers = (int?)null, effectiveFrom = project.StartDate }, amount: defaults.DailyPay));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (request.InitialPayment is { } initial)
        {
            await paymentService.RecordForProjectAsync(project, new RecordPaymentRequest
            {
                Amount = initial.Amount,
                Currency = initial.Currency,
                FxRate = initial.FxRate,
                ReceivedOn = initial.ReceivedOn,
                Method = initial.Method,
                Reference = initial.Reference,
                Note = initial.Note,
                IsInitial = true,
            }, actor.UserId, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, project.Id, cancellationToken);
    }

    public async Task<ProjectResponse> UpdateAsync(long projectId, UpdateProjectRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await VisibleProjectForUpdateAsync(actor, projectId, cancellationToken);

        if (request.OwnerId != project.OwnerId)
        {
            await EnsureOwnerAsync(request.OwnerId, project.DomainCode, cancellationToken);
            activity.Add(ProjectEvent(project, ActivityEventKinds.ProjectOwnerSet, actor.UserId,
                new { ownerId = request.OwnerId, prevOwnerId = project.OwnerId }));
            project.OwnerId = request.OwnerId;
            await notifier.OwnerSetAsync(project, actor.UserId, cancellationToken);
        }

        project.Title = request.Title.Trim();
        project.Description = Clean(request.Description);
        project.Icon = Clean(request.Icon);
        project.Tint = Clean(request.Tint);
        project.TargetDate = request.TargetDate;
        project.UpdatedAt = clock.UtcNow;
        activity.Add(ProjectEvent(project, ActivityEventKinds.ProjectUpdated, actor.UserId));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, project.Id, cancellationToken);
    }

    public async Task<ProjectResponse> UpdateBillingAsync(
        long projectId,
        UpdateProjectBillingRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await VisibleProjectForUpdateAsync(actor, projectId, cancellationToken);

        if (project.DomainCode == DomainCodes.Execution)
        {
            throw new BusinessRuleException(
                ErrorCodes.NotForThisDomain, "Execution projects are always client work; change their fee and markup instead.");
        }

        Throw(ProjectRules.CheckMoneyEditable(project.RevenueLockedAt is not null));
        Throw(ProjectRules.CheckSetup(new ProjectRules.Setup(
            project.DomainCode, request.BillingType, request.ClientId, request.Revenue, null, null)));
        Throw(ProjectRules.CheckClientChange(
            project.ClientId, request.ClientId, await projects.HasBillingRecordsAsync(project.Id, cancellationToken)));

        if (request.ClientId != project.ClientId)
        {
            await EnsureClientAsync(request.ClientId, project.DomainCode, cancellationToken);
            activity.Add(ProjectEvent(project, ActivityEventKinds.ProjectClientSet, actor.UserId,
                new { clientId = request.ClientId, prevClientId = project.ClientId }));
        }

        var revenue = request.Revenue ?? 0;
        if (revenue != project.Revenue)
        {
            var kind = project.Revenue > 0 ? ActivityEventKinds.RevenueChanged : ActivityEventKinds.RevenueSet;
            activity.Add(ProjectEvent(project, kind, actor.UserId, amount: revenue, prevAmount: project.Revenue));
        }

        project.BillingType = request.BillingType;
        project.ClientId = request.ClientId;
        project.Revenue = revenue;
        project.UpdatedAt = clock.UtcNow;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, project.Id, cancellationToken);
    }

    public async Task<ProjectResponse> UpdatePricingAsync(
        long projectId,
        UpdateProjectPricingRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await VisibleProjectForUpdateAsync(actor, projectId, cancellationToken);

        if (project.DomainCode != DomainCodes.Execution)
        {
            throw new BusinessRuleException(ErrorCodes.NotForThisDomain, "Only Execution projects have a project fee and a material markup.");
        }

        Throw(ProjectRules.CheckMoneyEditable(project.RevenueLockedAt is not null));

        if (request.ProjectFee != project.ProjectFee)
        {
            activity.Add(ProjectEvent(project, ActivityEventKinds.ProjectFeeSet, actor.UserId,
                amount: request.ProjectFee, prevAmount: project.ProjectFee));
        }

        if (request.MaterialMarkup != project.MaterialMarkup)
        {
            // A fraction (0.125 is 12.5%), so it goes in the payload: the amount columns keep 2 decimals.
            activity.Add(ProjectEvent(project, ActivityEventKinds.MaterialMarkupSet, actor.UserId,
                new { markup = request.MaterialMarkup, prevMarkup = project.MaterialMarkup }));
        }

        project.ProjectFee = request.ProjectFee;
        project.MaterialMarkup = request.MaterialMarkup;
        project.UpdatedAt = clock.UtcNow;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, project.Id, cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    private async Task<Project> VisibleProjectForUpdateAsync(UserAccess actor, long projectId, CancellationToken cancellationToken)
    {
        var project = await projects.GetForUpdateAsync(projectId, cancellationToken);
        return project is not null && actor.CanSee(project.DomainCode) ? project : throw new NotFoundException(NotFoundMessage);
    }

    private async Task EnsureClientAsync(long? clientId, string domain, CancellationToken cancellationToken)
    {
        if (clientId is not { } id)
        {
            return;
        }

        var row = await clients.GetAsync(id, cancellationToken);
        if (row is null || row.Client.DomainCode != domain)
        {
            throw new RequestValidationException(ErrorCodes.ValidationFailed, "clientId", "The client does not exist in this domain.");
        }

        Throw(ProjectRules.CheckClient(row.Client.ArchivedAt is not null));
    }

    private async Task EnsureOwnerAsync(long ownerId, string domain, CancellationToken cancellationToken)
    {
        var owner = await users.GetRoleSnapshotAsync(ownerId, cancellationToken);
        Throw(ProjectRules.CheckOwner(
            owner is null ? null : new ProjectRules.Person(owner.RoleCode, owner.DomainCode, owner.Status), domain));
    }

    private async Task<ProjectResponse> DescribeAsync(UserAccess actor, long projectId, CancellationToken cancellationToken)
    {
        var p = await projects.GetAsync(projectId, cancellationToken);
        if (p is null || !actor.CanSee(p.DomainCode))
        {
            throw new NotFoundException(NotFoundMessage);
        }

        return new ProjectResponse
        {
            Id = p.Id,
            Domain = p.DomainCode,
            Title = p.Title,
            Description = p.Description,
            Icon = p.Icon,
            Tint = p.Tint,
            Status = p.Status,
            BillingType = p.BillingType,
            Client = ClientOf(p),
            Owner = OwnerOf(p),
            StartDate = p.StartDate,
            TargetDate = p.TargetDate,
            CompletedAt = p.CompletedAt,
            FinalizedAt = p.FinalizedAt,
            ProgressPct = p.ProgressPct,
            CreatedAt = p.CreatedAt,
            Money = actor.Has(PermissionCodes.FinancialView) ? await MoneyOfAsync(p, cancellationToken) : null,
        };
    }

    /// <summary>The same batched load as the lists and the Financials page, so every page shows the same billing.</summary>
    private async Task<ProjectMoneyResponse> MoneyOfAsync(Project p, CancellationToken cancellationToken)
    {
        var rollup = await moneyRollups.LoadAsync([p], cancellationToken);
        var money = rollup.Money(p.Id);
        var summary = money.Summary;
        var costs = money.Costs;
        var expenseTotals = ExpenseRules.Totals(await expenses.AmountsAsync(p.Id, cancellationToken))
            .Select(t => new CurrencyTotalResponse(t.Currency, t.Total))
            .ToList();

        return new ProjectMoneyResponse
        {
            Currency = p.RevenueCurrency,
            Revenue = summary.Revenue,
            IsLocked = p.RevenueLockedAt is not null,
            ProjectFee = p.ProjectFee,
            MaterialMarkup = p.MaterialMarkup,
            MaterialCharge = money.MaterialCharge,
            ExpenseTotals = expenseTotals,
            Billing = rollup.ProjectBillingOf(p),
            Cost = new ProjectCostResponse(
                costs.Labour, costs.Expenses, costs.Materials, costs.GroundLabour, costs.Total,
                costs.Unconverted.Select(u => new UnconvertedCostResponse(u.Source, u.Currency, u.Amount)).ToList(),
                money.UnpricedLabourMinutes),
            Profit = summary.Profit,
            Margin = summary.Margin,
            RecognisedToDate = summary.RecognisedToDate,
            Projected = summary.Projected is { } projected
                ? new ProjectProjectionResponse(projected.Cost, projected.Profit, projected.Margin)
                : null,
            LabourHours = Math.Round(money.LabourMinutes / 60m, 2),
            CostPerHour = summary.CostPerHour,
            CashProfit = money.CashProfit,
            MaterialProfit = money.MaterialProfit,
            UnconvertedMaterials = money.UnconvertedMaterials,
        };
    }

    private ActivityEvent ProjectEvent(
        Project project,
        string kind,
        long actorId,
        object? payload = null,
        decimal? amount = null,
        decimal? prevAmount = null) => new()
    {
        DomainCode = project.DomainCode,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        ProjectId = project.Id,
        SubjectType = "project",
        SubjectId = project.Id,
        Amount = amount,
        PrevAmount = prevAmount,
        Payload = payload is null ? null : JsonSerializer.Serialize(payload),
    };

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The whole year, or one quarter of it.</summary>
    private static (DateOnly From, DateOnly To)? OpenPeriodOf(int? year, int? quarter)
    {
        if (year is not { } y)
        {
            return null;
        }

        var from = new DateOnly(y, quarter is { } q ? (q - 1) * 3 + 1 : 1, 1);
        return (from, from.AddMonths(quarter is null ? 12 : 3).AddDays(-1));
    }

    /// <summary>The page's Design projects' work: one query for totals and contributors, one for the last 8 weeks.</summary>
    private async Task<IReadOnlyDictionary<long, ProjectListWorkResponse>> ListWorkAsync(
        IReadOnlyList<Project> items,
        CancellationToken cancellationToken)
    {
        var ids = items.Where(p => p.DomainCode == DomainCodes.Design).Select(p => p.Id).ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<long, ProjectListWorkResponse>();
        }

        var today = clock.CompanyToday;
        var firstDay = CompanyCalendar.Of(await company.GetSettingsAsync(cancellationToken: cancellationToken)).FirstDayOfWeek;
        var since = WorkStats.WeekStartOf(today, firstDay).AddDays(-7 * (ListSparkWeeks - 1));
        var people = (await entries.ContributionsAsync(ids, cancellationToken)).ToLookup(c => c.ProjectId);
        var recent = (await projects.RecentWorkAsync(ids, clock.CompanyDayStartUtc(since), cancellationToken))
            .Select(r => new WorkStats.Worked(r.Id, r.UserId, r.ProjectId, clock.ToCompanyTime(r.StartedAt), r.Minutes))
            .ToLookup(w => w.ProjectId);

        return ids.ToDictionary(id => id, id =>
        {
            var contributors = people[id].OrderByDescending(c => c.Minutes).ThenBy(c => c.UserId).ToList();
            var worked = recent[id].ToList();
            var last14 = WorkStats.Minutes(worked, today.AddDays(-13), today);
            return new ProjectListWorkResponse(
                contributors.Sum(c => c.Minutes),
                WorkStats.LevelOf(last14),
                last14,
                WorkStats.Weekly(worked, today, ListSparkWeeks, firstDay).Select(w => w.Minutes).ToList(),
                contributors.Count > 0 ? contributors.Max(c => c.LastStartedAt) : null,
                contributors.Take(ListContributors).Select(c => new PersonRefResponse(c.UserId, c.UserName)).ToList(),
                contributors.Count);
        });
    }

    private static ProjectClientResponse? ClientOf(Project p)
        => p.Client is null ? null : new ProjectClientResponse(p.Client.Id, ClientRules.DisplayName(p.Client.Name, p.Client.Organisation));

    private static ProjectOwnerResponse OwnerOf(Project p)
        => new(p.Owner.Id, $"{p.Owner.FirstName} {p.Owner.LastName}".Trim() is { Length: > 0 } name ? name : p.Owner.Email);

    private static ProjectSummaryResponse ToSummary(Project p) => new()
    {
        Id = p.Id,
        Domain = p.DomainCode,
        Title = p.Title,
        Icon = p.Icon,
        Tint = p.Tint,
        Status = p.Status,
        BillingType = p.BillingType,
        Client = ClientOf(p),
        Owner = OwnerOf(p),
        StartDate = p.StartDate,
        TargetDate = p.TargetDate,
        ProgressPct = p.ProgressPct,
    };
}
