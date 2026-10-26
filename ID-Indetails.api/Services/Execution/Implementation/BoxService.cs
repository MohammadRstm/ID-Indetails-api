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

namespace ID_Indetails.api.Services.Execution.Implementation;

public sealed class BoxService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IBoxRepository box,
    IProjectFinanceService finance,
    IUnitOfWork unitOfWork,
    IClock clock) : IBoxService
{
    public async Task<BoxResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        await ActorAsync(cancellationToken);
        var rows = await box.ProjectsAsync(cancellationToken);
        var money = await finance.GetAsync(rows.Select(r => r.Project).ToList(), cancellationToken);
        var all = rows.Select(r => new BoxProjectResponse(
            r.Project.Id,
            r.Project.Title,
            r.ClientName,
            r.Project.Status,
            r.Project.BoxState ?? BoxState.In,
            r.Contributed,
            r.FromBox,
            money[r.Project.Id].CashProfit ?? 0)).ToList();
        var contributing = all.Where(p => p.BoxState != BoxState.Removed).ToList();

        return new BoxResponse(
            await box.BalanceAsync(cancellationToken),
            PaymentRules.BaseCurrency,
            contributing,
            contributing.Sum(p => p.Contributed),
            all.Where(p => p.Status != ProjectStatus.Archived && p.CashProfit < 0).OrderBy(p => p.CashProfit).ToList(),
            all.Where(p => p.BoxState == BoxState.Pending).ToList());
    }

    public async Task<PagedResponse<BoxEntryResponse>> EntriesAsync(BoxEntryListQuery query, CancellationToken cancellationToken = default)
    {
        await ActorAsync(cancellationToken);
        var (items, total) = await box.SearchAsync(query.ProjectId, query.Page, query.PageSize, cancellationToken);
        return new PagedResponse<BoxEntryResponse>(items.Select(ToResponse).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<BoxResponse> TransferAsync(BoxTransferRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var project = await SiteAsync(request.ProjectId, cancellationToken);
            await box.LockAsync(cancellationToken);
            var cashProfit = (await finance.GetAsync(project, cancellationToken)).CashProfit ?? 0;
            Throw(BoxRules.CheckTransfer(request.Amount, await box.BalanceAsync(cancellationToken), cashProfit));

            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            box.Add(Entry(BoxEntryKind.Transfer, project.Id, -request.Amount, actor.UserId, note: note));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await GetAsync(cancellationToken);
    }

    public async Task<BoxResponse> DecideAsync(long projectId, BoxDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var project = await SiteAsync(projectId, cancellationToken, forUpdate: true);
            Throw(BoxRules.CheckDecision(project.BoxState));
            Throw(BoxRules.CheckDecisionChoice(request.Decision));
            if (request.Decision == BoxChoice.Remove)
            {
                await RemoveAsync(project, actor.UserId, cancellationToken);
            }
            else
            {
                project.BoxState = BoxState.In;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await GetAsync(cancellationToken);
    }

    public void PaymentReceived(Project project, Payment payment, long actorId)
    {
        if (project.DomainCode == DomainCodes.Execution && BoxRules.TakesPayments(project.BoxState))
        {
            box.Add(Entry(BoxEntryKind.PaymentIn, project.Id, payment.AmountUsd, actorId, payment.Id));
        }
    }

    public async Task PaymentVoidedAsync(Payment payment, long actorId, CancellationToken cancellationToken = default)
    {
        // a removed project's money already left with its removal
        var project = await projects.GetAsync(payment.ProjectId, cancellationToken);
        if (!BoxRules.TakesPayments(project?.BoxState) || !await box.HoldsPaymentAsync(payment.Id, cancellationToken))
        {
            return;
        }

        box.Add(Entry(BoxEntryKind.PaymentVoided, payment.ProjectId, -payment.AmountUsd, actorId, payment.Id));
    }

    public async Task ClosedAsync(Project project, BoxChoice? choice, UserAccess actor, CancellationToken cancellationToken = default)
    {
        if (project.DomainCode != DomainCodes.Execution)
        {
            return;
        }

        var (state, denial) = BoxRules.OnClose(choice, SeesMoney(actor));
        Throw(denial);
        if (state == BoxState.Removed)
        {
            await RemoveAsync(project, actor.UserId, cancellationToken);
            return;
        }

        project.BoxState = state;
    }

    public async Task ReopenedAsync(Project project, BoxChoice? choice, UserAccess actor, CancellationToken cancellationToken = default)
    {
        if (project.DomainCode != DomainCodes.Execution)
        {
            return;
        }

        var (readd, denial) = BoxRules.OnReopen(project.BoxState, choice, SeesMoney(actor));
        Throw(denial);
        if (readd && await box.LastRemovalAsync(project.Id, cancellationToken) is { Amount: < 0 } removal)
        {
            box.Add(Entry(BoxEntryKind.Readded, project.Id, -removal.Amount, actor.UserId));
        }

        project.BoxState = BoxState.In;
    }

    /// <summary>
    /// Takes out what the project put in, as far as the box holds it. Recorded even when nothing is left to take, so a
    /// reopening puts back what this removal took and not an older one's.
    /// </summary>
    private async Task RemoveAsync(Project project, long actorId, CancellationToken cancellationToken)
    {
        await box.LockAsync(cancellationToken);
        var amount = BoxRules.Removal(
            await box.ContributedAsync(project.Id, cancellationToken),
            await box.BalanceAsync(cancellationToken));
        box.Add(Entry(BoxEntryKind.Removed, project.Id, amount, actorId));
        project.BoxState = BoxState.Removed;
    }

    private async Task<Project> SiteAsync(long projectId, CancellationToken cancellationToken, bool forUpdate = false)
    {
        var project = forUpdate
            ? await projects.GetForUpdateAsync(projectId, cancellationToken)
            : await projects.GetAsync(projectId, cancellationToken);
        return project is { DomainCode: DomainCodes.Execution }
            ? project
            : throw new NotFoundException("Project not found.");
    }

    private BoxEntry Entry(BoxEntryKind kind, long projectId, decimal amount, long actorId, long? paymentId = null, string? note = null) => new()
    {
        Kind = kind,
        ProjectId = projectId,
        PaymentId = paymentId,
        Amount = amount,
        Note = note,
        CreatedAt = clock.UtcNow,
        CreatedById = actorId,
    };

    private static BoxEntryResponse ToResponse(BoxEntryRow r) => new(
        r.Entry.Id,
        r.Entry.Kind,
        r.Entry.Amount,
        new BoxEntryProjectResponse(r.Entry.ProjectId, r.ProjectTitle),
        r.Entry.Note,
        r.Entry.CreatedAt,
        r.Entry.CreatedById is { } id && r.CreatedByName is { } name ? new PersonRefResponse(id, name) : null);

    private static bool SeesMoney(UserAccess actor) => actor.Has(PermissionCodes.FinancialView);

    /// <summary>The box is Execution's money: whoever sees money in that domain.</summary>
    private async Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
    {
        var actor = (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);
        return actor.CanSee(DomainCodes.Execution) ? actor : throw new NotFoundException("The Execution box isn't in your domains.");
    }

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
