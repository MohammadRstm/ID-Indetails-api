using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Business.Payroll;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Payroll;
using ID_Indetails.api.DTO.Response.WorkEntries;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Payroll.Abstraction;

namespace ID_Indetails.api.Services.Payroll.Implementation;

/// <summary>
/// Every change locks the person, then the advance (<c>SELECT … FOR UPDATE</c>), in that order: two super admins deciding
/// at once run one after the other, and a request, an approval and a recorded advance can't overlap.
/// </summary>
public sealed class SalaryAdvanceService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IUserRepository users,
    IPayrollRepository payroll,
    ISalaryAdvanceRepository advances,
    ISalaryAdvanceSchedules schedules,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : ISalaryAdvanceService
{
    private const string NotFoundMessage = "Advance not found.";

    private DateOnly CurrentMonth => AdvanceRules.MonthOf(clock.CompanyToday);

    public async Task<MySalaryAdvancesResponse> MineAsync(CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var person = await PersonAsync(actor.UserId, cancellationToken);
        var mine = await advances.OfUserAsync(person.Id, cancellationToken);
        var given = await schedules.ScheduleAsync(Given(mine), CurrentMonth, cancellationToken: cancellationToken);
        var payNow = await schedules.PayNowAsync(person, cancellationToken);
        var denial = AdvanceRules.CheckAsk(
            payNow is not null,
            mine.Any(a => a.Status == SalaryAdvanceStatus.Pending),
            given.Values.Any(IsOpen));

        return new MySalaryAdvancesResponse(
            denial is null,
            denial?.Code,
            payNow?.Currency,
            mine.Select(a => ToMine(a, given.GetValueOrDefault(a.Id))).ToList());
    }

    public async Task<MySalaryAdvanceResponse> AskAsync(AskSalaryAdvanceRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        long id;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            await LockPersonAsync(actor.UserId, cancellationToken);
            var person = await PersonAsync(actor.UserId, cancellationToken);
            var payNow = await schedules.PayNowAsync(person, cancellationToken);
            AdvanceRules.CheckAsk(
                    payNow is not null,
                    await advances.HasPendingAsync(person.Id, cancellationToken),
                    await HasOpenAsync(person.Id, cancellationToken))
                .ThrowIfDenied();

            var now = clock.UtcNow;
            var asked = new SalaryAdvance
            {
                UserId = person.Id,
                Amount = request.Amount,
                AskedAmount = request.Amount,
                Currency = payNow!.Currency,
                Status = SalaryAdvanceStatus.Pending,
                Reason = request.Reason.Trim(),
                CreatedAt = now,
                CreatedById = person.Id,
            };
            advances.Add(asked);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            activity.Add(Event(asked, ActivityEventKinds.SalaryAdvanceRequested, person.Id, now));
            await notifications.SendAsync(
                Draft(NotificationKinds.SalaryAdvanceRequested, person.Id, asked, NameOf(person)),
                await GiversAsync(person, cancellationToken),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            id = asked.Id;
        }

        return await MineOneAsync(actor.UserId, id, cancellationToken);
    }

    public async Task<MySalaryAdvanceResponse> WithdrawAsync(long id, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            await LockPersonAsync(actor.UserId, cancellationToken);
            var advance = await advances.GetForUpdateAsync(id, cancellationToken);
            if (advance is null || advance.UserId != actor.UserId)
            {
                throw new NotFoundException(ErrorCodes.NotFound, NotFoundMessage);
            }

            AdvanceRules.CheckPending(advance.Status).ThrowIfDenied();
            var now = clock.UtcNow;
            advance.Status = SalaryAdvanceStatus.Withdrawn;
            advance.DecidedAt = now;
            advance.DecidedById = actor.UserId;
            activity.Add(Event(advance, ActivityEventKinds.SalaryAdvanceWithdrawn, actor.UserId, now));
            var person = await PersonAsync(actor.UserId, cancellationToken);
            await notifications.SendAsync(
                Draft(NotificationKinds.SalaryAdvanceWithdrawn, actor.UserId, advance, NameOf(person)),
                await GiversAsync(person, cancellationToken),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await MineOneAsync(actor.UserId, id, cancellationToken);
    }

    public async Task<PagedResponse<SalaryAdvanceResponse>> ListAsync(SalaryAdvanceListQuery query, CancellationToken cancellationToken = default)
    {
        var actor = await ViewerAsync(cancellationToken);
        SalaryAdvanceStatus[] statuses = query.View == SalaryAdvanceView.Pending
            ? [SalaryAdvanceStatus.Pending]
            : [SalaryAdvanceStatus.Approved, SalaryAdvanceStatus.Declined, SalaryAdvanceStatus.Withdrawn, SalaryAdvanceStatus.Cancelled];
        var rows = (await advances.WithStatusAsync(statuses, cancellationToken)).Where(a => Sees(actor, a.User)).ToList();
        var given = await schedules.ScheduleAsync(Given(rows), CurrentMonth, cancellationToken: cancellationToken);
        bool Open(SalaryAdvance a) => given.TryGetValue(a.Id, out var g) && IsOpen(g);

        var listed = query.View switch
        {
            SalaryAdvanceView.Pending => rows.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToList(),
            SalaryAdvanceView.Open => rows.Where(Open).OrderByDescending(a => a.GivenOn).ThenByDescending(a => a.Id).ToList(),
            _ => rows.Where(a => !Open(a)).OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).ToList(),
        };
        var page = listed.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        var items = await DescribeAsync(actor, page, given, cancellationToken);
        return new PagedResponse<SalaryAdvanceResponse>(items, query.Page, query.PageSize, listed.Count);
    }

    public async Task<SalaryAdvanceResponse> GetAsync(long id, CancellationToken cancellationToken = default)
        => await DescribeAsync(await ViewerAsync(cancellationToken), id, cancellationToken);

    public async Task<SalaryAdvanceResponse> ApproveAsync(long id, ApproveSalaryAdvanceRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await GiverAsync(cancellationToken);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var (advance, person) = await LockAsync(actor, id, cancellationToken);
            AdvanceRules.CheckPending(advance.Status).ThrowIfDenied();
            var payNow = await schedules.PayNowAsync(person, cancellationToken);
            var amount = request.Amount ?? advance.Amount;
            var months = Months(request.Months);
            AdvanceRules.CheckGive(
                    amount,
                    advance.AskedAmount,
                    payNow is not null,
                    payNow?.MonthlyPay ?? 0,
                    await HasOpenAsync(person.Id, cancellationToken),
                    months,
                    CurrentMonth)
                .ThrowIfDenied();

            var now = clock.UtcNow;
            advance.Status = SalaryAdvanceStatus.Approved;
            advance.Amount = amount;
            advance.Currency = payNow!.Currency;
            advance.GivenOn = clock.CompanyToday;
            advance.DecidedAt = now;
            advance.DecidedById = actor.UserId;
            advance.DecisionNote = Clean(request.Note);
            advance.Repayments.AddRange(months.Select(m => new SalaryAdvanceRepayment { Month = m.Month, Amount = m.Amount }));
            activity.Add(Event(advance, ActivityEventKinds.SalaryAdvanceApproved, actor.UserId, now));
            await notifications.SendAsync(Draft(NotificationKinds.SalaryAdvanceApproved, actor.UserId, advance, null), [person.Id], cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(actor, id, cancellationToken);
    }

    public async Task<SalaryAdvanceResponse> DeclineAsync(long id, SalaryAdvanceNoteRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await GiverAsync(cancellationToken);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var (advance, person) = await LockAsync(actor, id, cancellationToken);
            AdvanceRules.CheckPending(advance.Status).ThrowIfDenied();

            var now = clock.UtcNow;
            advance.Status = SalaryAdvanceStatus.Declined;
            advance.DecidedAt = now;
            advance.DecidedById = actor.UserId;
            advance.DecisionNote = Clean(request.Note);
            activity.Add(Event(advance, ActivityEventKinds.SalaryAdvanceDeclined, actor.UserId, now));
            await notifications.SendAsync(Draft(NotificationKinds.SalaryAdvanceDeclined, actor.UserId, advance, null), [person.Id], cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(actor, id, cancellationToken);
    }

    public async Task<SalaryAdvanceResponse> RecordAsync(RecordSalaryAdvanceRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await GiverAsync(cancellationToken);
        long id;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            await LockPersonAsync(request.UserId, cancellationToken);
            var person = await VisiblePersonAsync(actor, request.UserId, cancellationToken);
            CheckDecider(actor, person);
            var givenOn = request.GivenOn ?? clock.CompanyToday;
            AdvanceRules.CheckGivenOn(givenOn, clock.CompanyToday).ThrowIfDenied();
            if (await advances.HasPendingAsync(person.Id, cancellationToken))
            {
                throw new BusinessRuleException(ErrorCodes.SalaryAdvancePending, "They have a request waiting for a decision: decide that one instead.");
            }

            var payNow = await schedules.PayNowAsync(person, cancellationToken);
            var months = Months(request.Months);
            AdvanceRules.CheckGive(
                    request.Amount,
                    null,
                    payNow is not null,
                    payNow?.MonthlyPay ?? 0,
                    await HasOpenAsync(person.Id, cancellationToken),
                    months,
                    AdvanceRules.MonthOf(givenOn))
                .ThrowIfDenied();

            var now = clock.UtcNow;
            var given = new SalaryAdvance
            {
                UserId = person.Id,
                Amount = request.Amount,
                Currency = payNow!.Currency,
                Status = SalaryAdvanceStatus.Approved,
                CreatedAt = now,
                CreatedById = actor.UserId,
                DecidedAt = now,
                DecidedById = actor.UserId,
                DecisionNote = Clean(request.Note),
                GivenOn = givenOn,
                Repayments = months.Select(m => new SalaryAdvanceRepayment { Month = m.Month, Amount = m.Amount }).ToList(),
            };
            advances.Add(given);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            activity.Add(Event(given, ActivityEventKinds.SalaryAdvanceRecorded, actor.UserId, now));
            await notifications.SendAsync(Draft(NotificationKinds.SalaryAdvanceRecorded, actor.UserId, given, null), [person.Id], cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            id = given.Id;
        }

        return await DescribeAsync(actor, id, cancellationToken);
    }

    public async Task<SalaryAdvanceResponse> ChangeMonthsAsync(long id, SalaryAdvanceMonthsRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await GiverAsync(cancellationToken);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var (advance, person) = await LockAsync(actor, id, cancellationToken);
            var currentMonth = CurrentMonth;
            var payNow = await schedules.PayNowAsync(person, cancellationToken);
            if (payNow is null && AdvanceRules.CheckGiven(advance.Status, advance.SettledOn) is null)
            {
                throw new BusinessRuleException(ErrorCodes.SalaryAdvanceNoPay, "They have no pay set today, so nothing can come off it: settle what is left instead.");
            }

            var months = Months(request.Months);
            AdvanceRules.CheckChangeMonths(
                    advance.Status,
                    advance.SettledOn,
                    advance.Amount,
                    advance.Repayments.Where(r => r.Month < currentMonth).Sum(r => r.Amount),
                    payNow?.MonthlyPay ?? 0,
                    months,
                    currentMonth)
                .ThrowIfDenied();

            // the open months go first, so a month chosen again doesn't meet its old row
            advances.RemoveRepayments(advance.Repayments.Where(r => r.Month >= currentMonth).ToList());
            advance.Repayments.RemoveAll(r => r.Month >= currentMonth);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            advance.Repayments.AddRange(months.Select(m => new SalaryAdvanceRepayment { AdvanceId = advance.Id, Month = m.Month, Amount = m.Amount }));
            activity.Add(Event(advance, ActivityEventKinds.SalaryAdvanceMonthsChanged, actor.UserId, clock.UtcNow));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(actor, id, cancellationToken);
    }

    public async Task<SalaryAdvanceResponse> CancelAsync(long id, SalaryAdvanceNoteRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await GiverAsync(cancellationToken);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var (advance, _) = await LockAsync(actor, id, cancellationToken);
            AdvanceRules.CheckCancel(advance.Status, advance.SettledOn, advance.GivenOn, CurrentMonth).ThrowIfDenied();

            var now = clock.UtcNow;
            advance.Status = SalaryAdvanceStatus.Cancelled;
            advance.CancelledAt = now;
            advance.CancelledById = actor.UserId;
            if (Clean(request.Note) is { } note)
            {
                advance.DecisionNote = note;
            }

            activity.Add(Event(advance, ActivityEventKinds.SalaryAdvanceCancelled, actor.UserId, now));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(actor, id, cancellationToken);
    }

    public async Task<SalaryAdvanceResponse> SettleAsync(long id, SettleSalaryAdvanceRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await GiverAsync(cancellationToken);
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var (advance, _) = await LockAsync(actor, id, cancellationToken);
            var currentMonth = CurrentMonth;
            var described = await advances.GetAsync(id, cancellationToken) ?? throw new NotFoundException(ErrorCodes.NotFound, NotFoundMessage);
            var schedule = (await schedules.ScheduleAsync([described], currentMonth, cancellationToken: cancellationToken)).GetValueOrDefault(id);
            var left = advance.Amount - (schedule?.Schedule.TakenBefore(currentMonth) ?? 0);
            AdvanceRules.CheckSettle(advance.Status, advance.SettledOn, left).ThrowIfDenied();

            var now = clock.UtcNow;
            advance.SettledKind = request.Kind;
            advance.SettledAmount = left;
            advance.SettledOn = clock.CompanyToday;
            advance.SettledById = actor.UserId;
            advance.SettlementNote = Clean(request.Note);
            activity.Add(Event(advance, ActivityEventKinds.SalaryAdvanceSettled, actor.UserId, now));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DescribeAsync(actor, id, cancellationToken);
    }

    public async Task<IReadOnlyList<SalaryAdvanceResponse>> ForSalaryAsync(User person, DateOnly month, CancellationToken cancellationToken = default)
    {
        var actor = await ViewerAsync(cancellationToken);
        var rows = (await advances.OfUserAsync(person.Id, cancellationToken))
            .Where(a => a.Status == SalaryAdvanceStatus.Pending || a.Status == SalaryAdvanceStatus.Approved)
            .ToList();
        var given = await schedules.ScheduleAsync(Given(rows), month, cancellationToken: cancellationToken);
        var shown = rows
            .Where(a => a.Status == SalaryAdvanceStatus.Pending
                || (given.TryGetValue(a.Id, out var g)
                    && (g.Schedule.Months.Any(m => m.Month == month) || a.Repayments.Any(r => r.Month == month) || IsOpen(g))))
            .ToList();
        return await DescribeAsync(actor, shown, given, cancellationToken);
    }

    public async Task<SalaryAdvanceSummaryResponse> SummaryAsync(CancellationToken cancellationToken = default)
    {
        var actor = await ViewerAsync(cancellationToken);
        var rows = (await advances.WithStatusAsync([SalaryAdvanceStatus.Pending, SalaryAdvanceStatus.Approved], cancellationToken))
            .Where(a => Sees(actor, a.User) && a.SettledOn is null)
            .ToList();
        var given = await schedules.ScheduleAsync(Given(rows), CurrentMonth, cancellationToken: cancellationToken);
        var open = given.Values.Where(IsOpen).ToList();
        var left = open
            .GroupBy(g => g.Advance.Currency)
            .OrderBy(g => g.Key)
            .Select(g => new CurrencyAmountResponse(g.Key, g.Sum(x => x.Advance.Amount - x.Schedule.Months.Sum(m => m.Taken))))
            .Where(c => c.Amount > 0)
            .ToList();
        return new SalaryAdvanceSummaryResponse(
            rows.Count(a => a.Status == SalaryAdvanceStatus.Pending),
            left,
            open.Count(g => HasLeft(g.Advance.User)));
    }

    public async Task NoteLeavingAsync(User person, long actorId, CancellationToken cancellationToken = default)
    {
        if (!await HasOpenAsync(person.Id, cancellationToken))
        {
            return;
        }

        var leaver = await PersonAsync(person.Id, cancellationToken);
        await notifications.SendAsync(
            new NotificationDraft(
                NotificationKinds.SalaryAdvanceUnsettled, actorId, leaver.DomainCode, null, "user", person.Id, new { name = NameOf(leaver), userId = person.Id }),
            await GiversAsync(leaver, cancellationToken),
            cancellationToken);
    }

    private async Task<bool> HasOpenAsync(long userId, CancellationToken cancellationToken)
    {
        var given = await advances.GivenToAsync([userId], cancellationToken);
        return (await schedules.ScheduleAsync(given, CurrentMonth, cancellationToken: cancellationToken)).Values.Any(IsOpen);
    }

    private bool IsOpen(GivenAdvance given)
        => AdvanceRules.IsOpen(given.Advance.Status, given.Advance.SettledOn, given.Advance.Amount, given.Schedule.TakenBefore(CurrentMonth));

    private static List<SalaryAdvance> Given(IEnumerable<SalaryAdvance> rows) => rows.Where(a => a.Status == SalaryAdvanceStatus.Approved).ToList();

    private static IReadOnlyList<AdvanceRules.PlannedMonth> Months(IReadOnlyList<PlannedMonthRequest> months)
        => months.Select(m => new AdvanceRules.PlannedMonth(m.Month, m.Amount)).ToList();

    private async Task<UserAccess> ViewerAsync(CancellationToken cancellationToken)
        => (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.SalaryAdvanceManage, PermissionCodes.SalaryView);

    private async Task<UserAccess> GiverAsync(CancellationToken cancellationToken)
        => (await permissions.GetAccessAsync(currentUser.Id, cancellationToken)).Require(PermissionCodes.SalaryAdvanceManage);

    private static bool Sees(UserAccess actor, User person) => EmployeeRules.CanSee(actor, person.DomainCode);

    private static void CheckDecider(UserAccess actor, User person)
        => AdvanceRules.CheckDecider(
                actor.UserId, person.Id, person.RoleCode == RoleCodes.SuperAdmin, actor.Has(PermissionCodes.EmployeeInviteSuperAdmin))
            .ThrowIfDenied();

    private static bool MayDecide(UserAccess actor, User person)
        => actor.Has(PermissionCodes.SalaryAdvanceManage)
            && AdvanceRules.CheckDecider(
                actor.UserId, person.Id, person.RoleCode == RoleCodes.SuperAdmin, actor.Has(PermissionCodes.EmployeeInviteSuperAdmin)) is null;

    private async Task LockPersonAsync(long userId, CancellationToken cancellationToken)
        => _ = await users.GetByIdForUpdateAsync(userId, cancellationToken) ?? throw new NotFoundException(ErrorCodes.NotFound, "Employee not found.");

    /// <summary>The person with their team and periods of employment, not tracked.</summary>
    private async Task<User> PersonAsync(long userId, CancellationToken cancellationToken)
        => (await payroll.UsersAsync([userId], cancellationToken)).SingleOrDefault()
            ?? throw new NotFoundException(ErrorCodes.NotFound, "Employee not found.");

    private async Task<User> VisiblePersonAsync(UserAccess actor, long userId, CancellationToken cancellationToken)
    {
        var person = await PersonAsync(userId, cancellationToken);
        return Sees(actor, person) ? person : throw new NotFoundException(ErrorCodes.NotFound, "Employee not found.");
    }

    /// <summary>The person, then the advance: the order every change to someone's advances takes.</summary>
    private async Task<(SalaryAdvance Advance, User Person)> LockAsync(UserAccess actor, long id, CancellationToken cancellationToken)
    {
        var found = await advances.GetAsync(id, cancellationToken) ?? throw new NotFoundException(ErrorCodes.NotFound, NotFoundMessage);
        if (!Sees(actor, found.User))
        {
            throw new NotFoundException(ErrorCodes.NotFound, NotFoundMessage);
        }

        await LockPersonAsync(found.UserId, cancellationToken);
        var advance = await advances.GetForUpdateAsync(id, cancellationToken) ?? throw new NotFoundException(ErrorCodes.NotFound, NotFoundMessage);
        CheckDecider(actor, found.User);
        return (advance, found.User);
    }

    /// <summary>Those who give advances and see the person: holders of the permission in their business, every super admin for a super admin.</summary>
    private async Task<IReadOnlyList<long>> GiversAsync(User person, CancellationToken cancellationToken)
        => person.DomainCode is { } domain
            ? await notifications.HoldersAsync(domain, PermissionCodes.SalaryAdvanceManage, cancellationToken)
            : await notifications.SuperAdminsAsync(cancellationToken);

    private async Task<MySalaryAdvanceResponse> MineOneAsync(long userId, long id, CancellationToken cancellationToken)
    {
        var advance = await advances.GetAsync(id, cancellationToken);
        if (advance is null || advance.UserId != userId)
        {
            throw new NotFoundException(ErrorCodes.NotFound, NotFoundMessage);
        }

        var given = await schedules.ScheduleAsync(Given([advance]), CurrentMonth, cancellationToken: cancellationToken);
        return ToMine(advance, given.GetValueOrDefault(id));
    }

    private async Task<SalaryAdvanceResponse> DescribeAsync(UserAccess actor, long id, CancellationToken cancellationToken)
    {
        var advance = await advances.GetAsync(id, cancellationToken);
        if (advance is null || !Sees(actor, advance.User))
        {
            throw new NotFoundException(ErrorCodes.NotFound, NotFoundMessage);
        }

        var given = await schedules.ScheduleAsync(Given([advance]), CurrentMonth, cancellationToken: cancellationToken);
        return (await DescribeAsync(actor, [advance], given, cancellationToken))[0];
    }

    private async Task<IReadOnlyList<SalaryAdvanceResponse>> DescribeAsync(
        UserAccess actor,
        IReadOnlyList<SalaryAdvance> rows,
        IReadOnlyDictionary<long, GivenAdvance> given,
        CancellationToken cancellationToken)
    {
        var nameIds = rows
            .SelectMany(a => new[] { a.CreatedById, a.DecidedById, a.SettledById })
            .OfType<long>()
            .Distinct()
            .ToList();
        IReadOnlyDictionary<long, string> names = nameIds.Count == 0 ? new Dictionary<long, string>() : await advances.NamesAsync(nameIds, cancellationToken);
        return rows.Select(a => Describe(actor, a, given.GetValueOrDefault(a.Id), names)).ToList();
    }

    private SalaryAdvanceResponse Describe(UserAccess actor, SalaryAdvance a, GivenAdvance? given, IReadOnlyDictionary<long, string> names)
    {
        var currentMonth = CurrentMonth;
        var repaid = given?.Schedule.Months.Sum(m => m.Taken);
        decimal? left = a.Status == SalaryAdvanceStatus.Approved ? (a.SettledOn is null ? a.Amount - (repaid ?? 0) : 0) : null;
        var open = given is not null && IsOpen(given);
        var mayDecide = MayDecide(actor, a.User);
        var changeable = mayDecide && AdvanceRules.CheckGiven(a.Status, a.SettledOn) is null;

        return new SalaryAdvanceResponse(
            a.Id,
            new SalaryAdvancePersonResponse(
                a.User.Id,
                NameOf(a.User),
                a.User.PhotoFileId,
                a.User.RoleCode,
                a.User.DomainCode,
                a.User.Team?.Name,
                LeftOn(a.User)),
            a.Status,
            a.Amount,
            a.AskedAmount,
            a.Currency,
            a.Reason,
            a.CreatedAt,
            a.AskedAmount is null ? names.GetValueOrDefault(a.CreatedById) : null,
            a.DecidedAt,
            a.DecidedById is { } decidedBy && a.AskedAmount is not null ? names.GetValueOrDefault(decidedBy) : null,
            a.DecisionNote,
            a.GivenOn,
            MonthsOf(a, given, currentMonth, forPerson: false),
            a.Status == SalaryAdvanceStatus.Approved ? repaid ?? 0 : null,
            left,
            given?.Schedule.Months.Any(m => m.Month < currentMonth && m.Taken < m.Due) ?? false,
            open && HasLeft(a.User),
            a.SettledKind is { } kind
                ? new SalaryAdvanceSettlementResponse(
                    kind, a.SettledAmount ?? 0, a.SettledOn!.Value, a.SettledById is { } by ? names.GetValueOrDefault(by) : null, a.SettlementNote)
                : null,
            a.CancelledAt,
            mayDecide && a.Status == SalaryAdvanceStatus.Pending,
            changeable,
            changeable && a.GivenOn is { } givenOn && AdvanceRules.MonthOf(givenOn) == currentMonth);
    }

    private MySalaryAdvanceResponse ToMine(SalaryAdvance a, GivenAdvance? given)
    {
        var repaid = given?.Schedule.Months.Sum(m => m.Taken) ?? 0;
        return new MySalaryAdvanceResponse(
            a.Id,
            a.Status,
            a.Amount,
            a.AskedAmount,
            a.Currency,
            a.Reason,
            a.CreatedAt,
            a.DecidedAt,
            a.DecisionNote,
            a.GivenOn,
            MonthsOf(a, given, CurrentMonth, forPerson: true),
            a.Status == SalaryAdvanceStatus.Approved ? (a.SettledOn is null ? a.Amount - repaid : 0) : null,
            a.SettledKind,
            a.SettledOn,
            a.CancelledAt);
    }

    /// <summary>
    /// The months planned, and any a shortfall moved on to. What each took is left out of the person's own list: in a
    /// short month it would show what they were paid.
    /// </summary>
    private static IReadOnlyList<SalaryAdvanceMonthResponse> MonthsOf(SalaryAdvance a, GivenAdvance? given, DateOnly currentMonth, bool forPerson)
    {
        var taken = given?.Schedule.Months.ToDictionary(m => m.Month, m => m.Taken) ?? new Dictionary<DateOnly, decimal>();
        return a.Repayments.Select(r => r.Month)
            .Concat(taken.Keys)
            .Distinct()
            .Order()
            .Select(month => new SalaryAdvanceMonthResponse(
                month,
                a.Repayments.Where(r => r.Month == month).Sum(r => r.Amount),
                forPerson || a.Status != SalaryAdvanceStatus.Approved || month > currentMonth ? null : taken.GetValueOrDefault(month),
                month < currentMonth))
            .ToList();
    }

    private static bool HasLeft(User person)
        => person.Status == UserStatus.Deactivated
            || (person.EmploymentPeriods.Count > 0 && person.EmploymentPeriods.All(p => p.EndedOn is not null));

    private static DateOnly? LeftOn(User person)
        => HasLeft(person) ? person.EmploymentPeriods.Select(p => p.EndedOn).Max() : null;

    private static string NameOf(User user) => PersonName.Of(user.FirstName, user.LastName, user.Email);

    private static string? Clean(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    /// <summary>The activity row every step writes: company level, without the amount, as pay changes are.</summary>
    private static ActivityEvent Event(SalaryAdvance advance, string kind, long actorId, DateTime at) => new()
    {
        DomainCode = null,
        Kind = kind,
        OccurredAt = at,
        ActorId = actorId,
        SubjectType = "user",
        SubjectId = advance.UserId,
        Payload = JsonSerializer.Serialize(new { advanceId = advance.Id, status = advance.Status.ToString(), settledKind = advance.SettledKind?.ToString() }),
    };

    /// <summary>Never an amount (rule N5): the page it opens checks permissions as usual.</summary>
    private static NotificationDraft Draft(string kind, long actorId, SalaryAdvance advance, string? name)
    {
        object payload = name is null ? new { } : new { name };
        return new NotificationDraft(kind, actorId, null, null, "salary_advance", advance.Id, payload);
    }
}
