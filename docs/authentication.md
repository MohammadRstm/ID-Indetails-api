# Authentication and accounts

There is **no self-signup**. A developer creates the first super admin. After that, every account is created by invitation.

## Endpoints

| Method and path | Access | Purpose |
|---|---|---|
| `POST /api/setup/super-admin` | `X-Setup-Key` header | Creates a super admin with a generated password, emailed to them |
| `POST /api/auth/login` | Anyone | Email and password in. Returns the access token and sets the refresh cookie |
| `POST /api/auth/refresh` | Refresh cookie | Rotates the refresh cookie and returns a new access token |
| `POST /api/auth/logout` | Refresh cookie | Revokes the refresh token and clears the cookie |
| `POST /api/auth/change-password` | Signed in (allowed while a temporary password is pending) | Sets a new password and signs out every other session |
| `GET /api/auth/me` | Signed in (allowed while a temporary password is pending) | Current user and effective permissions |
| `POST /api/invitations` | `employee:invite` | Invites someone by email |
| `POST /api/invitations/{userId}/resend` | `employee:invite` | Emails a fresh link; the previous one stops working |
| `POST /api/invitations/validate` | Anyone with a link | What the signup page shows (email, role, domain, team, position) |
| `POST /api/invitations/accept` | Anyone with a link | Sets name, phone and password, activates the account, signs in |

Login, refresh and the two public invitation endpoints are rate-limited per IP: 10 per minute by default. The setup endpoint allows 5 per hour.

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

1. **Invite.** A super admin or admin calls `POST /api/invitations` with `email`, `role`, `domainCode`, and optionally `teamId`, `position` and `pay`. The person's `users` row is created with `status = invited` and no password.
2. **Email.** They receive a link to `{Frontend:BaseUrl}{Frontend:AcceptInvitationPath}?token=…`. It is valid for 7 days (`Invitations:ExpirationDays`) and works once. If the email can't be sent, nothing is saved.
3. **Signup page.** The client reads `token` from the URL, shows the details from `POST /api/invitations/validate`, and submits `POST /api/invitations/accept` with the token, first name, last name, optional phone and a password. The account becomes active and the person is signed in.

Who may invite whom (see `Business/Auth/InvitationPolicy.cs`):

| Inviter | May invite | Into |
|---|---|---|
| Super admin | super admins, admins, employees | Either domain. Super admins get no domain |
| Admin | admins, employees | Their own domain only. `domainCode` may be omitted |
| Employee | no one | — |

An unknown, used, revoked or expired link always answers `404 invitation_invalid` with the same message.

## Tokens

### Access token

A JWT signed with HMAC-SHA256, valid for **3 hours** (`Jwt:AccessTokenMinutes`). Send it as `Authorization: Bearer <token>`. Claims:

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

Permissions are **not** in the token. The server checks them against the database on every request, and the client gets them from `/me` and the login response.

### Refresh token

- **Lifetime:** each refresh token lasts 1 day (`RefreshToken:ExpirationDays`). Every refresh replaces it, so an active session keeps going and an idle one ends after a day.
- **Cookie:** `idi_refresh`, with `HttpOnly`, `Secure`, `SameSite=Strict` and `Path=/api/auth`. JavaScript can't read it, and it's only sent to the auth endpoints.
- **Storage:** a random token is generated and discarded. The cookie carries its SHA-256 hash, and the database stores the SHA-256 of the cookie value. Nothing in the database can be replayed as a cookie.
- **Reuse detection:** presenting a refresh cookie that was already rotated revokes **all** of that user's sessions, because the old cookie may have been stolen.
- **Other revocations:** changing the password and logging out revoke refresh tokens too.

## Using it from the React client

- **Send cookies.** Call the auth endpoints with credentials (`withCredentials: true` in axios) so the browser sends and stores the refresh cookie.
- **Run the API behind the Vite dev proxy.** Proxy `/api` to `https://localhost:7187`, so the browser sees one origin. With `SameSite=Strict`, the cookie is not sent from `http://localhost:5173` to `https://localhost:7187` directly, because those count as different sites. In production, serve the client and API from the same site.
- **Keep the access token in memory.** On page load, call `POST /api/auth/refresh` to get a new one. If it fails, show the login page.
- **Refresh once at a time.** Several tabs or requests refreshing with the same cookie at the same moment look like token reuse and sign the user out. Let one refresh run and have other requests wait for it.
- **React to the error `code`,** not the message. For example, `password_change_required` means "go to the change-password screen".

## Errors

Every error has the same shape:

```json
{ "type": "authentication", "code": "invalid_credentials", "message": "Email or password is incorrect.", "errors": null, "traceId": "…" }
```

`type` decides the HTTP status: `validation` 400, `authentication` 401, `authorization` 403, `not_found` 404, `conflict` 409, `business_rule` 422, `rate_limited` 429, `unavailable` 503, `unexpected` 500. `errors` holds field messages for validation failures. Unexpected errors never include internal details; use `traceId` to find them in the server logs.

## Configuration

| Setting | Default | Notes |
|---|---|---|
| `Jwt:SigningKey` | — | **Secret, at least 32 characters.** The API won't start without it |
| `Setup:SuperAdminKey` | empty | **Secret.** When empty, the setup endpoint is disabled |
| `Jwt:Issuer` / `Jwt:Audience` | `id-indetails-api` / `id-indetails-client` | |
| `RefreshToken:SameSite` | `Strict` | `None` only if the client is ever on another site |
| `Frontend:BaseUrl` | `http://localhost:5173` | Used for links in emails |
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

- **Access tokens can't be revoked.** When a user is deactivated or changes their password, access tokens already issued keep working until they expire (up to 3 hours). Refresh tokens stop working immediately.
- **No "forgot password" flow yet.** The `password_reset_tokens` table is ready for it.
- **No invitation revoke endpoint.** An invitation can be resent (which cancels the old link) but not cancelled on its own. That belongs with the employee-management feature.
