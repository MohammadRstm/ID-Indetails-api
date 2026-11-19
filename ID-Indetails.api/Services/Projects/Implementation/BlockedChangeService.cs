using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Execution;
using ID_Indetails.api.Business.Projects;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Boqs;
using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.DTO.Request.Expenses;
using ID_Indetails.api.DTO.Request.Labour;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Request.SiteExpenses;
using ID_Indetails.api.DTO.Request.WorkEntries;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Projects;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Boqs.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Employees.Abstraction;
using ID_Indetails.api.Repositories.Projects.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Boqs.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Expenses.Abstraction;
using ID_Indetails.api.Services.Finance.Abstraction;
using ID_Indetails.api.Services.Labour.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Projects.Abstraction;
using ID_Indetails.api.Services.SiteExpenses.Abstraction;
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
    ISiteExpenseService siteExpenses,
    ILabourService labour,
    IWorkEntryService workEntries,
    IProjectService projects,
    IBoqService boqs,
    IBoqRepository boqRows,
    IDesignVoService designVos,
    IDesignVoRepository designVoRows,
    IEmployeeService employees,
    IEmployeeRepository payRecords,
    IProjectRepository projectRows,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IBlockedChangeService
{
    public async Task<BlockedChangeResponse> GetAsync(long id, CancellationToken cancellationToken = default)
        => await DescribeAsync(await RowAsync(await ActorAsync(cancellationToken), id, cancellationToken), cancellationToken);

    public async Task<BlockedChangeResponse> ForceAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var row = await RowAsync(actor, id, cancellationToken);
        if (row.Change.Kind == BlockedChangeKind.PayChange)
        {
            await CheckPayHoldAsync(row.Change, actor, forcing: true, cancellationToken);
        }

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
            workEntries.LetOutsideDepartment = false;
        }

        if (row.Change.Kind == BlockedChangeKind.WorkEntryDepartment)
        {
            await TellLoggerAsync(row, actor, approved: true, cancellationToken);
        }

        return await DescribeAsync(await RowAsync(actor, id, cancellationToken), cancellationToken);
    }

    public async Task<BlockedChangeResponse> DismissAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        var row = await RowAsync(actor, id, cancellationToken);
        if (row.Change.Kind == BlockedChangeKind.PayChange)
        {
            await CheckPayHoldAsync(row.Change, actor, forcing: false, cancellationToken);
        }

        await DecideAsync(row, BlockedChangeStatus.Dismissed, actor, cancellationToken);
        if (row.Change.Kind == BlockedChangeKind.WorkEntryDepartment)
        {
            await TellLoggerAsync(row, actor, approved: false, cancellationToken);
        }

        return await DescribeAsync(await RowAsync(actor, id, cancellationToken), cancellationToken);
    }

    /// <summary>Tells whoever logged hours waiting for approval that they went through, or were turned down.</summary>
    private async Task TellLoggerAsync(BlockedChangeRow row, UserAccess actor, bool approved, CancellationToken cancellationToken)
    {
        await notifications.SendDigestAsync(
            new NotificationDraft(NotificationKinds.WorkEntryReleased, actor.UserId, row.Project.DomainCode, row.Project.Id, "project", row.Project.Id,
                new { projectTitle = row.Project.Title, released = approved, approved, turnedDown = !approved }),
            $"log-released-{row.Project.Id}-{(approved ? "yes" : "turned-down")}",
            [row.Change.RequestedById],
            cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
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
                var added = Read<AddSiteExpensesRequest>(change);
                return (await siteExpenses.AddAsync(added.ProjectId, added.Request, cancellationToken)).FirstOrDefault()?.Id;
            case BlockedChangeKind.MaterialUpdate:
                var siteExpense = Read<SiteExpenseEntryRequest>(change);
                return (await siteExpenses.UpdateAsync(siteExpense.ProjectId, siteExpense.ItemId!.Value, siteExpense.Request, cancellationToken)).Id;
            case BlockedChangeKind.LabourWeek:
                var week = Read<SubmitLabourWeekRequest>(change);
                await labour.SubmitWeekAsync(week.ProjectId, week.WeekStart!.Value, week.Request, cancellationToken);
                return null;
            case BlockedChangeKind.WorkEntryCreate:
                await ActAsAsync(change.RequestedById, cancellationToken);
                return (await workEntries.CreateAsync(Read<SaveWorkEntryRequest>(change).Request, cancellationToken)).Id;
            case BlockedChangeKind.WorkEntryDepartment:
                // approved: these hours alone, counted outside the project's departments, without adding theirs
                await ActAsAsync(change.RequestedById, cancellationToken);
                workEntries.LetOutsideDepartment = true;
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
            case BlockedChangeKind.DesignVo:
                return (await designVos.ConfirmAsync(Read<object?>(change).ItemId!.Value, cancellationToken)).Id;
            case BlockedChangeKind.PayChange:
                // the change stays the requester's: they are recorded as making it, and the forcer as deciding the hold
                var pay = ReadPay(change);
                await ActAsAsync(change.RequestedById, cancellationToken);
                return (await employees.ChangePayAsync(pay.UserId, pay.Request, cancellationToken)).Id;
            default:
                throw new InvalidOperationException($"No replay for {change.Kind}.");
        }
    }

    /// <summary>
    /// Nobody decides a hold on their own pay; and one held before the person's pay changed again can only be dismissed,
    /// as it would now replace a change made since.
    /// </summary>
    private async Task CheckPayHoldAsync(Models.BlockedChange change, UserAccess actor, bool forcing, CancellationToken cancellationToken)
    {
        var held = ReadPay(change);
        if (held.UserId == actor.UserId)
        {
            throw new ForbiddenException(ErrorCodes.EmployeeChangeNotAllowed, "You cannot decide a change to your own pay.");
        }

        if (forcing && await payRecords.PayVersionAsync(held.UserId, cancellationToken) != held.PayVersion)
        {
            throw new BusinessRuleException(ErrorCodes.PayChangeStale,
                "Their pay changed after this was held, so it can't be forced through: dismiss it and make the change again.");
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
        var (person, held) = c.Kind == BlockedChangeKind.PayChange ? await HeldPayAsync(c, cancellationToken) : (null, null);
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
            money.Summary.Profit,
            person,
            held);
    }

    /// <summary>
    /// Whose pay a held change is, and the projects it reaches: their profit today and, while it waits, what forcing it
    /// now would leave, from the same preview the pay form shows.
    /// </summary>
    private async Task<(PersonRefResponse? Person, IReadOnlyList<BlockedProjectResponse>? Projects)> HeldPayAsync(
        Models.BlockedChange change,
        CancellationToken cancellationToken)
    {
        var held = ReadPay(change);
        var user = await users.GetByIdAsync(held.UserId, cancellationToken);
        var person = user is null ? null : new PersonRefResponse(user.Id, PersonName.Of(user.FirstName, user.LastName, user.Email));

        var projects = new List<Models.Project>();
        foreach (var projectId in held.ProjectIds)
        {
            if (await projectRows.GetAsync(projectId, cancellationToken) is { } project)
            {
                projects.Add(project);
            }
        }

        var money = projects.Count == 0 ? null : await finance.GetAsync(projects, cancellationToken);
        DTO.Response.Employees.PayPreviewResponse? preview = null;
        if (change.Status == BlockedChangeStatus.Pending)
        {
            try
            {
                preview = await employees.PreviewPayAsync(held.UserId, held.Request, cancellationToken);
            }
            catch (AppException)
            {
                // it no longer fits (a decider's own pay, a date the rules now refuse): the profit today is still shown
            }
        }

        return (person, projects
            .Select(p => new BlockedProjectResponse(
                p.Id,
                p.Title,
                money![p.Id].Summary.Profit,
                preview?.Projects.FirstOrDefault(x => x.ProjectId == p.Id)?.ProfitAfter))
            .ToList());
    }

    /// <summary>What the change was, in words. A BOQ's number is read from the BOQ itself.</summary>
    private async Task<string> SummaryAsync(Models.BlockedChange change, CancellationToken cancellationToken)
    {
        if (change.Kind == BlockedChangeKind.PayChange)
        {
            var held = ReadPay(change);
            var user = await users.GetByIdAsync(held.UserId, cancellationToken);
            var whose = user is null ? "Someone" : PersonName.Of(user.FirstName, user.LastName, user.Email);
            var from = held.Request.EffectiveFrom is { } day ? $" from {day:d MMM yyyy}" : string.Empty;
            return $"Pay of {whose}: {held.Request.Amount:N2} {held.Request.Currency.ToUpperInvariant()} a month{from}";
        }

        if (change.Kind == BlockedChangeKind.DesignVo)
        {
            var vo = await designVoRows.GetAsync(Read<object?>(change).ItemId!.Value, cancellationToken);
            return vo is null ? "Variation order" : $"Variation order {vo.Number}: {vo.Title}, {vo.TotalUsd:N2} USD";
        }

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
                var added = Read<AddSiteExpensesRequest>(change).Request.Items;
                return $"{Counted(added.Count, "expense")}: {string.Join(", ", added.Select(e => e.Name.Trim()))}";
            case BlockedChangeKind.MaterialUpdate:
                return $"Expense change: {Read<SiteExpenseEntryRequest>(change).Request.Name.Trim()}";
            case BlockedChangeKind.LabourWeek:
                return $"Labour of the week of {Read<SubmitLabourWeekRequest>(change).WeekStart:d MMM yyyy}";
            case BlockedChangeKind.WorkEntryCreate or BlockedChangeKind.WorkEntryUpdate or BlockedChangeKind.WorkEntryDepartment:
                var entry = Read<SaveWorkEntryRequest>(change).Request;
                var minutes = (int)(entry.EndedAt - entry.StartedAt).TotalMinutes;
                return $"{change.Kind switch
                {
                    BlockedChangeKind.WorkEntryCreate => "Hours logged",
                    BlockedChangeKind.WorkEntryDepartment => "Hours waiting for their department",
                    _ => "Hours changed",
                }}: "
                    + $"{minutes / 60}h {minutes % 60:00}m on {clock.ToCompanyDate(entry.StartedAt.ToUniversalTime()):d MMM yyyy}";
            case BlockedChangeKind.Payment:
                return $"Payment set to {Read<UpdateProjectPaymentRequest>(change).Request.Revenue:N2} USD";
            case BlockedChangeKind.Pricing:
                var pricing = Read<UpdateProjectPricingRequest>(change).Request;
                var rates = new[]
                {
                    pricing.SupervisionRate is { } supervision ? $"supervision {supervision * 100:0.##}%" : null,
                    pricing.TvaRate is { } tva ? $"TVA {tva * 100:0.##}%" : null,
                }.OfType<string>().ToList();
                return rates.Count == 0 ? "Pricing change" : $"Pricing set: {string.Join(", ", rates)}";
            default:
                return change.Kind.ToString();
        }
    }

    private static string Counted(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static HeldPayChange ReadPay(Models.BlockedChange change)
        => JsonSerializer.Deserialize<HeldPayChange>(change.Payload, ProfitFloor.Json)
            ?? throw new InvalidOperationException($"Blocked change {change.Id} has no pay change.");

    private static BlockedRequest<T> Read<T>(Models.BlockedChange change)
        => JsonSerializer.Deserialize<BlockedRequest<T>>(change.Payload, ProfitFloor.Json)
            ?? throw new InvalidOperationException($"Blocked change {change.Id} has no request.");

    private async Task<BlockedChangeRow> RowAsync(UserAccess actor, long id, CancellationToken cancellationToken)
    {
        var row = await changes.GetAsync(id, cancellationToken);
        // a held pay change is the super admins' alone, as pay is
        return row is not null && actor.CanSee(row.Project.DomainCode) && (row.Change.Kind != BlockedChangeKind.PayChange || actor.IsSuperAdmin)
            ? row
            : throw new NotFoundException("Change not found.");
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
