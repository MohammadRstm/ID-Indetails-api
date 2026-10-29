using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Execution.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Execution.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;

namespace ID_Indetails.api.Services.Execution.Implementation;

public sealed class PocketService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IPocketRepository pocket,
    IProjectFinanceService finance,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IPocketService
{
    public async Task<PocketResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        await ActorAsync(cancellationToken);
        var pending = await pocket.PendingAsync(cancellationToken);
        var sites = new List<Project>();
        foreach (var id in pending.Select(r => r.Request.ProjectId).Distinct())
        {
            if (await projects.GetAsync(id, cancellationToken) is { } site)
            {
                sites.Add(site);
            }
        }

        var money = await finance.GetAsync(sites, cancellationToken);
        var (contributed, paidOut) = await pocket.TotalsAsync(cancellationToken);
        return new PocketResponse(
            await pocket.BalanceAsync(cancellationToken),
            PaymentRules.BaseCurrency,
            contributed,
            paidOut,
            pending.Select(r => ToResponse(r, money.GetValueOrDefault(r.Request.ProjectId))).ToList());
    }

    public async Task<PagedResponse<PocketEntryResponse>> EntriesAsync(PocketEntryListQuery query, CancellationToken cancellationToken = default)
    {
        await ActorAsync(cancellationToken);
        var (items, total) = await pocket.SearchAsync(new PocketEntrySearch(
            query.Kind,
            query.ProjectId,
            query.From is { } from ? clock.CompanyDayStartUtc(from) : null,
            query.To is { } to ? clock.CompanyDayStartUtc(to.AddDays(1)) : null,
            query.Search?.Trim(),
            query.Page,
            query.PageSize), cancellationToken);
        return new PagedResponse<PocketEntryResponse>(items.Select(ToResponse).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<PocketResponse> ContributeAsync(PocketContributionRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            // the site stays locked while its profit is checked, so a cost added meanwhile can't slip under it
            var site = await SiteAsync(request.ProjectId, forUpdate: true, cancellationToken);
            var money = await finance.GetAsync(site, cancellationToken);
            Throw(PocketRules.CheckContribution(request.Amount, money.Summary.Profit));

            pocket.Add(new PocketEntry
            {
                Kind = PocketEntryKind.Contribution,
                ProjectId = site.Id,
                Amount = request.Amount,
                Note = Clean(request.Note),
                CreatedAt = clock.UtcNow,
                CreatedById = actor.UserId,
            });
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await GetAsync(cancellationToken);
    }

    public async Task<PocketRequestResponse> RequestAsync(CreatePocketRequestRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        long id;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var site = await SiteAsync(request.ProjectId, forUpdate: true, cancellationToken);
            id = (await pocket.PendingOfAsync(site.Id, cancellationToken))?.Id
                ?? await AddRequestAsync(site, actor.UserId, Clean(request.Note), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(id, cancellationToken);
    }

    public async Task RaiseAsync(Project site, string? note, CancellationToken cancellationToken = default)
    {
        if (await pocket.PendingOfAsync(site.Id, cancellationToken) is null)
        {
            await AddRequestAsync(site, null, Clean(note), cancellationToken);
        }
    }

    public async Task<PocketRequestResponse> ApproveAsync(long requestId, ApprovePocketRequestRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var pending = await PendingRequestAsync(requestId, cancellationToken);
            await pocket.LockAsync(cancellationToken);
            Throw(PocketRules.CheckBalance(request.Amount, await pocket.BalanceAsync(cancellationToken)));

            var now = clock.UtcNow;
            pocket.Add(new PocketEntry
            {
                Kind = PocketEntryKind.Payout,
                ProjectId = pending.ProjectId,
                Amount = -request.Amount,
                RequestId = pending.Id,
                Note = Clean(request.Note),
                CreatedAt = now,
                CreatedById = actor.UserId,
            });
            Decide(pending, PocketRequestStatus.Approved, request.Amount, request.Note, actor.UserId, now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(requestId, cancellationToken);
    }

    public async Task<PocketRequestResponse> DenyAsync(long requestId, DenyPocketRequestRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var pending = await PendingRequestAsync(requestId, cancellationToken);
            Decide(pending, PocketRequestStatus.Denied, null, request.Note, actor.UserId, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(requestId, cancellationToken);
    }

    private async Task<long> AddRequestAsync(Project site, long? actorId, string? note, CancellationToken cancellationToken)
    {
        var money = await finance.GetAsync(site, cancellationToken);
        var request = new PocketRequest
        {
            ProjectId = site.Id,
            Status = PocketRequestStatus.Pending,
            SuggestedAmount = PocketRules.Suggest(money.Summary.Revenue, money.Summary.Profit),
            Note = note,
            RequestedAt = clock.UtcNow,
            RequestedById = actorId,
        };
        pocket.Add(request);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // whoever sees Execution's money decides; the payload never holds amounts
        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.PocketRequested, actorId, DomainCodes.Execution, site.Id, "pocket_request", request.Id, null),
            await notifications.HoldersAsync(DomainCodes.Execution, PermissionCodes.FinancialView, cancellationToken),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return request.Id;
    }

    private async Task<PocketRequest> PendingRequestAsync(long requestId, CancellationToken cancellationToken)
    {
        var request = await pocket.GetRequestForUpdateAsync(requestId, cancellationToken)
            ?? throw new NotFoundException("Request not found.");
        Throw(PocketRules.CheckPending(request.Status));
        return request;
    }

    private static void Decide(PocketRequest request, PocketRequestStatus status, decimal? amount, string? note, long actorId, DateTime at)
    {
        request.Status = status;
        request.ApprovedAmount = amount;
        request.DecisionNote = Clean(note);
        request.DecidedById = actorId;
        request.DecidedAt = at;
    }

    private async Task<PocketRequestResponse> DescribeAsync(long requestId, CancellationToken cancellationToken)
    {
        var row = await pocket.GetRequestAsync(requestId, cancellationToken) ?? throw new NotFoundException("Request not found.");
        var site = row.Request.Status == PocketRequestStatus.Pending ? await projects.GetAsync(row.Request.ProjectId, cancellationToken) : null;
        return ToResponse(row, site is null ? null : await finance.GetAsync(site, cancellationToken));
    }

    private async Task<Project> SiteAsync(long projectId, bool forUpdate, CancellationToken cancellationToken)
    {
        var site = (forUpdate
            ? await projects.GetForUpdateAsync(projectId, cancellationToken)
            : await projects.GetAsync(projectId, cancellationToken)) ?? throw new NotFoundException("Project not found.");
        if (site.DomainCode != DomainCodes.Execution)
        {
            throw new NotFoundException("Project not found.");
        }

        Throw(PocketRules.CheckSite(site.DomainCode, site.BillingType));
        return site;
    }

    private static PocketRequestResponse ToResponse(PocketRequestRow row, ProjectFinancials? money)
    {
        var r = row.Request;
        var live = r.Status == PocketRequestStatus.Pending ? money : null;
        return new PocketRequestResponse(
            r.Id,
            new PocketProjectResponse(r.ProjectId, row.ProjectTitle),
            r.Status,
            r.SuggestedAmount,
            live is null ? null : PocketRules.Suggest(live.Summary.Revenue, live.Summary.Profit),
            live?.CashProfit,
            r.ApprovedAmount,
            r.Note,
            r.RequestedAt,
            r.RequestedById is { } by && row.RequestedByName is { } byName ? new PersonRefResponse(by, byName) : null,
            r.DecidedAt,
            r.DecidedById is { } decider && row.DecidedByName is { } deciderName ? new PersonRefResponse(decider, deciderName) : null,
            r.DecisionNote);
    }

    private static PocketEntryResponse ToResponse(PocketEntryRow row) => new(
        row.Entry.Id,
        row.Entry.Kind,
        row.Entry.Amount,
        new PocketProjectResponse(row.Entry.ProjectId, row.ProjectTitle),
        row.Entry.RequestId,
        row.Entry.Note,
        row.Entry.CreatedAt,
        new PersonRefResponse(row.Entry.CreatedById, row.CreatedByName));

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>The pocket is Execution's money: whoever sees money in that domain.</summary>
    private async Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
        return actor.CanSee(DomainCodes.Execution) ? actor : throw new NotFoundException("The pocket money isn't in your domains.");
    }

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
