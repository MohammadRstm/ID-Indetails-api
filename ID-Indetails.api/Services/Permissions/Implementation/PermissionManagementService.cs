using System.Text.Json;
using ID_Indetails.api.Authorization;
using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Request.Permissions;
using ID_Indetails.api.DTO.Response.Permissions;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Exceptions.Base;
using ID_Indetails.api.Models;
using ID_Indetails.api.Repositories.Activity.Abstraction;
using ID_Indetails.api.Repositories.Auth.Abstraction;
using ID_Indetails.api.Repositories.Common.Abstraction;
using ID_Indetails.api.Services.Auth.Abstraction;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Notifications.Abstraction;
using ID_Indetails.api.Services.Permissions.Abstraction;

namespace ID_Indetails.api.Services.Permissions.Implementation;

public sealed class PermissionManagementService(
    ICurrentUser currentUser,
    IUserRepository users,
    IPermissionRepository permissionRepository,
    IPermissionService permissions,
    IActivityEventRepository activity,
    INotificationSender notifications,
    IUnitOfWork unitOfWork,
    IClock clock) : IPermissionManagementService
{
    private const string UserNotFoundMessage = "User not found.";

    public async Task<IReadOnlyList<PermissionDefinitionResponse>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var all = await permissionRepository.GetAllAsync(cancellationToken);
        var grants = await permissions.GetGrantsByRoleAsync(cancellationToken);

        return all
            .Select(p => new PermissionDefinitionResponse(
                p.Code,
                p.Description,
                p.DomainCode,
                RoleCodes.All.Where(r => grants.TryGetValue(r, out var g) && g.Any(x => x.Code == p.Code)).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<HeldPermissionResponse>> GetMineAsync(CancellationToken cancellationToken = default)
    {
        // an access token outlives a deactivation
        var me = await users.GetRoleSnapshotAsync(currentUser.Id, cancellationToken) is { Status: UserStatus.Active } user
            ? user
            : throw new UnauthenticatedException(ErrorCodes.Unauthenticated, "Your session has expired. Sign in again.");
        var mine = await DescribeAsync(me.Id, me.RoleCode, me.DomainCode, previousRole: null, cancellationToken);
        var held = mine.Permissions.ToHashSet();
        var fromRole = mine.RoleDefaults.ToHashSet();

        return (await permissionRepository.GetAllAsync(cancellationToken))
            .Where(p => held.Contains(p.Code))
            .Select(p => new HeldPermissionResponse(p.Code, p.Description, fromRole.Contains(p.Code)))
            .ToList();
    }

    public async Task<UserPermissionsResponse> GetForUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var user = await users.GetRoleSnapshotAsync(userId, cancellationToken);

        // Someone in a domain the caller cannot see reads as not found (no cross-domain peeking).
        if (user is null || !actor.CanSee(user.DomainCode))
        {
            throw new NotFoundException(UserNotFoundMessage);
        }

        return await DescribeAsync(user.Id, user.RoleCode, user.DomainCode, previousRole: null, cancellationToken);
    }

    public async Task<UserPermissionsResponse> UpdateAsync(
        long userId,
        UpdateUserPermissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await permissions.GetAccessAsync(currentUser.Id, cancellationToken);
        var catalog = await permissions.GetCatalogAsync(cancellationToken);

        var unknown = request.Changes.Select(c => c.Code).Where(code => catalog.All(p => p.Code != code)).ToList();
        if (unknown.Count > 0)
        {
            throw new RequestValidationException(
                ErrorCodes.UnknownPermission, "changes", $"Unknown permission: {string.Join(", ", unknown)}.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // Locked until commit: concurrent changes to the same person are applied one at a time,
        // so the automatic role check always sees the final set.
        var user = await users.GetByIdForUpdateAsync(userId, cancellationToken);
        if (user is null || !actor.CanSee(user.DomainCode))
        {
            throw new NotFoundException(UserNotFoundMessage);
        }

        var denial = PermissionChangePolicy.Check(
            actor, user.Id, user.RoleCode, user.Status, request.Changes.Select(c => c.Code));
        if (denial is not null)
        {
            throw denial.Kind == PermissionChangePolicy.DenialKind.BusinessRule
                ? new BusinessRuleException(denial.Code, denial.Message)
                : new ForbiddenException(denial.Code, denial.Message);
        }

        var grants = await permissions.GetGrantsByRoleAsync(cancellationToken);
        var existing = await permissionRepository.GetOverridesAsync(user.Id, forUpdate: true, cancellationToken);

        var plan = PermissionChangePlanner.Build(
            user.RoleCode,
            user.DomainCode,
            catalog,
            grants,
            existing.Select(o => new EffectivePermissions.Override(o.PermissionCode, o.Effect)),
            request.Changes.Select(c => new PermissionChangePlanner.Change(c.Code, c.Action)));

        var now = clock.UtcNow;
        var reasons = request.Changes.ToDictionary(c => c.Code, c => string.IsNullOrWhiteSpace(c.Reason) ? null : c.Reason.Trim());

        SyncOverrides(user.Id, existing, plan.Overrides, reasons, actor.UserId, now);
        RecordEvents(user, plan, reasons, actor.UserId, now);
        await NotifyAsync(user, plan, actor.UserId, cancellationToken);

        if (plan.PreviousRole is not null)
        {
            user.RoleCode = plan.Role;
            user.UpdatedAt = now;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        permissions.Forget(user.Id);

        return await DescribeAsync(user.Id, plan.Role, user.DomainCode, plan.PreviousRole, cancellationToken);
    }

    /// <summary>Makes the stored overrides match the plan, touching only rows that changed.</summary>
    private void SyncOverrides(
        long userId,
        IReadOnlyList<UserPermissionOverride> existing,
        IReadOnlyDictionary<string, PermissionEffect> planned,
        IReadOnlyDictionary<string, string?> reasons,
        long actorId,
        DateTime now)
    {
        foreach (var row in existing)
        {
            if (!planned.TryGetValue(row.PermissionCode, out var effect))
            {
                permissionRepository.RemoveOverride(row);
            }
            else if (row.Effect != effect)
            {
                row.Effect = effect;
                row.Reason = reasons.GetValueOrDefault(row.PermissionCode);
                row.GrantedById = actorId;
                row.UpdatedAt = now;
            }
        }

        var existingCodes = existing.Select(o => o.PermissionCode).ToHashSet(StringComparer.Ordinal);
        foreach (var (code, effect) in planned.Where(p => !existingCodes.Contains(p.Key)))
        {
            permissionRepository.AddOverride(new UserPermissionOverride
            {
                UserId = userId,
                PermissionCode = code,
                Effect = effect,
                Reason = reasons.GetValueOrDefault(code),
                GrantedById = actorId,
                CreatedAt = now,
            });
        }
    }

    private void RecordEvents(
        User user,
        PermissionChangePlanner.Plan plan,
        IReadOnlyDictionary<string, string?> reasons,
        long actorId,
        DateTime now)
    {
        foreach (var change in plan.Applied)
        {
            var kind = change.Action switch
            {
                PermissionChangeAction.Grant => ActivityEventKinds.PermissionGranted,
                PermissionChangeAction.Revoke => ActivityEventKinds.PermissionRevoked,
                _ => ActivityEventKinds.PermissionReset,
            };

            activity.Add(UserEvent(user, kind, actorId, now,
                new { permission = change.Code, reason = reasons.GetValueOrDefault(change.Code) }));
        }

        if (plan.PreviousRole is not null)
        {
            var kind = plan.Role == RoleCodes.Admin ? ActivityEventKinds.RoleAutoPromoted : ActivityEventKinds.RoleAutoDemoted;
            activity.Add(UserEvent(user, kind, actorId, now, new { from = plan.PreviousRole, to = plan.Role }));
        }
    }

    /// <summary>Tells the person what changed: one notification for the permissions, one for an automatic role change.</summary>
    private async Task NotifyAsync(User user, PermissionChangePlanner.Plan plan, long actorId, CancellationToken cancellationToken)
    {
        List<string> Codes(PermissionChangeAction action) => plan.Applied.Where(c => c.Action == action).Select(c => c.Code).ToList();

        if (plan.Applied.Count > 0)
        {
            await notifications.SendAsync(
                new NotificationDraft(NotificationKinds.PermissionsChanged, actorId, null, null, "user", user.Id,
                    new
                    {
                        granted = Codes(PermissionChangeAction.Grant),
                        revoked = Codes(PermissionChangeAction.Revoke),
                        reset = Codes(PermissionChangeAction.Reset),
                    }),
                [user.Id],
                cancellationToken);
        }

        if (plan.PreviousRole is not null)
        {
            await notifications.SendAsync(
                new NotificationDraft(NotificationKinds.RoleChanged, actorId, null, null, "user", user.Id,
                    new { from = plan.PreviousRole, to = plan.Role, domain = user.DomainCode, automatic = true }),
                [user.Id],
                cancellationToken);
        }
    }

    private static ActivityEvent UserEvent(User user, string kind, long actorId, DateTime now, object payload) => new()
    {
        DomainCode = user.DomainCode,
        Kind = kind,
        OccurredAt = now,
        ActorId = actorId,
        SubjectType = "user",
        SubjectId = user.Id,
        Payload = JsonSerializer.Serialize(payload),
    };

    private async Task<UserPermissionsResponse> DescribeAsync(
        long userId,
        string roleCode,
        string? domainCode,
        string? previousRole,
        CancellationToken cancellationToken)
    {
        var catalog = await permissions.GetCatalogAsync(cancellationToken);
        var grants = await permissions.GetGrantsByRoleAsync(cancellationToken);
        var roleGrants = grants.TryGetValue(roleCode, out var g) ? g : [];
        var overrides = roleCode == RoleCodes.SuperAdmin
            ? []
            : await permissionRepository.GetOverridesAsync(userId, cancellationToken: cancellationToken);

        var access = EffectivePermissions.Resolve(
            userId,
            roleCode,
            domainCode,
            catalog,
            roleGrants,
            overrides.Select(o => new EffectivePermissions.Override(o.PermissionCode, o.Effect)));

        return new UserPermissionsResponse
        {
            UserId = userId,
            Role = roleCode,
            Domain = domainCode,
            IsLocked = access.IsSuperAdmin,
            IsJoker = access.IsJoker,
            VisibleDomains = access.VisibleDomains,
            RoleDefaults = access.IsSuperAdmin
                ? access.Permissions
                : EffectivePermissions.RoleDefaults(roleCode, domainCode, roleGrants).Order(StringComparer.Ordinal).ToList(),
            Permissions = access.Permissions,
            Overrides = overrides
                .Select(o => new PermissionOverrideResponse(o.PermissionCode, o.Effect, o.Reason, o.GrantedById, o.UpdatedAt ?? o.CreatedAt))
                .ToList(),
            PreviousRole = previousRole,
        };
    }
}
