using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Execution;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Execution;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Execution.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Execution.Abstraction;

namespace ID_Indetails.api.Services.Execution.Implementation;

public sealed class SupervisionWithdrawalService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    ISupervisionWithdrawalRepository withdrawals,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IClock clock) : ISupervisionWithdrawalService
{
    private const string ProjectNotFound = "Project not found.";
    private const string WithdrawalNotFound = "Supervision withdrawal not found.";

    public async Task<IReadOnlyList<SupervisionWithdrawalResponse>> ListAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.FinancialView);
        var project = await projects.GetAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException(ProjectNotFound);
        }

        Throw(SupervisionRules.CheckSite(project.DomainCode, project.BillingType));
        return (await withdrawals.ListAsync(projectId, cancellationToken)).Select(ToResponse).ToList();
    }

    public async Task<SupervisionWithdrawalResponse> TakeAsync(long projectId, TakeSupervisionRequest request, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.PaymentRecord);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await projects.GetForUpdateAsync(projectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException(ProjectNotFound);
        }

        Throw(SupervisionRules.CheckSite(project.DomainCode, project.BillingType));
        Throw(SiteRules.CheckOpen(project.DomainCode, project.Status, project.RevenueLockedAt is not null));
        Throw(SupervisionRules.CheckTakenOn(request.TakenOn, clock.CompanyToday));

        var withdrawal = new SupervisionWithdrawal
        {
            ProjectId = project.Id,
            AmountUsd = request.AmountUsd,
            TakenOn = request.TakenOn,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedAt = clock.UtcNow,
            CreatedById = actor.UserId,
        };
        withdrawals.Add(withdrawal);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity.Add(WithdrawalEvent(project, withdrawal, ActivityEventKinds.SupervisionTaken, actor.UserId, new { takenOn = withdrawal.TakenOn, note = withdrawal.Note }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(await withdrawals.GetRowAsync(withdrawal.Id, cancellationToken) ?? throw new NotFoundException(WithdrawalNotFound));
    }

    public async Task RemoveAsync(long withdrawalId, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.PaymentRecord);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var withdrawal = await withdrawals.GetForUpdateAsync(withdrawalId, cancellationToken)
            ?? throw new NotFoundException(WithdrawalNotFound);
        var project = await projects.GetForUpdateAsync(withdrawal.ProjectId, cancellationToken);
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException(WithdrawalNotFound);
        }

        Throw(SiteRules.CheckOpen(project.DomainCode, project.Status, project.RevenueLockedAt is not null));

        withdrawal.DeletedAt = clock.UtcNow;
        activity.Add(WithdrawalEvent(project, withdrawal, ActivityEventKinds.SupervisionWithdrawalRemoved, actor.UserId, new { takenOn = withdrawal.TakenOn }));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    private static SupervisionWithdrawalResponse ToResponse(SupervisionWithdrawalRow row) => new(
        row.Withdrawal.Id,
        row.Withdrawal.AmountUsd,
        row.Withdrawal.TakenOn,
        row.Withdrawal.Note,
        new PersonRefResponse(row.Withdrawal.CreatedById, row.CreatedByName),
        row.Withdrawal.CreatedAt);

    private ActivityEvent WithdrawalEvent(Project project, SupervisionWithdrawal withdrawal, string kind, long actorId, object payload) => new()
    {
        DomainCode = project.DomainCode,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        ProjectId = project.Id,
        ClientId = project.ClientId,
        SubjectType = "supervision_withdrawal",
        SubjectId = withdrawal.Id,
        Amount = withdrawal.AmountUsd,
        Payload = JsonSerializer.Serialize(payload),
    };

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
