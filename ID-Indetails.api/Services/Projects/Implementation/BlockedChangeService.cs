using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Boqs;
using ID_Indetails.api.DTO.Request.Expenses;
using ID_Indetails.api.DTO.Request.Labour;
using ID_Indetails.api.DTO.Request.Materials;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Boqs.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Boqs.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Expenses.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Labour.Abstraction;
using ID_Indetails.api.Services.Materials.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.WorkEntries.Abstraction;

namespace ID_Indetails.api.Services.Projects.Implementation;

public sealed class BlockedChangeService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IBlockedChangeRepository changes,
    IUserRepository users,
    IProjectFinanceService finance,
    IProfitFloor floor,
    IExpenseService expenses,
    IMaterialService materials,
    ILabourService labour,
    IWorkEntryService workEntries,
    IProjectService projects,
    IBoqService boqs,
    IBoqRepository boqRows,
    IClock clock) : IBlockedChangeService
{
    public async Task<BlockedChangeResponse> GetAsync(long id, CancellationToken cancellationToken = default)
        => await DescribeAsync(await RowAsync(await ActorAsync(cancellationToken), id, cancellationToken), cancellationToken);

    public async Task<BlockedChangeResponse> ForceAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var row = await RowAsync(actor, id, cancellationToken);
        await DecideAsync(row, BlockedChangeStatus.Forced, actor, cancellationToken);
        try
        {
            floor.Forcing = true;
            if (await ReplayAsync(row.Change, cancellationToken) is { } resultId)
            {
                await changes.SetResultAsync(id, resultId, cancellationToken);
            }
        }
        catch
        {
            // the change didn't go through (it no longer fits, or was changed meanwhile): it stays to force or dismiss
            await changes.ReopenAsync(id, cancellationToken);
            throw;
        }
        finally
        {
            floor.Forcing = false;
        }

        return await DescribeAsync(await RowAsync(actor, id, cancellationToken), cancellationToken);
    }

    public async Task<BlockedChangeResponse> DismissAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await DecideAsync(await RowAsync(actor, id, cancellationToken), BlockedChangeStatus.Dismissed, actor, cancellationToken);
        return await DescribeAsync(await RowAsync(actor, id, cancellationToken), cancellationToken);
    }

    /// <summary>Makes the refused request again, through the same service and checks; returns what it made, if anything.</summary>
    private async Task<long?> ReplayAsync(Models.BlockedChange change, CancellationToken cancellationToken)
    {
        switch (change.Kind)
        {
            case BlockedChangeKind.Expense:
                var expense = Read<CreateExpenseRequest>(change);
                return (await expenses.CreateAsync(expense.ProjectId, expense.Request, cancellationToken)).Id;
            case BlockedChangeKind.MaterialAdd:
                var added = Read<AddMaterialsRequest>(change);
                return (await materials.AddAsync(added.ProjectId, added.Request, cancellationToken)).FirstOrDefault()?.Id;
            case BlockedChangeKind.MaterialUpdate:
                var material = Read<MaterialEntryRequest>(change);
                return (await materials.UpdateAsync(material.ProjectId, material.ItemId!.Value, material.Request, cancellationToken)).Id;
            case BlockedChangeKind.LabourWeek:
                var week = Read<SubmitLabourWeekRequest>(change);
                await labour.SubmitWeekAsync(week.ProjectId, week.WeekStart!.Value, week.Request, cancellationToken);
                return null;
            case BlockedChangeKind.WorkEntryCreate:
                await ActAsAsync(change.RequestedById, cancellationToken);
                return (await workEntries.CreateAsync(Read<SaveWorkEntryRequest>(change).Request, cancellationToken)).Id;
            case BlockedChangeKind.WorkEntryUpdate:
                var entry = Read<SaveWorkEntryRequest>(change);
                await ActAsAsync(change.RequestedById, cancellationToken);
                return (await workEntries.UpdateAsync(entry.ItemId!.Value, entry.Request, cancellationToken)).Id;
            case BlockedChangeKind.Payment:
                var payment = Read<UpdateProjectPaymentRequest>(change);
                await projects.UpdatePaymentAsync(payment.ProjectId, payment.Request, cancellationToken);
                return null;
            case BlockedChangeKind.Pricing:
                var pricing = Read<UpdateProjectPricingRequest>(change);
                await projects.UpdatePricingAsync(pricing.ProjectId, pricing.Request, cancellationToken);
                return null;
            case BlockedChangeKind.BoqAgree:
                await boqs.AgreeAsync(Read<object?>(change).ItemId!.Value, cancellationToken);
                return null;
            case BlockedChangeKind.BoqWithdraw:
                var withdrawal = Read<WithdrawBoqAgreementRequest>(change);
                await boqs.WithdrawAgreementAsync(withdrawal.ItemId!.Value, withdrawal.Request, cancellationToken);
                return null;
            default:
                throw new InvalidOperationException($"No replay for {change.Kind}.");
        }
    }

    /// <summary>Someone's hours are theirs: forcing logs them as that person, for the rest of this request.</summary>
    private async Task ActAsAsync(long userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken) ?? throw new NotFoundException("The person who logged these hours is gone.");
        currentUser.ActAs(user.Id, user.RoleCode, user.DomainCode, PersonName.Of(user.FirstName, user.LastName, user.Email));
    }

    private async Task DecideAsync(BlockedChangeRow row, BlockedChangeStatus status, UserAccess actor, CancellationToken cancellationToken)
    {
        Throw(ProfitFloorRules.CheckPending(await changes.DecideAsync(row.Change.Id, status, actor.UserId, clock.UtcNow, cancellationToken)));
    }

    private async Task<BlockedChangeResponse> DescribeAsync(BlockedChangeRow row, CancellationToken cancellationToken)
    {
        var money = await finance.GetAsync(row.Project, cancellationToken);
        var c = row.Change;
        return new BlockedChangeResponse(
            c.Id,
            row.Project.Id,
            row.Project.Title,
            row.Project.DomainCode,
            c.Kind,
            await SummaryAsync(c, cancellationToken),
            c.Status,
            new PersonRefResponse(c.RequestedById, row.RequestedByName),
            c.RequestedAt,
            c.DecidedById is { } by && row.DecidedByName is { } byName ? new PersonRefResponse(by, byName) : null,
            c.DecidedAt,
            money.Summary.Profit);
    }

    /// <summary>What the change was, in words. A BOQ's number is read from the BOQ itself.</summary>
    private async Task<string> SummaryAsync(Models.BlockedChange change, CancellationToken cancellationToken)
    {
        if (change.Kind is not (BlockedChangeKind.BoqAgree or BlockedChangeKind.BoqWithdraw))
        {
            return Summary(change);
        }

        var boq = await boqRows.GetAsync(Read<object?>(change).ItemId!.Value, cancellationToken);
        var name = boq?.Number ?? "BOQ";
        return change.Kind == BlockedChangeKind.BoqAgree
            ? $"Agreeing {name}"
            : $"Withdrawing the agreement of {name}: {Read<WithdrawBoqAgreementRequest>(change).Request.Reason.Trim()}";
    }

    private string Summary(Models.BlockedChange change)
    {
        switch (change.Kind)
        {
            case BlockedChangeKind.Expense:
                var expense = Read<CreateExpenseRequest>(change).Request;
                return $"Expense: {expense.Label.Trim()}, {expense.Amount:N2} {expense.Currency.ToUpperInvariant()}";
            case BlockedChangeKind.MaterialAdd:
                var added = Read<AddMaterialsRequest>(change).Request.Items;
                return $"{Counted(added.Count, "material")}: {string.Join(", ", added.Select(m => m.Name.Trim()))}";
            case BlockedChangeKind.MaterialUpdate:
                return $"Material change: {Read<MaterialEntryRequest>(change).Request.Name.Trim()}";
            case BlockedChangeKind.LabourWeek:
                return $"Labour of the week of {Read<SubmitLabourWeekRequest>(change).WeekStart:d MMM yyyy}";
            case BlockedChangeKind.WorkEntryCreate or BlockedChangeKind.WorkEntryUpdate:
                var entry = Read<SaveWorkEntryRequest>(change).Request;
                var minutes = (int)(entry.EndedAt - entry.StartedAt).TotalMinutes;
                return $"{(change.Kind == BlockedChangeKind.WorkEntryCreate ? "Hours logged" : "Hours changed")}: "
                    + $"{minutes / 60}h {minutes % 60:00}m on {clock.ToCompanyDate(entry.StartedAt.ToUniversalTime()):d MMM yyyy}";
            case BlockedChangeKind.Payment:
                return $"Payment set to {Read<UpdateProjectPaymentRequest>(change).Request.Revenue:N2} USD";
            case BlockedChangeKind.Pricing:
                var pricing = Read<UpdateProjectPricingRequest>(change).Request;
                return $"Material profit set to {pricing.MaterialMarkup * 100:0.##}%{(pricing.SupervisionRate is { } supervision ? $", supervision {supervision * 100:0.##}%" : "")}";
            default:
                return change.Kind.ToString();
        }
    }

    private static string Counted(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static BlockedRequest<T> Read<T>(Models.BlockedChange change)
        => JsonSerializer.Deserialize<BlockedRequest<T>>(change.Payload, ProfitFloor.Json)
            ?? throw new InvalidOperationException($"Blocked change {change.Id} has no request.");

    private async Task<BlockedChangeRow> RowAsync(UserAccess actor, long id, CancellationToken cancellationToken)
    {
        var row = await changes.GetAsync(id, cancellationToken);
        return row is not null && actor.CanSee(row.Project.DomainCode) ? row : throw new NotFoundException("Change not found.");
    }

    /// <summary>Refused changes are about money: whoever sees it decides.</summary>
    private async Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.FinancialView);

    private static void Throw(RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }
}
