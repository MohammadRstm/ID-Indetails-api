# Authentication and accounts

There is **no self-signup**. A developer creates the first super admin. After that, every account is created by invitation.

Clients of Design projects sign in to the client portal with separate accounts, tokens and cookies: see
[client-portal.md](client-portal.md). Nothing below applies to them.

## Endpoints

| Method and path | Access | Purpose |
|---|---|---|
| `POST /api/setup/super-admin` | `X-Setup-Key` header | Creates a super admin with a generated password, emailed to them |
| `POST /api/auth/login` | Anyone | Email and password in. Returns the access token and sets the refresh cookie |
| `POST /api/auth/refresh` | Refresh cookie | Rotates the refresh cookie and returns a new access token |
| `POST /api/auth/logout` | Refresh cookie | Revokes the refresh token and clears the cookie |
| `POST /api/auth/change-password` | Signed in (allowed while a temporary password is pending) | Sets a new password and signs out every other session |
| `POST /api/auth/change-email` | `employee:change_email` | `{ email, currentPassword }`. Their own new sign-in address; signs out every other session ([employees.md](employees.md#changing-a-sign-in-address)) |
| `POST /api/auth/forgot-password` | Anyone | `{ email }`. Emails a reset link to an active account with that email. Always `204` |
| `POST /api/auth/reset-password` | Anyone with a link | `{ token, password }`. Sets the new password and signs out everywhere. `204` |
| `GET /api/auth/me` | Signed in (allowed while a temporary password is pending) | Current user, effective permissions, `isJoker` and `visibleDomains` |
| `POST /api/invitations` | `employee:invite` or `employee:invite_super_admin` | Invites someone by email |
| `POST /api/invitations/{userId}/resend` | `employee:invite` or `employee:invite_super_admin` | Emails a fresh link; the previous one stops working |
| `POST /api/invitations/validate` | Anyone with a link | What the signup page shows (email, role, domain, team, position) |
| `POST /api/invitations/accept` | Anyone with a link | Sets name, phone and password, activates the account, signs in |
| `POST /api/mobile/auth/login` | Anyone | The phone app's sign-in: `{ email, password }`. Returns both tokens in the body (see [Mobile sessions](#mobile-sessions)) |
| `POST /api/mobile/auth/refresh` | The refresh token | `{ refreshToken }`. Rotates it and returns new tokens |
| `POST /api/mobile/auth/logout` | Anyone | `{ refreshToken }`. Ends this phone's session and removes its push device. Always `204` |
| `POST /api/mobile/auth/change-password` | Signed in (allowed while a temporary password is pending) | `{ currentPassword, newPassword }`. Signs out every other session; this phone gets new tokens |

Every request counts against a global limit: 300 a minute per signed-in user, or 60 a minute per IP without a valid access token. On top of it:

- Login (the website's and the phone's), forgot and reset password and the two public invitation endpoints: 10 a minute per IP, together (`auth`).
- Refresh (the website's and the phone's): 60 a minute per IP on its own (`refresh`), since every page load refreshes and an office shares one address; the refresh token itself is the secret.
- Change password (on either) and change sign-in address, which check the current password: 10 a minute per person (`credential`).
- Requests that email an address the caller chooses (invoice and statement emails, the invoice plan, invitations and resends, a new sign-in address): 10 a minute per person (`email`).
- Exports and PDFs: 10 a minute per person (`export`). Uploads: 30 a minute per person. Bug reports: 5 an hour per person. Setup: 5 an hour per IP.

All limits are in the `RateLimiting` configuration section. Going over answers `429 too_many_requests` with a `Retry-After` header, and is logged as a warning under the `ID_Indetails.api.Security` category with the caller (user id, or IP when signed out). Refused sign-ins are logged there too, with the IP only.

## Creating the first super admin

The endpoint only works with the setup key from configuration (`Setup:SuperAdminKey`). Without the right key, every request answers `404`, so the endpoint doesn't reveal that it exists. Locally the key is in user secrets:

```bash
dotnet user-secrets list --project ID-Indetails.api
```

```bash
curl -k -X POST https://localhost:7187/api/setup/super-admin -H "Content-Type: application/json" -H "X-Setup-Key: <key>" -d "{\"email\":\"director@company.com\",\"firstName\":\"Rana\",\"lastName\":\"Haddad\",\"position\":\"Managing Director\"}"
```

The response contains the user id and email, never the password. The generated password is emailed (locally it lands in Mailpit at http://127.0.0.1:8025).

At first sign-in the account has `mustChangePassword: true`. Every endpoint except `change-password`, `me` and `logout` then answers `403 password_change_required` until the password is changed.

## Invitations

1. **Invite.** Someone allowed to invite calls `POST /api/invitations` with `email`, `role`, `domainCode`, and optionally `teamId`, `position` and `pay`. `pay` is required for an admin or super admin, as a monthly salary: they are paid a fixed salary and log no hours (see [employees.md](employees.md#pay)). The person's `users` row is created with `status = invited` and no password.
2. **Email.** They receive a link to `{Frontend:BaseUrl}{Frontend:AcceptInvitationPath}?token=…`. It is valid for 7 days (`Invitations:ExpirationDays`) and works once. If the email can't be sent, nothing is saved.
3. **Signup page.** The client reads `token` from the URL, shows the details from `POST /api/invitations/validate`, and submits `POST /api/invitations/accept` with the token, first name, last name, optional phone and a password. The account becomes active and the person is signed in.

Who may invite whom is decided by permissions, not roles (see `Business/Auth/InvitationPolicy.cs` and [authorization.md](authorization.md)):

| Inviting | Needs | Into |
|---|---|---|
| Super admins | `employee:invite_super_admin` | No domain |
| Admins and employees | `employee:invite` | A domain the inviter can see. `domainCode` may be omitted when they see only one |

By default only super admins hold `employee:invite_super_admin`, and super admins and admins hold `employee:invite`. Both can be granted to anyone.

An unknown, used, revoked or expired link always answers `404 invitation_invalid` with the same message.

## Forgotten passwords

1. **Ask.** The sign-in page's "Forgot password?" link leads to a form that calls `POST /api/auth/forgot-password` with the email. The answer is always an empty `204`: for an unknown email, an invited account that hasn't signed up, a deactivated account, and an email that couldn't be sent alike. Only active accounts get an email, and the email is sent without the answer waiting for it, so neither the answer nor its timing says whether the email belongs to anyone.
2. **Email.** The link goes to `{Frontend:BaseUrl}{Frontend:ResetPasswordPath}?token=…`. It works once and expires after 30 minutes (`PasswordReset:ExpirationMinutes`). Asking again makes earlier links stop working. Only the token's SHA-256 hash is stored, in `password_reset_tokens`.
3. **Reset.** The page reads `token` from the URL and submits `POST /api/auth/reset-password` with the token and a new password (the same length rules as everywhere). The password is replaced, a pending temporary password no longer needs changing, every other link of the account stops working, and every refresh token is revoked, so the account is signed out everywhere. The person then signs in with the new password.

An unknown, used, expired or replaced link, or one whose account has since been deactivated, always answers `404 password_reset_invalid` with the same message. The rules are in `Business/Auth/PasswordResetRules.cs`.

## Tokens

### Access token

A JWT signed with HMAC-SHA256, valid for **15 minutes** (`Jwt:AccessTokenMinutes`); the client refreshes it silently when it runs out. Send it as `Authorization: Bearer <token>`. Claims:

| Claim | Value |
|---|---|
| `sub` | User id |
| `email` | Email |
| `given_name`, `family_name` | First and last name |
| `role` | `super_admin`, `admin` or `employee` |
| `domain` | `design` or `execution`. Absent for super admins |
| `team` | Team (department) name. Absent if none |
| `position` | Job title. Absent if none |
| `must_change_password` | `"true"` until a generated password is replaced |
| `sid` | The phone app's session id. Only on tokens issued to the app; push devices are tied to it |

Permissions are **not** in the token, and the `role` claim is informational only. The server checks permissions against the database on every request, so a permission change or automatic role change applies immediately (see [authorization.md](authorization.md)). The client gets them from `/me` and the login response.

### Refresh token

- **Lifetime:** each refresh token lasts 1 day (`RefreshToken:ExpirationDays`). Every refresh replaces it, so an active session keeps going and an idle one ends after a day.
- **Cookie:** `idi_refresh`, with `HttpOnly`, `Secure`, `SameSite=Strict` and `Path=/api/auth`. JavaScript can't read it, and it's only sent to the auth endpoints.
- **Storage:** a random token is generated and discarded. The cookie carries its SHA-256 hash, and the database stores the SHA-256 of the cookie value. Nothing in the database can be replayed as a cookie.
- **Reuse detection:** presenting a refresh cookie that was rotated more than 30 seconds ago revokes **all** of that user's sessions, because the old cookie may have been stolen. Within those 30 seconds (an answer lost on a bad connection, two tabs at once) it is only refused (`Business/Auth/RefreshTokenRules.cs`).
- **Other revocations:** changing or resetting the password and logging out revoke refresh tokens too.
- **Sessions and reasons:** each sign-in starts a session (`refresh_tokens.session_id`) that its refreshes continue. Every token records which app it belongs to (`client`: `web` or `mobile`) and, once it stops working, why (`revoke_reason`): `rotated` (refreshed), `superseded` (see Mobile sessions), `signed_out`, or `revoked` (a password change or reset, a super admin, deactivation, a new sign-in address, theft). Tokens from before this have neither.
- **Each app's tokens work only there:** a phone's token sent as the cookie, or a cookie sent to the phone's endpoints, is refused like an unknown one and ends nothing.

### Mobile sessions

The phone app can't use the cookie, so it has its own endpoints (`Controllers/MobileAuthController`). They run the same checks as the website (the same answer and timing for unknown emails, `sign_in_suspended`, inactive accounts, password rehashing, last seen) and differ only in transport, lifetime and replays.

- **Tokens in the body.** Login, refresh and change-password answer `{ accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt, user }`; `user` is what `/me` returns. The app keeps the refresh token in the phone's secure store.
- **30 days since last use.** Each refresh moves the session's end to 30 days ahead (`MobileSession:RefreshTokenDays`), so a phone used once a month stays signed in; there is no absolute cap. The access token is the same 15-minute token, with a `sid` claim.
- **Several phones.** Each sign-in on a phone is its own session.
- **Signing out** ends that session only, and removes its push device ([me.md](me.md#my-devices)). An unknown token answers `204` too. Changing the password on the phone ends every other session and keeps the phone's session id with a new refresh token.

**Replays.** A refresh can succeed while its answer is lost on a weak connection; the phone then presents the token it still holds. What happens depends on the token presented, P (`Business/Auth/MobileRefreshRules.cs`):

| P is | Answer |
|---|---|
| Unknown or expired | `401 refresh_token_invalid` |
| Valid | A normal rotation: P becomes `rotated`, a new token in the same session is returned |
| `rotated`, and no token after it was ever presented, within 5 minutes of its rotation (`MobileSession:ReplayGraceSeconds`) | A lost answer. The session's newest, never presented token becomes `superseded` and a new one is returned. Repeated retries in the window work the same way. Logged at Information |
| `rotated`, as above, but the session already reached its end | `401 refresh_token_invalid`, nothing else |
| `rotated`, as above, after the 5 minutes | A stale replay: `401 refresh_token_invalid` and **only this session** ends (`revoked`). Logged as a warning |
| `rotated`, and a token after it was itself presented | Two holders: theft. Every session of the person ends, everywhere, access tokens included (`sessions_valid_from`). Logged as a warning |
| `superseded` | Theft, as above: only someone other than the phone that retried can hold it |
| `signed_out` or `revoked` | `401 refresh_token_invalid`, nothing else |

Warnings and the retries go to the `ID_Indetails.api.Security` log category. Claiming a token (rotating P, or superseding the newest) is one conditional update, so of two refreshes at once only one gets a new token; the other gets `401 refresh_token_reused`. The app sends one refresh at a time.

A stolen token is still caught: used after the phone moved on, it is "two holders"; used before, the phone's next refresh presents the superseded token, which ends everything. The 5-minute window only exists for phone sessions, and only for a token whose successors were never used.

## Using it from the React client

- **Send cookies.** Call the auth endpoints with credentials (`withCredentials: true` in axios) so the browser sends and stores the refresh cookie.
- **Run the API behind the Vite dev proxy.** Proxy `/api` to `https://localhost:7187`, so the browser sees one origin. With `SameSite=Strict`, the cookie is not sent from `http://localhost:5173` to `https://localhost:7187` directly, because those count as different sites. In production, serve the client and API from the same site.
- **Keep the access token in memory.** On page load, call `POST /api/auth/refresh` to get a new one. If it fails, show the login page.
- **Refresh once at a time.** Several tabs or requests refreshing with the same cookie at the same moment get one new session and refusals for the rest. Let one refresh run and have other requests wait for it.
- **React to the error `code`,** not the message. For example, `password_change_required` means "go to the change-password screen".

## Errors

Every error has the same shape:

```json
{ "type": "authentication", "code": "invalid_credentials", "message": "Email or password is incorrect. After several wrong attempts, sign-in pauses for a few minutes.", "errors": null, "traceId": "…" }
```

**Sign-in pause.** After **5 wrong passwords** in a row for one account, website and phone-app sign-in for it pauses
for **10 minutes** (`Business/Auth/SignInLockout.cs`), whatever IP the attempts come from. While paused, even the right
password gets the same `invalid_credentials` answer as an unknown email. The count is kept on the user row
(`users.failed_sign_ins`, `users.locked_until`) and raised in a single statement, so wrong passwords sent at once all
count; a successful sign-in resets it. Signed-in sessions are not affected. Pauses go to the security log.

`type` decides the HTTP status: `validation` 400, `authentication` 401, `authorization` 403, `not_found` 404, `conflict` 409, `business_rule` 422, `rate_limited` 429, `unavailable` 503, `unexpected` 500. `errors` holds field messages for validation failures. Unexpected errors never include internal details; use `traceId` to find them in the server logs.

## Configuration

| Setting | Default | Notes |
|---|---|---|
| `Jwt:SigningKey` | — | **Secret, at least 32 characters.** The API won't start without it |
| `Setup:SuperAdminKey` | empty | **Secret.** When empty, the setup endpoint is disabled |
| `Jwt:Issuer` / `Jwt:Audience` | `id-indetails-api` / `id-indetails-client` | |
| `RefreshToken:SameSite` | `Strict` | `None` only if the client is ever on another site |
| `RefreshToken:ExpirationDays` | `1` | The website's refresh tokens |
| `MobileSession:RefreshTokenDays` | `30` | How long a phone stays signed in since its last refresh (1 to 90) |
| `MobileSession:ReplayGraceSeconds` | `300` | How long after a rotation the phone may present the old token again (0 to 900) |
| `Frontend:BaseUrl` | `http://localhost:5173` | Used for links in emails |
| `Frontend:ResetPasswordPath` | `/reset-password` | The client's page for choosing a new password |
| `PasswordReset:ExpirationMinutes` | `30` | How long a reset link works (5 to 1440) |
| `Cors:AllowedOrigins` | `http://localhost:5173` | Origins allowed to call the API with credentials |
| `Email:*` | Mailpit on `127.0.0.1:1025` | Put SMTP credentials in user secrets |
| `Company:TimeZone` | `Asia/Beirut` | Decides "today" for join dates and pay dates |
| `RateLimiting:*` | 10 per minute / 5 per hour | |

Set the secrets locally with:

```bash
dotnet user-secrets set "Jwt:SigningKey" "<64+ random characters>" --project ID-Indetails.api
```

```bash
dotnet user-secrets set "Setup:SuperAdminKey" "<random key>" --project ID-Indetails.api
```

## Known limitations

- **Access tokens end with the session.** A password reset, deactivation, ended sessions, a replayed refresh token and a new sign-in address set by someone else refuse every access token issued before (`users.sessions_valid_from`, `Business/Auth/SessionRules.cs`). Changing your own password or sign-in address does the same for your other devices and keeps the one you used. The live connection closes when its token expires, so it follows within 15 minutes. These also cancel any pending password-reset link.
- **No "forgot password" flow yet.** The `password_reset_tokens` table is ready for it.
- **No invitation revoke endpoint.** An invitation can be resent (which cancels the old link) but not cancelled on its own. That belongs with the employee-management feature.
