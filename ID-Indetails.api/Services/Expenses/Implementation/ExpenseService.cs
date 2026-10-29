using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Expenses;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Expenses;
using ID_Indetails.api.DTO.Response.Expenses;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Expenses.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Expenses.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Services.Expenses.Implementation;

/// <summary>Expenses of projects in the caller's visible domains.</summary>
public sealed class ExpenseService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IProjectRepository projects,
    IExpenseRepository expenses,
    IActivityEventRepository activity,
    IUnitOfWork unitOfWork,
    IProfitFloor floor,
    IClock clock) : IExpenseService
{
    public async Task<ExpenseListResponse> ListAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.ExpenseManage, PermissionCodes.FinancialView);
        var project = await projects.GetAsync(projectId, cancellationToken);
        EnsureVisible(actor, project);

        var rows = await expenses.ListAsync(projectId, cancellationToken);
        var totals = ExpenseRules.Totals(rows.Select(x => (x.Currency, x.Amount)));
        return new ExpenseListResponse(
            rows.Select(ToResponse).ToList(),
            totals.Select(t => new CurrencyTotalResponse(t.Currency, t.Total)).ToList());
    }

    public async Task<ExpenseResponse> CreateAsync(long projectId, CreateExpenseRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await EditableProjectAsync(actor, projectId, cancellationToken);
        if (ExpenseRules.CheckNotExecution(project.DomainCode) is { } denial)
        {
            throw denial.ToException();
        }

        var before = await floor.BeforeAsync(project, cancellationToken);

        var expense = new ProjectExpense
        {
            ProjectId = project.Id,
            Label = request.Label.Trim(),
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            Category = request.Category ?? ExpenseRules.DefaultCategory,
            SpentOn = request.SpentOn ?? clock.CompanyToday,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedAt = clock.UtcNow,
            CreatedById = actor.UserId,
        };
        expenses.Add(expense);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity.Add(ExpenseEvent(project, expense, ActivityEventKinds.ExpenseAdded, actor.UserId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await floor.CheckAsync(before, BlockedChangeKind.Expense, new BlockedRequest<CreateExpenseRequest>(project.Id, request), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(expense);
    }

    public async Task DeleteAsync(long projectId, long expenseId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var project = await EditableProjectAsync(actor, projectId, cancellationToken);
        var expense = await expenses.GetForUpdateAsync(projectId, expenseId, cancellationToken)
            ?? throw new NotFoundException("Expense not found.");

        expense.DeletedAt = clock.UtcNow;
        activity.Add(ExpenseEvent(project, expense, ActivityEventKinds.ExpenseRemoved, actor.UserId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    private static void EnsureVisible(UserAccess actor, Project? project)
    {
        if (project is null || !actor.CanSee(project.DomainCode))
        {
            throw new NotFoundException("Project not found.");
        }
    }

    private async Task<Project> EditableProjectAsync(UserAccess actor, long projectId, CancellationToken cancellationToken)
    {
        var project = await projects.GetForUpdateAsync(projectId, cancellationToken);
        EnsureVisible(actor, project);

        if (ExpenseRules.CheckEditable(project!.Status, project.RevenueLockedAt is not null) is { } denial)
        {
            throw denial.ToException();
        }

        return project;
    }

    private ActivityEvent ExpenseEvent(Project project, ProjectExpense expense, string kind, long actorId) => new()
    {
        DomainCode = project.DomainCode,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        ProjectId = project.Id,
        SubjectType = "expense",
        SubjectId = expense.Id,
        Amount = expense.Amount,
        Payload = JsonSerializer.Serialize(new { label = expense.Label, category = expense.Category, currency = expense.Currency }),
    };

    private static ExpenseResponse ToResponse(ProjectExpense x)
        => new(x.Id, x.Label, x.Amount, x.Currency, x.Category, x.SpentOn, x.Note, x.CreatedAt);
}
