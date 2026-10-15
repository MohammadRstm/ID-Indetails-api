# Client portal: accounts, invitations and access (Phase 1)

A Design project's client can follow the project in a client portal. Phase 1 covers:
- client accounts;
- invitations;
- the clients' own sign-in;
- access per project.

The portal's content (presentations, 3D and drawings, publishing, comments, chat and notifications to clients) comes in
later phases.

**Execution projects have no client portal.** Their endpoints answer 404, and the permission is limited to Design.

## Identity: separate from staff

Clients are not staff users. They never appear in staff lists, contacts, notifications, search, presence or Proton,
because:
- **Separate tables:** clients live in `portal_accounts`. The `users` table is untouched.
- **One account per email**, unique among portal accounts. One account can have access to several projects
  (`portal_project_access`).
- **Their own token scheme** (`Portal`), with the same issuer and key but its own audience (`Portal:Audience`, default
  `id-indetails-portal`):
  - Staff endpoints validate the staff audience, so they refuse a client's token.
  - Portal endpoints (`[Authorize(Policy = "Portal")]`) authenticate only the portal scheme, so they refuse a staff token.
- **No staff user id in a client's token:**
  - A portal token carries the account id as `portal_account` and **no `sub`**.
  - Everything that reads a staff user id from `sub` therefore finds none: permissions, last seen, presence, the per-user
    rate limit and the security log.
  - The portal scheme also refuses any token that has a `sub`.
- **Own refresh cookie:**
  - `idi_portal_refresh`: HttpOnly, Secure, `SameSite=Strict`, `Path=/api/portal/auth`, 1 day (`Portal:RefreshTokenDays`).
  - The staff cookie stays on `/api/auth`. Neither is ever sent to the other's endpoints, so a staff member and a client
    can be signed in in the same browser.
- **Rotation and reuse detection:** refresh rotates the token, as for staff on the web. Presenting a rotated token more
  than 30 seconds later ends every session of the account.
- **Ending sessions:**
  - Each token is checked against `portal_accounts.sessions_valid_from` and the account's status on every request.
  - A password reset, or the end of the account's last project access, signs it out everywhere: it sets that cut-off and
    revokes the refresh tokens.

## Who can invite

- **Permission:** `client_access:manage`, limited to Design. It is a default for admins (effective only for Design admins)
  and for super admins, and can be granted like any other.
- **What it covers:**
  - listing a project's client contacts and where each stands;
  - inviting;
  - resending;
  - cancelling an invitation;
  - ending access.
- **It is checked twice:** by the endpoint attribute and again in `PortalAccessService`.
- **Who can be invited:**
  - only an **email contact of the project's client**, so invitations never go to arbitrary addresses;
  - on a Design client project that isn't archived, whose client isn't archived.

## Invitation flow

1. **Invite.** Staff call `POST /api/projects/{id}/portal/invitations { email }`.
   - **New address:** an `invited` account is created, and an invitation with a 256-bit token valid **24 hours**
     (`Portal:InvitationExpirationHours`) is emailed.
   - **No password is ever emailed.** Only the token's SHA-256 is stored.
   - The email is sent inside the transaction: if it can't be delivered, nothing is saved (503).
2. **The client opens the link** (`/portal/invite?token=…`). `POST /api/portal/invitations/validate` shows the email and the
   project, or why the link can't be used:
   - `404 portal_invitation_invalid`: no such link;
   - `422 portal_invitation_expired`;
   - `422 portal_invitation_revoked`: cancelled, or replaced by a resend;
   - `422 portal_invitation_used`: the account is already set up, so sign in.
3. **The client sets up the account.** `POST /api/portal/invitations/accept { token, firstName, lastName, password }`:
   - The password follows the staff rules (10–128 characters, PBKDF2 hashing).
   - The token is claimed atomically, so it can be used **once**.
   - The account becomes `active` and gets access to the project, plus any other project it had a pending invitation to.
   - The client is signed in.
4. **An address that already has an active account** gets access to the new project at once, and an email saying so. No
   second account and no new password.
5. **Resend** (`…/invitations/{id}/resend`) replaces the link; the old one reads as revoked. **Cancel**
   (`…/invitations/{id}/revoke`) stops a pending link.
6. **End access** (`…/portal/access/{id}/revoke`): the project disappears from the client's portal at once, and their
   pending invitations to it are cancelled. If it was their last project, they are signed out everywhere.
7. **Moving the project to another client, or making it internal**, ends every client's access to it.

## Client sign-in

All of these are under `/api/portal/auth`:
- `login`
- `refresh` (cookie)
- `logout`
- `forgot-password` (always 204; the link goes to `/portal/reset-password` and lasts 30 minutes)
- `reset-password` (signs out everywhere)

Errors:
- Unknown emails and accounts not set up yet get the same answer and the same timing as a wrong password, through a dummy
  hash.
