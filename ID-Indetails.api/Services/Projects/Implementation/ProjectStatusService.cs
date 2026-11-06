using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Billing;
using ID_Indetails.api.Business.Boqs;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Repositories.Requirements.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Boqs.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Services.Projects.Implementation;

/// <summary>Status changes, following <see cref="ProjectStatusRules"/>.</summary>
public sealed class ProjectStatusService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IRequirementRepository requirements,
    IProjectProgress progress,
    IProjectService projectService,
    IProjectFinanceService finance,
    IBoqService boqs,
    IActivityEventRepository activity,
    IProjectNotifier notifier,
    IUnitOfWork unitOfWork,
    IClock clock) : IProjectStatusService
{
    public async Task<ProjectResponse> ChangeAsync(
        long projectId,
        ProjectStatusRules.Action action,
        CancellationToken cancellationToken = default,
        bool keepPaidAhead = false,
        OwedSettlement? settleOwed = null)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var project = await projects.GetForUpdateAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }

        var (transition, denial) = ProjectStatusRules.Plan(project.Status, action);
        if (denial is not null)
        {
            throw denial.ToException();
        }

        var now = clock.UtcNow;
        project.Status = transition!.To;
        project.UpdatedAt = now;
        activity.Add(Event(project, transition.EventKind, actor.UserId, null));

        var completes = transition.Finalises && action == ProjectStatusRules.Action.Complete;
        if (completes && keepPaidAhead && project.BillingType == BillingType.Client)
        {
            await KeepPaidAheadAsync(project, actor.UserId, cancellationToken);
        }

        if (completes && settleOwed is { } settlement)
        {
            await SettleOwedAsync(project, actor, settlement, cancellationToken);
        }

        if (transition.Finalises)
        {
            await FinaliseAsync(project, actor.UserId, now, cancellationToken);
            await notifier.ClosedAsync(project, (await finance.GetAsync(project, cancellationToken)).Summary.Margin, actor.UserId, cancellationToken);
        }

        if (transition.Reopens)
        {
            project.CompletedAt = null;
            project.FinalizedAt = null;
            project.RevenueLockedAt = null;
            if (project.DomainCode == DomainCodes.Execution)
            {
                // Open Execution projects calculate their agreed amount live again.
                project.Revenue = 0;
            }

            await requirements.UntickAutoDoneAsync(project.Id, cancellationToken);
        }

        await progress.RefreshAsync(project, actor.UserId, cancellationToken);
        await notifier.StatusChangedAsync(project, action, actor.UserId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await projectService.GetAsync(project.Id, cancellationToken);
    }

    /// <summary>
    /// What a client paid beyond the price becomes part of it, so the project closes on what it really earned.
    /// </summary>
    private async Task KeepPaidAheadAsync(Project project, long actorId, CancellationToken cancellationToken)
    {
        var money = await finance.GetAsync(project, cancellationToken);
        if (money.Received <= money.RevenueWithTva)
        {
            return;
        }

        await PriceAtReceivedAsync(project, money, actorId, BoqRules.ClosingTitle, BoqRules.ClosingReason, cancellationToken);
    }

    /// <summary>
    /// What the client still owes, all of it, excused (a discount agreed with them) or written off (revenue lost): the
    /// price drops to what was received, so they owe nothing more, and the project keeps what was settled and how. Only for
    /// whoever changes the project's money.
    /// </summary>
    private async Task SettleOwedAsync(Project project, UserAccess actor, OwedSettlement settlement, CancellationToken cancellationToken)
    {
        var moneyWriter = project.DomainCode == DomainCodes.Execution ? PermissionCodes.ProjectPricingManage : PermissionCodes.FinancialWrite;
        if (!actor.Has(moneyWriter))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Only whoever changes this project's money can excuse or write off what its client owes.");
        }

        var money = await finance.GetAsync(project, cancellationToken);
        var owed = project.BillingType == BillingType.Client ? money.RevenueWithTva - money.Received : 0m;
        if (owed <= 0)
        {
            throw new BusinessRuleException(ErrorCodes.NothingOwed, "The client owes nothing on this project.");
        }

        var (title, reason, kind) = settlement == OwedSettlement.Excused
            ? (BoqRules.ExcusedTitle, BoqRules.ExcusedReason, ActivityEventKinds.OwedExcused)
            : (BoqRules.WrittenOffTitle, BoqRules.WrittenOffReason, ActivityEventKinds.OwedWrittenOff);
        await PriceAtReceivedAsync(project, money, actor.UserId, title, reason, cancellationToken);
        project.SettledOwedUsd = owed;
        project.SettledOwedAs = settlement;
        activity.Add(Event(project, kind, actor.UserId, owed));
    }

    /// <summary>
    /// The price set to what the client paid, TVA included: a site gets a closing VO, a Design project a new payment,
    /// each written to the payment history like any price change.
    /// </summary>
    private async Task PriceAtReceivedAsync(Project project, ProjectFinancials money, long actorId, string title, string reason, CancellationToken cancellationToken)
    {
        if (project.DomainCode == DomainCodes.Execution)
        {
            var work = ExecutionPricing.WorkFor(money.Received, project.PermitsPrice ?? 0, project.SupervisionRate ?? 0, project.TvaRate);
            if (work < 0)
            {
                throw new BusinessRuleException(
                    ErrorCodes.OwedBeyondWork, "The client paid less than the permits alone, so no work price makes what they paid the price.");
            }

            await boqs.AddClosingPriceAsync(project, work, title, reason, actorId, cancellationToken);
            return;
        }

        var revenue = Tva.NetFor(money.Received, project.TvaRate, money.UntaxedUsd);
        var now = clock.UtcNow;
        project.PaymentChanges.Add(new ProjectPaymentChange
        {
            Amount = revenue,
            PreviousAmount = project.Revenue,
            TvaRate = project.TvaRate,
            PreviousTvaRate = project.TvaRate,
            Description = reason,
            ChangedAt = now,
            ChangedById = actorId,
        });
        var changed = Event(project, project.Revenue > 0 ? ActivityEventKinds.RevenueChanged : ActivityEventKinds.RevenueSet, actorId, revenue);
        changed.PrevAmount = project.Revenue;
        activity.Add(changed);
        project.Revenue = revenue;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Locks revenue (freezing Execution's agreed amount) and ticks open requirements as done automatically.</summary>
    private async Task FinaliseAsync(Project project, long actorId, DateTime now, CancellationToken cancellationToken)
    {
        if (project.DomainCode == DomainCodes.Execution)
        {
            // The agreed amount, the work price plus supervision, is frozen here.
            var money = await finance.GetAsync(project, cancellationToken);
            project.Revenue = money.Summary.Revenue;
        }

        project.CompletedAt = now;
        project.FinalizedAt = now;
        project.RevenueLockedAt = now;
        await requirements.AutoTickOpenAsync(project.Id, now, actorId, cancellationToken);
        activity.Add(Event(project, ActivityEventKinds.ProjectFinalized, actorId, project.Revenue));
    }

    private ActivityEvent Event(Project project, string kind, long actorId, decimal? amount) => new()
    {
        DomainCode = project.DomainCode,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        ProjectId = project.Id,
        SubjectType = "project",
        SubjectId = project.Id,
        Amount = amount,
    };
}
