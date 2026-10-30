using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Execution.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;

namespace ID_Indetails.api.Services.Projects.Implementation;

public sealed class ProfitFloor(
    ICurrentUser currentUser,
    IProjectFinanceService finance,
    IPocketService pocket,
    INotificationSender notifications,
    IUnitOfWork unitOfWork) : IProfitFloor
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool Forcing { get; set; }

    public async Task<ProfitBefore> BeforeAsync(Project project, CancellationToken cancellationToken = default)
        => new(project, Applies(project) ? (await MoneyAsync(project, cancellationToken)).Profit : null);

    public async Task CheckAsync<T>(ProfitBefore before, BlockedChangeKind kind, BlockedRequest<T> request, CancellationToken cancellationToken = default)
    {
        // lowering the payment to nothing is a change like any other
        var project = before.Project;
        if (before.Profit is null && !Applies(project))
        {
            return;
        }

        var (profit, agreed) = await MoneyAsync(project, cancellationToken);
        if (profit >= before.Profit)
        {
            return;
        }

        var standing = ProfitFloorRules.StandingOf(profit, agreed);
        if (standing == ProfitFloorRules.Standing.AtZero && !Forcing)
        {
            throw new ProfitFloorException(project.Id, project.Title, project.DomainCode, kind, JsonSerializer.Serialize(request, Json), currentUser.Id);
        }

        if (standing != ProfitFloorRules.Standing.Fine)
        {
            await AlertAsync(project, standing, profit, agreed, cancellationToken);
        }
    }

    /// <summary>
    /// Tells whoever sees the domain's money, once per project and standing, that the project is close to zero or at
    /// it; an Execution site also asks the pocket money for help. The scheduler sends the same alerts, under the same
    /// keys, when the profit drops without anyone's change (pay, schedules, exchange rates).
    /// </summary>
    public static async Task AlertAsync(
        INotificationSender notifications,
        Project project,
        ProfitFloorRules.Standing standing,
        decimal profit,
        decimal agreed,
        long? actorId,
        CancellationToken cancellationToken)
    {
        var (kind, key) = standing == ProfitFloorRules.Standing.AtZero
            ? (NotificationKinds.ProfitAtZero, $"profit-zero-{project.Id}")
            : (NotificationKinds.ProfitLow, $"profit-low-{project.Id}");
        await notifications.SendOnceAsync(
            new NotificationDraft(kind, actorId, project.DomainCode, project.Id, "project", project.Id,
                new { projectTitle = project.Title, marginPct = ProfitFloorRules.MarginPct(profit, agreed) }),
            key,
            await notifications.HoldersAsync(project.DomainCode, PermissionCodes.FinancialView, cancellationToken),
            cancellationToken);
    }

    private static bool Applies(Project project)
        => ProfitFloorRules.Applies(project.BillingType, project.DomainCode, project.Revenue, project.ProjectFee);

    /// <summary>Profit and agreed amount: Execution's agreed amount is the work price plus supervision, net of TVA.</summary>
    private async Task<(decimal Profit, decimal Agreed)> MoneyAsync(Project project, CancellationToken cancellationToken)
    {
        var money = await finance.GetAsync(project, cancellationToken);
        return (money.Summary.Profit, money.Summary.Revenue);
    }

    private async Task AlertAsync(Project project, ProfitFloorRules.Standing standing, decimal profit, decimal agreed, CancellationToken cancellationToken)
    {
        await AlertAsync(notifications, project, standing, profit, agreed, currentUser.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (project.DomainCode == DomainCodes.Execution)
        {
            await pocket.RaiseAsync(project, "Close to zero profit", cancellationToken);
        }
    }
}

public sealed class BlockedChangeRecorder(
    IBlockedChangeRepository changes,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IBlockedChangeRecorder
{
    public async Task RecordAsync(ProfitFloorException refused, CancellationToken cancellationToken = default)
    {
        // what the refused request staged was rolled back with its transaction
        unitOfWork.DiscardChanges();
        var change = new BlockedChange
        {
            ProjectId = refused.ProjectId,
            Kind = refused.Kind,
            Payload = refused.Payload,
            RequestedById = refused.RequestedById,
            RequestedAt = clock.UtcNow,
        };
        changes.Add(change);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // the payload never holds amounts: whoever opens it reads them under financial:view
        await notifications.SendAsync(
            new NotificationDraft(NotificationKinds.ChangeBlocked, refused.RequestedById, refused.Domain, refused.ProjectId, "blocked_change", change.Id,
                new { projectTitle = refused.ProjectTitle, kind = JsonNamingPolicy.SnakeCaseLower.ConvertName(refused.Kind.ToString()) }),
            await notifications.HoldersAsync(refused.Domain, PermissionCodes.FinancialView, cancellationToken),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        refused.Id = change.Id;
    }
}
