using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Business.Common;
using ID_Indetails.api.Business.Employees;
using ID_Indetails.api.Business.Pay;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.DTO.Request.Files;
using ID_Indetails.api.DTO.Response.Auth;
using ID_Indetails.api.DTO.Response.Common;
using ID_Indetails.api.DTO.Response.Employees;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Repositories.Employees.Abstraction;
using ID_Indetails.api.Repositories.Invitations.Abstraction;
using ID_Indetails.api.Repositories.Payroll.Abstraction;
using ID_Indetails.api.Repositories.Teams.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Employees.Abstraction;
using ID_Indetails.api.Services.Files.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Realtime.Abstraction;

namespace ID_Indetails.api.Services.Employees.Implementation;

public sealed class EmployeeService(
    ICurrentUser currentUser,
    IPermissionService permissions,
    IUserRepository users,
    IEmployeeRepository employees,
    ITeamRepository teams,
    IPermissionRepository permissionRepository,
    IRefreshTokenRepository refreshTokens,
    IInvitationRepository invitations,
    IHourExcuseRepository excuses,
    IFileClaims fileClaims,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    ISessionService sessions,
    IPresence presence,
    IClock clock) : IEmployeeService
{
    private const string NotFoundMessage = "Employee not found.";

    public async Task<PagedResponse<EmployeeSummaryResponse>> ListAsync(
        EmployeeListQuery query,
        CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.EmployeeView);
        var (rows, total) = await employees.SearchAsync(
            new EmployeeSearch(
                actor.VisibleDomains,
                IncludeSuperAdmins: EmployeeRules.CanSee(actor, null),
                query.Search?.Trim(),
                query.TeamId,
                query.Role,
                query.Domain,
                query.Status,
                query.Page,
                query.PageSize),
            cancellationToken);

        return new PagedResponse<EmployeeSummaryResponse>(rows.Select(ToSummary).ToList(), query.Page, query.PageSize, total);
    }

    public async Task<EmployeeResponse> GetAsync(long userId, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.EmployeeView, PermissionCodes.EmployeeChangeEmail);
        return await DescribeAsync(actor, userId, cancellationToken);
    }

    public async Task<EmployeeResponse> UpdateAsync(
        long userId,
        UpdateEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var user = await VisibleUserForUpdateAsync(actor, userId, cancellationToken);
        Throw(EmployeeRules.CheckCanChange(actor, TargetOf(user)));

        if (user.Status == UserStatus.Active
            && (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName)))
        {
            throw new RequestValidationException(
                ErrorCodes.ValidationFailed, "firstName", "First and last name are required once someone has signed up.");
        }

        await EnsureTeamInDomainAsync(request.TeamId, user.DomainCode, cancellationToken);

        user.FirstName = Clean(request.FirstName);
        user.LastName = Clean(request.LastName);
        user.Phone = Clean(request.Phone);
        user.Position = Clean(request.Position);
        var previousTeam = user.TeamId;
        user.TeamId = request.TeamId;
        user.UpdatedAt = clock.UtcNow;
        activity.Add(UserEvent(user, ActivityEventKinds.EmployeeUpdated, actor.UserId, null));
        if (previousTeam != user.TeamId)
        {
            await NotifySelfAsync(NotificationKinds.TeamChanged, user, actor.UserId, new { teamId = user.TeamId }, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await DescribeAsync(actor, userId, cancellationToken);
    }

    public async Task<EmployeeResponse> DeactivateAsync(long userId, DeactivateEmployeeRequest? request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // Super admins are locked first (always in this order) so the last one can't be removed
        // by two requests at once.
        var superAdmins = await employees.LockActiveSuperAdminsAsync(cancellationToken);
        var user = await VisibleUserForUpdateAsync(actor, userId, cancellationToken);
        Throw(EmployeeRules.CheckDeactivate(actor, TargetOf(user), superAdmins));

        // Their employment ends on their last working day (today unless given); someone who never started has none.
        var today = clock.CompanyToday;
        var lastDay = request?.LastWorkingDay ?? today;
        var period = await employees.LatestPeriodAsync(user.Id, cancellationToken);
        if (period is { EndedOn: null })
        {
            Throw(EmploymentRules.CheckLastWorkingDay(lastDay, period.StartedOn, today));
            period.EndedOn = lastDay;
        }

        var now = clock.UtcNow;
        user.Status = UserStatus.Deactivated;
        user.DeactivatedAt = now;
        user.UpdatedAt = now;
        user.SessionsValidFrom = now;

        // Signed out everywhere (the access tokens they hold too), and any pending signup link stops working.
        await refreshTokens.RevokeAllActiveForUserAsync(user.Id, now, cancellationToken);
        await invitations.RevokePendingForUserAsync(user.Id, now, cancellationToken);
        activity.Add(UserEvent(user, ActivityEventKinds.EmployeeDeactivated, actor.UserId, period is null ? null : new { lastWorkingDay = lastDay }));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        permissions.Forget(user.Id);

        return await DescribeAsync(actor, userId, cancellationToken);
    }

    public async Task<EmployeeResponse> EndSessionsAsync(long userId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var user = await VisibleUserForUpdateAsync(actor, userId, cancellationToken);
        Throw(EmployeeRules.CheckEndSessions(actor, TargetOf(user), alreadyEnded: user.SessionsRevokedAt is not null));

        // Refresh tokens are revoked now; access tokens are refused from their next request (SessionRules).
        var now = clock.UtcNow;
        user.SessionsRevokedAt = now;
        user.SessionsValidFrom = now;
        user.UpdatedAt = now;
        await refreshTokens.RevokeAllActiveForUserAsync(user.Id, now, cancellationToken);
        activity.Add(UserEvent(user, ActivityEventKinds.SessionsEnded, actor.UserId, null));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        permissions.Forget(user.Id);

        return await DescribeAsync(actor, userId, cancellationToken);
    }

    public async Task<EmployeeResponse> RestoreSignInAsync(long userId, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var user = await VisibleUserForUpdateAsync(actor, userId, cancellationToken);
        Throw(EmployeeRules.CheckRestoreSignIn(actor, TargetOf(user), ended: user.SessionsRevokedAt is not null));

        // SessionsValidFrom stays: whatever was issued before the sessions were ended is still refused.
        user.SessionsRevokedAt = null;
        user.UpdatedAt = clock.UtcNow;
        activity.Add(UserEvent(user, ActivityEventKinds.SignInRestored, actor.UserId, null));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        permissions.Forget(user.Id);

        return await DescribeAsync(actor, userId, cancellationToken);
    }

    public async Task<EmployeeResponse> ReactivateAsync(long userId, ReactivateEmployeeRequest? request, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var user = await VisibleUserForUpdateAsync(actor, userId, cancellationToken);
        Throw(EmployeeRules.CheckReactivate(actor, TargetOf(user)));
        if (await employees.LatestPeriodAsync(user.Id, cancellationToken) is { EndedOn: { } leftOn } period)
        {
            ComeBack(user.Id, period, leftOn, request?.PayGap, actor.UserId);
        }

        user.Status = EmployeeRules.ReactivatedStatus(hasPassword: user.PasswordHash is not null);
        user.DeactivatedAt = null;
        user.UpdatedAt = clock.UtcNow;
        activity.Add(UserEvent(user, ActivityEventKinds.EmployeeReactivated, actor.UserId, new { status = user.Status }));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        permissions.Forget(user.Id);

        return await DescribeAsync(actor, userId, cancellationToken);
    }

    /// <summary>
    /// Taking someone back. Time away that began earlier this month is paid only if the admin says so: their period
    /// then simply continues, with the days away excused (paid) so the hours ledger doesn't count them short.
    /// Otherwise a new period starts today and the days away aren't paid.
    /// </summary>
    private void ComeBack(long userId, EmploymentPeriod period, DateOnly leftOn, bool? payGap, long actorId)
    {
        var today = clock.CompanyToday;
        var asked = EmploymentRules.AsksAboutGap(leftOn, today);
        if (asked && payGap is null)
        {
            throw new BusinessRuleException(
                ErrorCodes.PayGapRequired, "They left earlier this month: say whether their time away is paid (payGap).");
        }

        if (!asked && payGap is not null)
        {
            throw new BusinessRuleException(
                ErrorCodes.PayGapNotAsked, "Only time away that began earlier this month can be paid.");
        }

        var gap = EmploymentRules.GapDays(leftOn, today).ToList();
        if (gap.Count == 0 || payGap == true)
        {
            period.EndedOn = null;
            if (gap.Count > 0)
            {
                var excuse = new HourExcuse
                {
                    UserId = userId,
                    MonthStart = new DateOnly(today.Year, today.Month, 1),
                    Kind = ExcuseKind.Days,
                    IsPaid = true,
                    Note = "Paid time away between leaving and coming back",
                    CreatedAt = clock.UtcNow,
                    CreatedById = actorId,
                };
                foreach (var day in gap)
                {
                    excuse.Days.Add(new HourExcuseDay { Day = day });
                }

                excuses.Add(excuse);
            }

            return;
        }

        employees.AddPeriod(new EmploymentPeriod { UserId = userId, StartedOn = today, CreatedAt = clock.UtcNow, CreatedById = actorId });
    }

    public async Task<EmployeeResponse> ChangeRoleAsync(
        long userId,
        ChangeRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var superAdmins = await employees.LockActiveSuperAdminsAsync(cancellationToken);
        var user = await VisibleUserForUpdateAsync(actor, userId, cancellationToken);
        var (decision, denial) = EmployeeRules.CheckRoleChange(actor, TargetOf(user), request.Role, request.DomainCode, superAdmins);
        Throw(denial);
        Throw(await CheckHandOutAsync(actor, user.Id, decision!.Role, decision.Domain, cancellationToken));
        var pay = await employees.GetPayInForceAsync(user.Id, clock.CompanyToday, cancellationToken);
        Throw(PayRules.CheckRoleChange(decision.Role, pay?.Basis));

        var previousRole = user.RoleCode;
        var previousDomain = user.DomainCode;
        user.RoleCode = decision.Role;
        user.DomainCode = decision.Domain;
        if (decision.Domain != previousDomain)
        {
            // Teams belong to a domain.
            user.TeamId = null;
        }

        // a promotion or demotion may come with a new job title; without one, the old title stays
        if (request.Position is not null)
        {
            user.Position = Clean(request.Position);
        }

        user.UpdatedAt = clock.UtcNow;

        // A manual role change always hands the person over to the new role's defaults.
        await ClearPermissionOverridesAsync(user.Id, cancellationToken);
        activity.Add(UserEvent(user, ActivityEventKinds.RoleChanged, actor.UserId,
            new { from = previousRole, to = user.RoleCode, domain = user.DomainCode }));
        await NotifySelfAsync(NotificationKinds.RoleChanged, user, actor.UserId,
            new { from = previousRole, to = user.RoleCode, domain = user.DomainCode, automatic = false }, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        permissions.Forget(user.Id);

        return (await DescribeAsync(actor, userId, cancellationToken)) with
        {
            FixedPayFrom = PayRules.FixedPayFrom(previousRole, user.RoleCode, clock.CompanyToday),
        };
    }

    public async Task<EmployeeResponse> MoveAsync(
        long userId,
        MoveEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var user = await VisibleUserForUpdateAsync(actor, userId, cancellationToken);
        Throw(EmployeeRules.CheckMove(actor, TargetOf(user), request.DomainCode));
        Throw(await CheckHandOutAsync(actor, user.Id, user.RoleCode, request.DomainCode, cancellationToken));
        await EnsureTeamInDomainAsync(request.TeamId, request.DomainCode, cancellationToken);

        var previousDomain = user.DomainCode;
        user.DomainCode = request.DomainCode;
        user.TeamId = request.TeamId;
        user.UpdatedAt = clock.UtcNow;

        // Their personal permission changes were made for the old domain.
        await ClearPermissionOverridesAsync(user.Id, cancellationToken);
        activity.Add(UserEvent(user, ActivityEventKinds.EmployeeMoved, actor.UserId,
            new { from = previousDomain, to = user.DomainCode }));
        await NotifySelfAsync(NotificationKinds.DomainChanged, user, actor.UserId, new { from = previousDomain, to = user.DomainCode }, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        permissions.Forget(user.Id);

        return await DescribeAsync(actor, userId, cancellationToken);
    }

    public async Task<IReadOnlyList<PayResponse>> GetPayHistoryAsync(long userId, CancellationToken cancellationToken = default)
    {
        var actor = (await ActorAsync(cancellationToken)).Require(PermissionCodes.EmployeePayManage);
        var row = await employees.GetAsync(userId, cancellationToken);
        if (row is null || !EmployeeRules.CanSee(actor, row.Domain))
        {
            throw new NotFoundException(NotFoundMessage);
        }

        if (!SeesPayOf(actor, row.Role))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "A super admin's pay is a company matter: it needs the permission to view salaries.");
        }

        return (await employees.GetPayHistoryAsync(userId, cancellationToken)).Select(ToPay).ToList();
    }

    public async Task<PayResponse> ChangePayAsync(
        long userId,
        ChangePayRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var user = await VisibleUserForUpdateAsync(actor, userId, cancellationToken);
        Throw(EmployeeRules.CheckPayChange(actor, TargetOf(user)));
        Throw(PayRules.CheckBasis(user.RoleCode, request.Basis));

        var effectiveFrom = request.EffectiveFrom ?? clock.CompanyToday;
        var latest = await employees.GetLatestPayDateAsync(user.Id, cancellationToken);
        Throw(PayChangePolicy.Check(effectiveFrom, clock.CompanyToday, latest));

        var pay = new UserPayHistory
        {
            UserId = user.Id,
            EffectiveFrom = effectiveFrom,
            Basis = request.Basis,
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            HourlyCostRate = request.Basis == PayBasis.Hourly ? request.Amount : 0,
            Note = Clean(request.Note),
            CreatedAt = clock.UtcNow,
            CreatedById = actor.UserId,
        };
        users.AddPayHistory(pay);

        // Amounts stay out of the activity feed, which people without financial access can read.
        activity.Add(UserEvent(user, ActivityEventKinds.PayChanged, actor.UserId, new { effectiveFrom }));
        await NotifySelfAsync(NotificationKinds.PayChanged, user, actor.UserId, new { effectiveFrom }, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToPay(pay);
    }

    public async Task<CurrentUserResponse> UpdateMyProfileAsync(
        UpdateMyProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await ActiveSelfAsync(cancellationToken);
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Phone = Clean(request.Phone);
        user.UpdatedAt = clock.UtcNow;
        activity.Add(UserEvent(user, ActivityEventKinds.EmployeeUpdated, user.Id, null));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await sessions.DescribeAsync(user, cancellationToken);
    }

    public async Task<CurrentUserResponse> SetMyPhotoAsync(AttachFileRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var user = await ActiveSelfAsync(cancellationToken);
        user.PhotoFileId = (await fileClaims.ClaimAsync(request.FileId, FileKind.Image, cancellationToken)).Id;
        user.UpdatedAt = clock.UtcNow;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await sessions.DescribeAsync(user, cancellationToken);
    }

    /// <summary>The old photo is attached to nothing any more, so the cleanup deletes it.</summary>
    public async Task<CurrentUserResponse> RemoveMyPhotoAsync(CancellationToken cancellationToken = default)
    {
        var user = await ActiveSelfAsync(cancellationToken);
        user.PhotoFileId = null;
        user.UpdatedAt = clock.UtcNow;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await sessions.DescribeAsync(user, cancellationToken);
    }

    public Task<EmployeeResponse> SetPhotoAsync(long userId, AttachFileRequest request, CancellationToken cancellationToken = default)
        => ChangePhotoAsync(userId, request.FileId, cancellationToken);

    public Task<EmployeeResponse> RemovePhotoAsync(long userId, CancellationToken cancellationToken = default)
        => ChangePhotoAsync(userId, null, cancellationToken);

    private async Task<EmployeeResponse> ChangePhotoAsync(long userId, long? fileId, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(cancellationToken);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var user = await VisibleUserForUpdateAsync(actor, userId, cancellationToken);
        Throw(EmployeeRules.CheckCanChange(actor, TargetOf(user)));
        user.PhotoFileId = fileId is { } id ? (await fileClaims.ClaimAsync(id, FileKind.Image, cancellationToken)).Id : null;
        user.UpdatedAt = clock.UtcNow;
        activity.Add(UserEvent(user, ActivityEventKinds.EmployeeUpdated, actor.UserId, new { photo = fileId is not null }));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DescribeAsync(actor, userId, cancellationToken);
    }

    /// <summary>The caller, who must still be active: an access token outlives a deactivation.</summary>
    private async Task<User> ActiveSelfAsync(CancellationToken cancellationToken)
        => await users.GetByIdAsync(currentUser.Id, cancellationToken) is { Status: UserStatus.Active } user
            ? user
            : throw new UnauthenticatedException(ErrorCodes.Unauthenticated, "Your session has expired. Sign in again.");

    /// <summary>A company-level notification to the person it is about.</summary>
    private Task NotifySelfAsync(string kind, User user, long actorId, object payload, CancellationToken cancellationToken)
        => notifications.SendAsync(new NotificationDraft(kind, actorId, null, null, "user", user.Id, payload), [user.Id], cancellationToken);

    private Task<UserAccess> ActorAsync(CancellationToken cancellationToken)
        => permissions.GetAccessAsync(currentUser.Id, cancellationToken);

    /// <summary>The person, tracked and locked; someone the caller can't see reads as not found.</summary>
    private async Task<User> VisibleUserForUpdateAsync(UserAccess actor, long userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdForUpdateAsync(userId, cancellationToken);
        return user is not null && EmployeeRules.CanSee(actor, user.DomainCode)
            ? user
            : throw new NotFoundException(NotFoundMessage);
    }

    private async Task<EmployeeResponse> DescribeAsync(UserAccess actor, long userId, CancellationToken cancellationToken)
    {
        var row = await employees.GetAsync(userId, cancellationToken);
        if (row is null || !EmployeeRules.CanSee(actor, row.Domain))
        {
            throw new NotFoundException(NotFoundMessage);
        }

        var showPay = (actor.Has(PermissionCodes.FinancialView) || actor.Has(PermissionCodes.EmployeePayManage)) && SeesPayOf(actor, row.Role);
        var pay = showPay ? await employees.GetPayInForceAsync(userId, clock.CompanyToday, cancellationToken) : null;

        return new EmployeeResponse
        {
            Id = row.Id,
            Email = row.Email,
            FirstName = row.FirstName,
            LastName = row.LastName,
            Role = row.Role,
            Domain = row.Domain,
            Team = TeamOf(row),
            Position = row.Position,
            PhotoFileId = row.PhotoFileId,
            Status = row.Status,
            IsJoker = IsJoker(row),
            JoinedOn = row.JoinedOn,
            LeftOn = row.LeftOn,
            LastSeenAt = row.LastSeenAt,
            Online = presence.IsOnline(row.Id),
            SessionsRevokedAt = row.SessionsRevokedAt,
            Phone = row.Phone,
            CreatedAt = row.CreatedAt,
            CurrentPay = pay is null ? null : ToPay(pay),
        };
    }

    private async Task EnsureTeamInDomainAsync(long? teamId, string? domainCode, CancellationToken cancellationToken)
    {
        if (teamId is not { } id)
        {
            return;
        }

        var team = await teams.GetByIdAsync(id, cancellationToken);
        if (team is null || team.DomainCode != domainCode)
        {
            throw new RequestValidationException(ErrorCodes.TeamNotInDomain, "teamId", "The team does not exist in this domain.");
        }
    }

    /// <summary><see cref="EmployeeRules.CheckRoleHandOut"/> for someone about to get <paramref name="role"/>'s defaults in <paramref name="domain"/>.</summary>
    private async Task<RuleDenial?> CheckHandOutAsync(UserAccess actor, long userId, string role, string? domain, CancellationToken cancellationToken)
    {
        var grants = await permissions.GetGrantsByRoleAsync(cancellationToken);
        var revoked = (await permissionRepository.GetOverridesAsync(userId, cancellationToken: cancellationToken))
            .Where(o => o.Effect == PermissionEffect.Revoke)
            .Select(o => o.PermissionCode);
        return EmployeeRules.CheckRoleHandOut(actor, EffectivePermissions.RoleDefaults(role, domain, grants[role]).ToList(), revoked);
    }

    private static bool SeesPayOf(UserAccess actor, string role)
        => role != RoleCodes.SuperAdmin || actor.Has(PermissionCodes.SalaryView) || actor.Has(PermissionCodes.EmployeeInviteSuperAdmin);

    private async Task ClearPermissionOverridesAsync(long userId, CancellationToken cancellationToken)
    {
        foreach (var row in await permissionRepository.GetOverridesAsync(userId, forUpdate: true, cancellationToken))
        {
            permissionRepository.RemoveOverride(row);
        }
    }

    private ActivityEvent UserEvent(User user, string kind, long actorId, object? payload) => new()
    {
        DomainCode = user.DomainCode,
        Kind = kind,
        OccurredAt = clock.UtcNow,
        ActorId = actorId,
        SubjectType = "user",
        SubjectId = user.Id,
        Payload = payload is null ? null : JsonSerializer.Serialize(payload, EventJson),
    };

    private static readonly JsonSerializerOptions EventJson = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private static void Throw(Business.Common.RuleDenial? denial)
    {
        if (denial is not null)
        {
            throw denial.ToException();
        }
    }

    private static EmployeeRules.Target TargetOf(User user) => new(user.Id, user.RoleCode, user.DomainCode, user.Status);

    /// <summary>Only differences from the role defaults are stored, so any override means a joker.</summary>
    private static bool IsJoker(EmployeeRow row) => row.Role != RoleCodes.SuperAdmin && row.HasPermissionOverrides;

    private static TeamSummaryResponse? TeamOf(EmployeeRow row)
        => row.TeamId is { } id ? new TeamSummaryResponse(id, row.TeamName ?? string.Empty) : null;

    private EmployeeSummaryResponse ToSummary(EmployeeRow row) => new()
    {
        Id = row.Id,
        Email = row.Email,
        FirstName = row.FirstName,
        LastName = row.LastName,
        Role = row.Role,
        Domain = row.Domain,
        Team = TeamOf(row),
        Position = row.Position,
        PhotoFileId = row.PhotoFileId,
        Status = row.Status,
        IsJoker = IsJoker(row),
        JoinedOn = row.JoinedOn,
        LeftOn = row.LeftOn,
        LastSeenAt = row.LastSeenAt,
        Online = presence.IsOnline(row.Id),
        SessionsRevokedAt = row.SessionsRevokedAt,
    };

    private static PayResponse ToPay(UserPayHistory p)
        => new(p.Id, p.EffectiveFrom, p.Basis, p.Amount, p.Currency, p.Note, p.CreatedAt, p.CreatedById);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
