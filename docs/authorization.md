# Authorization and permissions

Endpoints never ask for a role. They ask for **permissions**. A role only supplies a person's *default* permissions, and super admins (or anyone they delegate to) can give individual people more or fewer.

## Where permissions are defined

| File | Holds |
|---|---|
| `Constants/PermissionCodes.cs` | Every permission code (`resource:action`) |
| `Constants/PermissionCatalog.cs` | Each permission's description, the domain it is limited to (if any), and the **role defaults** |

The catalogue is seeded into `permissions` and `role_permissions`. Role defaults are fixed and can't be edited from the app; changing them is a migration and needs approval like any database change.

| Role | Defaults |
|---|---|
| Super admin | Every permission, in both domains and at company level |
| Admin | Everything except the company permissions (`company:view`, `overhead:manage`, `salary:view`, `company_settings:manage`, `working_time:manage`, `schedule:manage`, `hours:excuse`, `employee:invite_super_admin`, `permission:manage`, `exchange_rate:manage`) |
| Employee | `project:view`, `requirement:view`, `meeting:create`, `work_entry:create`, `work_entry:view_own`, `material:create`, `labour_report:create` |

A permission limited to a domain (for example `material:create` for Execution) is only part of a role's defaults for people of that domain.

## How a user's permissions are worked out

`Business/Auth/EffectivePermissions.cs`, in this order:

1. **Super admin:** every permission. Nothing can be added or removed, and personal changes never apply.
2. **Everyone else:** the role's defaults for their home domain,
3. plus their **grant** overrides, minus their **revoke** overrides (`user_permission_overrides`).

The result is a `UserAccess`: the permissions, whether the user is a joker, and the domains they can see.

- **Joker:** an admin or employee whose permissions differ from their role defaults. Calculated, never stored.
- **Visible domains:** the user's home domain, plus the other domain as soon as they hold one of its domain-limited permissions (for example a Design admin granted `project:pricing_manage`). From then on their data is no longer limited to one domain; *what* they can do in it is still decided by the permissions they hold. The other domain's role defaults are **not** switched on; each permission must be granted.
- **Deactivated accounts** hold nothing.

## Changing someone's permissions

| Method and path | Access | Purpose |
|---|---|---|
| `GET /api/permissions` | `permission:manage` | Every permission, its description, domain and the roles that hold it by default |
| `GET /api/users/{userId}/permissions` | `permission:manage` | Role defaults, overrides, effective permissions, joker flag and visible domains |
| `PUT /api/users/{userId}/permissions` | `permission:manage` | Applies a list of changes in one go |

```json
{ "changes": [
  { "code": "salary:view", "action": "grant", "reason": "Handles payroll" },
  { "code": "invoice:void", "action": "revoke" },
  { "code": "client:manage", "action": "reset" }
] }
```

`grant` and `revoke` set whether the person holds the permission; `reset` returns it to the role default. Only differences from the role defaults are stored, so granting something the role already gives, or resetting everything, leaves no overrides and the person is no longer a joker.

Rules (`Business/Auth/PermissionChangePolicy.cs`):

- A super admin's permissions can't be changed (`403 permissions_locked`).
- Nobody changes their own permissions (`403 permission_change_not_allowed`).
- Deactivated accounts can't be changed (`422 user_deactivated`). Invited ones can, so access is ready when they sign up.
- `permission:manage` is itself grantable, so super admins can **delegate**. A delegate who isn't a super admin may only grant, revoke or reset permissions they hold themselves (`403 permission_change_not_allowed`), so nobody can hand out more than they have.
- A person in a domain the caller can't see reads as `404`.
- Unknown permission codes answer `400 unknown_permission`.

Every change that actually changed something writes an `activity_events` row (`permission_granted`, `permission_revoked`, `permission_reset`), with the target as the subject, the caller as the actor, and the permission and reason in the payload.

## Automatic role changes

When a change leaves an admin or employee holding **exactly** the default permissions of the other role in their domain, they take that role at once:

- An employee granted every admin default (and nothing else) becomes an **admin**.
- An admin revoked down to exactly the employee defaults becomes an **employee**.

Their overrides are cleared, because the new role's defaults now describe them, so they are no longer a joker. The change writes `role_auto_promoted` or `role_auto_demoted`, and the response's `previousRole` says what they were. Nobody is ever moved into or out of the super admin role automatically; super admins are only created by invitation.

The check runs only when someone's permissions are changed, never on ordinary requests. It compares two small sets in memory inside the same transaction. The person's `users` row is locked (`SELECT … FOR UPDATE`) for the duration, so two simultaneous changes to the same person are applied one after the other and the check always sees the final set.

**Manual role changes** (`PUT /api/employees/{id}/role`) and moves to the other domain clear the person's overrides, so the new role's defaults take over (see [employees.md](employees.md)).

## Protecting an endpoint

```csharp
[HasPermission(PermissionCodes.InvoiceVoid)]                  // must hold this one
[HasAnyPermission(PermissionCodes.A, PermissionCodes.B)]      // must hold at least one
```

- Both also require a signed-in user who has replaced any generated password.
- `PermissionHandler` loads the user's access **by user id from the database**, never from the role in the token. A grant, revoke or automatic role change applies on the user's very next request; the role claim in an older token is informational only.
- For finer rules (which domain, which record), services call `IPermissionService.GetAccessAsync(currentUser.Id)` and use `UserAccess.Has(...)` and `UserAccess.CanSee(domain)`. Records in a domain the caller can't see must read as not found.

## Caching

`PermissionService` caches the catalogue and role defaults (they only change by migration) and each user's access for 10 minutes. A user's cached access is dropped whenever their overrides or role change through the API. Anything that changes a user's role, domain or status must call `IPermissionService.Forget(userId)`; the people-management endpoints already do.

The cache is in-process. Running more than one API instance would need a shared cache or a per-user version stamp.