- After **10 wrong passwords** in a row, sign-in pauses for **15 minutes** (`Portal:LockoutAttempts`,
  `Portal:LockoutMinutes`). During the pause even the right password gets the same generic answer, so the pause can't be
  used to test passwords.
- Rate limits: the per-IP `auth` limit applies to login, invitations and resets; `refresh` to refresh; staff invitation
  emails use the per-person `email` limit.

`GET /api/portal/me` (portal token only) returns the account and the Design projects it may open.

## Endpoints

| Method and path | Who | |
|---|---|---|
| `GET /api/projects/{id}/portal/clients` | `client_access:manage` | The client's email contacts and their status: `not_invited`, `invited`, `invitation_expired`, `active`, `access_ended` |
| `POST /api/projects/{id}/portal/invitations` | `client_access:manage`, `email` limit | `{ email }` |
| `POST /api/projects/{id}/portal/invitations/{invitationId}/resend` | same | |
| `POST /api/projects/{id}/portal/invitations/{invitationId}/revoke` | `client_access:manage` | |
| `POST /api/projects/{id}/portal/access/{accessId}/revoke` | `client_access:manage` | |
| `POST /api/portal/invitations/validate`, `/accept` | anyone with the link, `auth` limit | |
| `POST /api/portal/auth/login`, `/refresh`, `/logout`, `/forgot-password`, `/reset-password` | the client | |
| `GET /api/portal/me` | portal token | |

Proton never calls any of these: they are `Never` in `ProtonCoverage`.

## Audit

- **Activity feed.** Invitations, resends, cancellations, acceptance and access granted or ended are written to
  `activity_events`, with the project, the client and `subject_type = portal_account`. They appear under Projects in the
  Design activity feed.
  - An acceptance has no staff actor.
  - These events count as project activity for auto-archive.
- **Security log.** Sign-in refusals, pauses and reused refresh tokens go to the security log
  (`ID_Indetails.api.Security`).

## Tables (migration `ClientPortalAccounts`)

All foreign keys restrict deletion; nothing cascades.

### `portal_accounts`
- Columns: `email` (unique), `first_name`, `last_name`, `password_hash`, `status` (`invited` / `active`), `sessions_valid_from`, `failed_sign_ins`, `locked_until`, `activated_at`, `last_seen_at`, `created_at`, `created_by_id`, `updated_at`.
- Foreign key: `created_by_id` → `users`.
- CHECK: an active account has a password and a name.

### `portal_project_access`
- Columns: `account_id`, `project_id`, `granted_at`, `granted_by_id`, `revoked_at`, `revoked_by_id`.
- Foreign keys: `account_id` → `portal_accounts`, `project_id` → `projects`, `granted_by_id` and `revoked_by_id` → `users`.
- Unique on `(account_id, project_id)`: ending access sets `revoked_at`, and giving it again reuses the row. The history is in the activity feed.

### `portal_invitations`
- Columns: `account_id`, `project_id`, `token_hash` (unique), `sent_at`, `expires_at`, `accepted_at`, `revoked_at`, `invited_by_id`.
- Foreign keys: `account_id` → `portal_accounts`, `project_id` → `projects`, `invited_by_id` → `users`.
- Indexes on `(account_id, project_id)` and on `project_id`.

### `portal_refresh_tokens`
- Columns: `account_id`, `token_hash` (unique), `session_id`, `created_at`, `expires_at`, `revoked_at`, `replaced_by_id` (self), `revoke_reason`.
- Foreign keys: `account_id` → `portal_accounts`, `replaced_by_id` → `portal_refresh_tokens`.

### `portal_password_reset_tokens`
- Columns: `account_id`, `token_hash` (unique), `created_at`, `expires_at`, `used_at`.
- Foreign key: `account_id` → `portal_accounts`.

### Seed
- The permission `client_access:manage` (domain `design`), in the admin and super admin role defaults.

### Rollback
- `Down` drops the five tables and removes the seed rows. Any portal accounts are lost.
- The migration only adds, so an older API runs on the newer schema.

## Configuration (`Portal` section)

| Key | Default |
|---|---|
| `Audience` | `id-indetails-portal` |
| `InvitationExpirationHours` | 24 |
| `PasswordResetExpirationMinutes` | 30 |
| `RefreshTokenDays` | 1 |
| `CookieName` | `idi_portal_refresh` |
| `CookiePath` | `/api/portal/auth` |
| `LockoutAttempts` | 10 |
| `LockoutMinutes` | 15 |

The email links use `Frontend:PortalInvitationPath` (`/portal/invite`), `PortalLoginPath` (`/portal/login`) and
`PortalResetPasswordPath` (`/portal/reset-password`).

## Known limits (later phases)
- **Rate limiting:** the global per-user limit counts a client by IP. The portal token isn't the default scheme, so a
  signed-in client looks anonymous to the limiter.
- **Archived projects** still appear to their clients. Read-only behaviour comes with the content phases.
- **No client notifications yet.** Publishing notifications come in a later phase.
