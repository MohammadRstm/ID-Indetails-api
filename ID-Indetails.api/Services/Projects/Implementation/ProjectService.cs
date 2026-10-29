using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Clients;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Business.Expenses;
using ID_Indetails.api.Business.Finance;
using ID_Indetails.api.Business.Execution;
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
using ID_Indetails.api.Repositories.Execution.Abstraction;
using ID_Indetails.api.Repositories.Expenses.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Repositories.Reports.Abstraction;
using ID_Indetails.api.Repositories.WorkEntries.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Billing.Abstraction;
using ID_Indetails.api.Services.Portal.Abstraction;
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
    IMoneyRollupService moneyRollups,
    IReportRepository reports,
    IActivityEventRepository activity,
    IProjectNotifier notifier,
    IPortalAccessService portalAccess,
    IPortalPublishingService portalPublishing,
    ISiteTeamRepository siteTeams,
    IUnitOfWork unitOfWork,
    IProfitFloor floor,
    IClock clock) : IProjectService
{
    private const string NotFoundMessage = "Project not found.";
    public const int ListSparkWeeks = 8;
    public const int ListContributors = 4;
    public const int SiteTeamShown = 5;

    public async Task<PagedResponse<ProjectSummaryResponse>> ListAsync(ProjectListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        if (query.Sort == ProjectSort.Hours && !actor.Has(PermissionCodes.WorkEntryViewAll))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Sorting projects by hours needs work_entry:view_all.");
        }

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
        var teams = await SiteTeamsAsync(items, cancellationToken);
        return new PagedResponse<ProjectSummaryResponse>(
            items.Select(p => ToSummary(p) with
            {
                Money = rollup?.ListMoney(p),
                Work = work?.GetValueOrDefault(p.Id),
                Team = teams.GetValueOrDefault(p.Id),
            }).ToList(),
            query.Page,
            query.PageSize,
            total);
    }

    public async Task<ProjectListTotalsResponse> TotalsAsync(ProjectListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialView);
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

    /// <summary>Each Execution site's team (Design projects have none), in one load for the whole page.</summary>
    private async Task<IReadOnlyDictionary<long, SiteTeamResponse>> SiteTeamsAsync(IReadOnlyCollection<Project> items, CancellationToken cancellationToken)
    {
        var sites = items.Where(p => p.DomainCode == DomainCodes.Execution).Select(p => p.Id).ToList();
        if (sites.Count == 0)
        {
            return new Dictionary<long, SiteTeamResponse>();
        }

        var staff = (await siteTeams.StaffAsync(sites, cancellationToken)).ToLookup(r => r.ProjectId);
        return sites.ToDictionary(id => id, id =>
        {
            var people = staff[id].ToList();
            return new SiteTeamResponse(people.Take(SiteTeamShown).Select(r => new PersonRefResponse(r.UserId, r.Name)).ToList(), people.Count);
        });
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
        var domain = request.DomainCode ?? (actor.VisibleBusinesses.Count == 1 ? actor.VisibleBusinesses[0] : null)
            ?? throw new RequestValidationException(ErrorCodes.ValidationFailed, "domainCode", "Choose the domain the project belongs to.");

        if (!actor.CanSee(domain))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "You can only create projects in a domain you work in.");
        }

        Throw(ProjectRules.CheckSetup(new ProjectRules.Setup(
            domain, request.BillingType, request.ClientId, request.Revenue, request.MaterialMarkup, request.SupervisionRate)));
        if ((request.Revenue is not null || request.TvaRate is not null) && !actor.Has(PermissionCodes.FinancialView))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Setting the customer payment or its TVA needs the permission to see project money.");
        }

        if ((request.MaterialMarkup is not null || request.SupervisionRate is not null) && !actor.Has(PermissionCodes.ProjectPricingManage))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Setting a site's material markup and supervision needs the permission to manage pricing.");
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

        if (domain == DomainCodes.Execution)
        {
            var chosen = request.StaffIds ?? [];
            Throw(SiteTeamRules.CheckStaff(chosen, await siteTeams.CandidatesAsync(chosen, cancellationToken)));
        }
        else if (request.StaffIds is { Count: > 0 })
        {
            throw new RequestValidationException(ErrorCodes.ValidationFailed, "staffIds", "Only Execution sites have staff overseeing them.");
        }

        await EnsureClientAsync(request.ClientId, domain, cancellationToken);
        var ownerId = request.OwnerId ?? actor.UserId;
        await EnsureOwnerAsync(actor, ownerId, domain, cancellationToken);

        var now = clock.UtcNow;
        var project = new Project
        {
            DomainCode = domain,
            Title = request.Title.Trim(),
            Description = Clean(request.Description),
            Location = Clean(request.Location),
            Icon = Clean(request.Icon),
            Tint = Clean(request.Tint),
            Status = ProjectStatus.Active,
            BillingType = request.BillingType,
            ClientId = request.ClientId,
            OwnerId = ownerId,
            StartDate = clock.CompanyToday,
            TargetDate = request.TargetDate,
            Revenue = request.Revenue ?? 0,
            // A site starts with no work price: agreeing a BOQ sets it. Created without pricing, it starts at no markup.
            ProjectFee = domain == DomainCodes.Execution ? 0 : null,
            MaterialMarkup = domain == DomainCodes.Execution ? request.MaterialMarkup ?? 0 : null,
            SupervisionRate = domain == DomainCodes.Execution ? request.SupervisionRate ?? ExecutionPricing.DefaultSupervisionRate : null,
            // TVA is on what a client pays: internal work has none
            TvaRate = request.BillingType == BillingType.Client ? request.TvaRate ?? Tva.DefaultRate : 0,
            CreatedAt = now,
            CreatedById = actor.UserId,
        };

        foreach (var staffId in (request.StaffIds ?? []).Where(_ => domain == DomainCodes.Execution))
        {
            project.Staff.Add(new ProjectStaff { UserId = staffId, AssignedAt = now, AssignedById = actor.UserId });
        }

        if (project.BillingType == BillingType.Client)
        {
            project.PaymentChanges.Add(PaymentChange(
                domain == DomainCodes.Execution ? project.ProjectFee ?? 0 : project.Revenue, null,
                project.MaterialMarkup, null, project.TvaRate, null, ProjectRules.AgreedAtStart, actor.UserId, project.SupervisionRate));
        }

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
        if (project.BillingType == BillingType.Client && domain != DomainCodes.Execution && !actor.Has(PermissionCodes.InvoiceSend))
        {
            // whoever sends quotes decides whether this one gets one (Execution has none); the creator can't
            await notifier.QuoteDueAsync(project, actor.UserId, cancellationToken);
        }
        if (project.Revenue > 0)
        {
            activity.Add(ProjectEvent(project, ActivityEventKinds.RevenueSet, actor.UserId, amount: project.Revenue));
        }

        if (request.LabourDefaults is { } defaults)
        {
            activity.Add(ProjectEvent(project, ActivityEventKinds.LabourDefaultsSet, actor.UserId,
                new { workers = defaults.Workers, previousWorkers = (int?)null, effectiveFrom = project.StartDate }, amount: defaults.DailyPay));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
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
            await EnsureOwnerAsync(actor, request.OwnerId, project.DomainCode, cancellationToken);
            activity.Add(ProjectEvent(project, ActivityEventKinds.ProjectOwnerSet, actor.UserId,
                new { ownerId = request.OwnerId, prevOwnerId = project.OwnerId }));
            project.OwnerId = request.OwnerId;
            await notifier.OwnerSetAsync(project, actor.UserId, cancellationToken);
        }

        project.Title = request.Title.Trim();
        project.Description = Clean(request.Description);
        project.Location = Clean(request.Location);
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
        // Replaces the revenue, so only someone who can see it may send it.
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialView);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await VisibleProjectForUpdateAsync(actor, projectId, cancellationToken);

        if (project.DomainCode == DomainCodes.Execution)
        {
            throw new BusinessRuleException(
                ErrorCodes.NotForThisDomain, "Execution projects are always client work; change their fee and markup instead.");
        }

        Throw(ProjectRules.CheckMoneyEditable(project.RevenueLockedAt is not null));
        // internal work has no payment; client work keeps the one it has, which is changed on its own
        var revenue = request.BillingType == BillingType.Internal ? 0 : project.Revenue;
        Throw(ProjectRules.CheckSetup(new ProjectRules.Setup(
            project.DomainCode, request.BillingType, request.ClientId, revenue, null, null)));
        Throw(ProjectRules.CheckClientChange(
            project.ClientId, request.ClientId, await projects.HasBillingRecordsAsync(project.Id, cancellationToken)));

        if (request.ClientId != project.ClientId)
        {
            await EnsureClientAsync(request.ClientId, project.DomainCode, cancellationToken);
            activity.Add(ProjectEvent(project, ActivityEventKinds.ProjectClientSet, actor.UserId,
                new { clientId = request.ClientId, prevClientId = project.ClientId }));

            // The previous client's contacts no longer follow the project in the client portal, and whoever follows it
            // next sees nothing published for them nor what was said with them.
            if (project.ClientId is not null)
            {
                await portalAccess.EndAllForProjectAsync(project.Id, project.DomainCode, project.ClientId, actor.UserId, cancellationToken);
                await portalPublishing.WithdrawForNewClientAsync(project.Id, clock.UtcNow, cancellationToken);
            }
        }

        if (revenue != project.Revenue)
        {
            activity.Add(ProjectEvent(project, ActivityEventKinds.RevenueChanged, actor.UserId, amount: revenue, prevAmount: project.Revenue));
            project.PaymentChanges.Add(PaymentChange(revenue, project.Revenue, null, null, 0, project.TvaRate, ProjectRules.MadeInternal, actor.UserId));
        }

        project.BillingType = request.BillingType;
        project.ClientId = request.ClientId;
        project.Revenue = revenue;
        if (request.BillingType == BillingType.Internal)
        {
            project.TvaRate = 0;
        }
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
            throw new BusinessRuleException(ErrorCodes.NotForThisDomain, "Only Execution projects have a material markup and a supervision rate.");
        }

        Throw(ProjectRules.CheckMoneyEditable(project.RevenueLockedAt is not null));
        var tva = request.TvaRate ?? project.TvaRate;
        var supervision = request.SupervisionRate ?? project.SupervisionRate;
        Throw(ProjectRules.CheckPaymentChanges(
            request.MaterialMarkup != project.MaterialMarkup || supervision != project.SupervisionRate || tva != project.TvaRate, "materialMarkup"));
        var before = await floor.BeforeAsync(project, cancellationToken);
        project.PaymentChanges.Add(PaymentChange(project.ProjectFee ?? 0, project.ProjectFee ?? 0, request.MaterialMarkup, project.MaterialMarkup,
            tva, project.TvaRate, request.Description.Trim(), actor.UserId, supervision, project.SupervisionRate));

        // A fraction (0.125 is 12.5%) goes in the payload: the amount columns keep 2 decimals.
        if (request.MaterialMarkup != project.MaterialMarkup)
        {
            activity.Add(ProjectEvent(project, ActivityEventKinds.MaterialMarkupSet, actor.UserId,
                new { markup = request.MaterialMarkup, prevMarkup = project.MaterialMarkup }));
        }

        if (supervision != project.SupervisionRate)
        {
            activity.Add(ProjectEvent(project, ActivityEventKinds.SupervisionRateSet, actor.UserId,
                new { rate = supervision, prevRate = project.SupervisionRate }));
        }

        project.MaterialMarkup = request.MaterialMarkup;
        project.SupervisionRate = supervision;
        project.TvaRate = tva;
        project.UpdatedAt = clock.UtcNow;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await floor.CheckAsync(before, BlockedChangeKind.Pricing, new BlockedRequest<UpdateProjectPricingRequest>(project.Id, request), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, project.Id, cancellationToken);
    }

    public async Task<ProjectResponse> UpdatePaymentAsync(
        long projectId,
        UpdateProjectPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialView);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await VisibleProjectForUpdateAsync(actor, projectId, cancellationToken);

        if (project.DomainCode == DomainCodes.Execution)
        {
            throw new BusinessRuleException(
                ErrorCodes.NotForThisDomain, "An Execution project's payment is its fee and material markup; change those instead.");
        }

        Throw(ProjectRules.CheckMoneyEditable(project.RevenueLockedAt is not null));
        Throw(ProjectRules.CheckHasPayment(project.BillingType));
        var tva = request.TvaRate ?? project.TvaRate;
        Throw(ProjectRules.CheckPaymentChanges(request.Revenue != project.Revenue || tva != project.TvaRate, "revenue"));
        var before = await floor.BeforeAsync(project, cancellationToken);

        if (request.Revenue != project.Revenue)
        {
            var kind = project.Revenue > 0 ? ActivityEventKinds.RevenueChanged : ActivityEventKinds.RevenueSet;
            activity.Add(ProjectEvent(project, kind, actor.UserId, amount: request.Revenue, prevAmount: project.Revenue));
        }

        project.PaymentChanges.Add(PaymentChange(request.Revenue, project.Revenue, null, null, tva, project.TvaRate, request.Description.Trim(), actor.UserId));
        project.Revenue = request.Revenue;
        project.TvaRate = tva;
        project.UpdatedAt = clock.UtcNow;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await floor.CheckAsync(before, BlockedChangeKind.Payment, new BlockedRequest<UpdateProjectPaymentRequest>(project.Id, request), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, project.Id, cancellationToken);
    }

    public async Task<ProjectResponse> UpdateStaffAsync(long projectId, UpdateProjectStaffRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await VisibleProjectForUpdateAsync(actor, projectId, cancellationToken);
        if (project.DomainCode != DomainCodes.Execution)
        {
            throw new BusinessRuleException(ErrorCodes.NotForThisDomain, "Only Execution sites have staff overseeing them.");
        }

        Throw(SiteTeamRules.CheckStaff(request.StaffIds, await siteTeams.CandidatesAsync(request.StaffIds, cancellationToken)));

        // taken off keeps the row, so their past records still say who they were; put back reuses it
        var now = clock.UtcNow;
        var rows = await siteTeams.StaffForUpdateAsync(project.Id, cancellationToken);
        foreach (var row in rows)
        {
            var stays = request.StaffIds.Contains(row.UserId);
            if (stays && row.RemovedAt is not null)
            {
                row.RemovedAt = null;
                row.AssignedAt = now;
                row.AssignedById = actor.UserId;
            }
            else if (!stays && row.RemovedAt is null)
            {
                row.RemovedAt = now;
            }
        }

        foreach (var userId in request.StaffIds.Where(id => rows.All(r => r.UserId != id)))
        {
            siteTeams.AddStaff(new ProjectStaff { ProjectId = project.Id, UserId = userId, AssignedAt = now, AssignedById = actor.UserId });
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(actor, project.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectPaymentChangeResponse>> PaymentChangesAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialView);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException(NotFoundMessage);
        }

        return (await projects.PaymentChangesAsync(projectId, cancellationToken))
            .Select(r => new ProjectPaymentChangeResponse(
                r.Change.Id, r.Change.Amount, r.Change.PreviousAmount, r.Change.MaterialMarkup, r.Change.PreviousMaterialMarkup,
                r.Change.Description, r.Change.ChangedAt,
                r.Change.ChangedById is { } id && r.ChangedByName is { } name ? new ProjectOwnerResponse(id, name) : null,
                r.Change.TvaRate, r.Change.PreviousTvaRate, r.Change.SupervisionRate, r.Change.PreviousSupervisionRate))
            .ToList();
    }

    private ProjectPaymentChange PaymentChange(
        decimal amount, decimal? previousAmount, decimal? markup, decimal? previousMarkup, decimal tva, decimal? previousTva, string description, long actorId,
        decimal? supervision = null, decimal? previousSupervision = null) => new()
    {
        Amount = amount,
        PreviousAmount = previousAmount,
        MaterialMarkup = markup,
        PreviousMaterialMarkup = previousMarkup,
        TvaRate = tva,
        PreviousTvaRate = previousTva,
        SupervisionRate = supervision,
        PreviousSupervisionRate = previousSupervision,
        Description = description,
        ChangedAt = clock.UtcNow,
        ChangedById = actorId,
    };

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

    /// <summary>Someone the caller can't see (a super admin, for a caller in one domain) reads as no one, so ids can't be probed.</summary>
    private async Task EnsureOwnerAsync(UserAccess actor, long ownerId, string domain, CancellationToken cancellationToken)
    {
        var owner = await users.GetRoleSnapshotAsync(ownerId, cancellationToken);
        var visible = owner is not null && (owner.Id == actor.UserId || EmployeeRules.CanSee(actor, owner.DomainCode));
        Throw(ProjectRules.CheckOwner(visible ? new ProjectRules.Person(owner!.RoleCode, owner.DomainCode, owner.Status) : null, domain));
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
            Location = p.Location,
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
            HeldSince = p.Status == ProjectStatus.Hold && (await reports.HeldSinceAsync([p.Id], cancellationToken)).TryGetValue(p.Id, out var held)
                ? held
                : null,
            Team = (await SiteTeamsAsync([p], cancellationToken)).GetValueOrDefault(p.Id),
            Staff = p.DomainCode == DomainCodes.Execution
                ? (await siteTeams.StaffAsync([p.Id], cancellationToken)).Select(s => new PersonRefResponse(s.UserId, s.Name)).ToList()
                : null,
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
            TvaRate = p.TvaRate,
            SupervisionRate = p.SupervisionRate,
            SupervisionUsd = money.Supervision,
            PaymentWithTva = p.DomainCode == DomainCodes.Execution
                ? ExecutionPricing.PriceOf(p.ProjectFee ?? 0, p.SupervisionRate ?? 0, p.TvaRate).Gross
                : Tva.Gross(p.Revenue, p.TvaRate),
            RevenueWithTva = money.RevenueWithTva,
            ProjectFee = p.ProjectFee,
            MaterialMarkup = p.MaterialMarkup,
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
            ToPocket = p.DomainCode == DomainCodes.Execution ? money.ToPocket : null,
            FromPocket = p.DomainCode == DomainCodes.Execution ? money.FromPocket : null,
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
        Description = p.Description,
        Location = p.Location,
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
