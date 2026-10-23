# Security and launch-readiness audit (October 2026)

Branch `MR-Security-Audit` in both repositories. Seven parallel audits (authentication, authorization and domain
isolation, database, input and uploads, infrastructure and CI/CD, error handling/logging/rate limits/caching,
dependencies/secrets/frontend), then three more authorization audits that re-traced every controller as a
cross-check, and a second-pass review of the fixes themselves (see the end). Every finding below was traced in the
code; fixes were verified with tests and builds.

Severity: **Critical** account takeover, auth bypass, cross-domain access, major compromise; **High** fix before
launch; **Medium** should be fixed; **Low** hardening; **Info** checked and fine.

## Critical

None found. No SQL injection, no unauthenticated endpoint, no cross-domain read or write by changing an id, no way
to act as another user, no secret in the repositories or their history.

## High

| # | Finding | Where | Status |
|---|---|---|---|
| H1 | Access tokens lived 3 hours and kept working after a password reset, a password change, deactivation, ended sessions (live connection) and a replayed refresh token: a stolen token outlived "sign out everywhere". | `AuthService`, `PasswordResetService`, `EmployeeService.DeactivateAsync`, `SignInEmailService`, `OnTokenValidated`, `LiveHub` | **Fixed.** Tokens last 15 minutes; those events set `sessions_valid_from` (your own change keeps the session you used); the live hub closes on expiry. A replayed refresh token signs everyone out only after a 30-second grace, so a lost answer or two tabs refreshing at once isn't taken for theft. Verified: `AccessTokenRevocationTests`, `RefreshTokenTests`. |
| H2 | Inviting or re-roling people skipped "hand out only what you hold": anyone given `employee:invite` could invite their own second mailbox as an admin; `employee:manage` holders could promote each other and wipe revoked permissions. | `InvitationPolicy`, `EmployeeRules.CheckRoleHandOut`, `EmployeeService.ChangeRoleAsync`/`MoveAsync` | **Fixed** (your decision): the inviter/changer must hold every default of the target role, for a role change and for a move to the other domain alike; a change whose new defaults include a hand-revoked permission needs `permission:manage` (a demotion that doesn't give it back doesn't). Consequence: only super admins can invite super admins. Verified: `PrivilegeAndPrivacyTests`, `PrivilegeRulesTests`, `PermissionTests`. |
| H3 | Login, refresh, password reset and invitations shared one 10-a-minute bucket per IP: an office behind one address could lock everyone out at the morning rush. | `AuthServiceCollectionExtensions` | **Fixed.** Refresh has its own 60/min per IP. Verified: `RefreshRateLimitTests`. |
| H4 | No database backup before migrating, and four past migrations are destructive, so a rollback across them (or a migration failing half-way) can only be repaired from the nightly backup. The docs said migrations "only add". | `deploy/deploy.sh`, `Data/Migrations` | **Open (held by you).** Docs corrected; they now say to run `./backup.sh` by hand before a deployment with a destructive migration. Automating it is to be decided with the VPS disk in mind. |
| H5 | Deployment is not blue-green: one stack restarted in place (about 30–60 s of 502s), and a release that never becomes healthy stays live. | `deploy/deploy.sh`, `docker-compose.prod.yml` | **Open (held by you).** Docs now describe it truthfully. |

## Medium

| # | Finding | Status |
|---|---|---|
| M1 | A super admin's salary could be worked out from project labour cost (their logged hours were priced), and was counted twice (projects and company overview). | **Fixed:** their time costs projects nothing; their salary stays a company cost. Since 23 Oct 2026 this goes by the role held on the day worked, not the role today. |
| M2 | Shared task-comment cards showed a colleague's entry length to the whole team chat (breaks "colleagues' hours stay private"). | **Fixed:** length and attachment only for readers who may read the entry. |
| M3 | One permission of the other domain switches on all of a person's general permissions there. | **Open (kept for launch, your decision):** documented in `authorization.md`; the client never offers other-domain permissions. Plan: domain-aware permissions. |
| M4 | CSV formula injection in the work-entries export (a description like `=HYPERLINK(...)`). | **Fixed:** the export's CSV helper guards cells like the others. |
| M5 | Uploads had no storage quota (30 × 100 MB a minute per user, on the database's disk). | **Fixed:** 2 GB per person per 24 h. Concurrent uploads are not limited (see L-list). |
| M6 | Email-sending endpoints (invoice/statement email, invoice plans, invitations, sign-in address) had only the 300/min global limit: an insider could spam from the company's mail identity. | **Fixed:** 10 a minute per person. Recipients may still be any address (documented product behaviour). |
| M7 | Nothing watches production. | **Partly fixed:** public `GET /api/health` (status only) and a monitoring section in `deployment.md`. **External setup needed** (uptime monitor, backup heartbeat, disk alert). |
| M8 | Container logs were never rotated (disk could fill and stop MySQL). | **Fixed:** every service rotates 5 × 10 MB. |
| M9 | The API connected to MySQL as the schema owner (could alter or drop tables). | **Fixed (approved):** the API uses `MYSQL_API_USER` with SELECT/INSERT/UPDATE/DELETE only, created by each deployment (the script refuses unsafe names and root or the schema owner). **Needs `MYSQL_API_USER`/`MYSQL_API_PASSWORD` in the server's `.env` before the next deploy.** |
| M10 | A one-sided rollback rebuilt the other side from branch HEAD, migrations included. | **Fixed:** any rollback tag means nothing is built; both tags are required. |
| M11 | nginx resolved the API once at start: an API restart without a client restart gave 502s. | **Fixed:** `web` restarts with `api` (`depends_on … restart: true`). |
| M12 | `Caddyfile` changes were copied but never applied. | **Fixed:** deploy reloads Caddy (restarts it if it isn't answering yet). |
| M13 | The setup endpoint creates super admins for as long as its key is set, and the key had no strength rule. | **Fixed as decided:** production refuses a key shorter than 32 characters; it stays usable while set (empty it after use, as `server-setup.md` says). |
| M14 | Security events weren't traceable (no IP or user on refused sign-ins, nothing on 429s). | **Fixed:** warnings under `ID_Indetails.api.Security` with user id or IP; the logged path is stripped of control characters, so it can't forge a line. |
| M15 | The work-log digest read under REPEATABLE READ could miss a concurrent digest (duplicate key → 500, or a lost count). | **Fixed:** locking read. |
| M16 | EF Core 9 (and Pomelo 9) leave support on 10 Nov 2026; Pomelo 10 isn't out. | **Decided:** stay on the newest EF Core 9 patch and keep the driver current (MySqlConnector 2.6.2, PR #80); then try Microting's fork of Pomelo for EF Core 10. Oracle's provider was set aside: it would mean a new driver and rebuilding the migration history. |
| M17 | Deploy can build any ref, rebuilds instead of shipping the CI-tested image, and `CLIENT_REPO_TOKEN` is a repository secret. | **Open:** restrict refs to `master`/`main`/tags, move the token to an environment (GitHub settings). |

## Low (fixed)

Change password/email checked the current password without a strict limit (now 10/min per person) · pending reset links survived a password or sign-in address change (now cancelled) · a refresh that failed on a server error cleared the cookie (now only when refused) · invitations could set pay without `employee:pay_manage` · people could excuse their own hours or set their own schedule · work summaries and project owners exposed super admins to single-domain callers · super admins' pay was readable without a company permission (now `salary:view`) · project pay listed people the caller can't see · old notifications stayed readable after losing the permission they were sent under · hour excuses and schedules appeared in the domain activity feed · adding a comment needed only domain visibility · the project report printed client contacts without `client:view` · sorting by hours ranked projects/clients without `work_entry:view_all` · "my projects" ordered colleagues by time spent · creating an Execution site set its pricing without `project:pricing_manage` · meetings of closed projects could be deleted · a hand-recorded email delivery could redirect reminders to a non-address · a year-1 company overview period priced 24,000 months · email validation accepted `?bcc=` and spaces · the project icon (the client's one `innerHTML` key) was free text · explicit JSON `null`s and huge page numbers gave 500s · pay accepted any 3-letter currency and unrounded amounts · dates had no range · JSON request bodies could be 30 MB (now 1 MB; uploads 101 MB on `/api/files` only) · conversation read-marker race · a stray cancellation could stop the background jobs · production allowed SMTP without TLS and MailKit's 2-minute timeout · API answers had no `Cache-Control: no-store`/`nosniff` · reset and invitation tokens stayed in the address bar and history · the dev-only kit page shipped in the bundle · `.gitignore` missed `.env.*`, `appsettings.*.json` and key files.

## Low / Info (not changed, with reasons)

- Forgot-password timing differs slightly for real accounts (rate-limited; accepted).
- An accepted invitation isn't re-checked against the inviter's current rights (product choice).
- The permission cache can briefly re-cache old access in a narrow race (in-process, single instance; accepted).
- Caddy's error log can include a request URL (with the live connection's token) during upstream failures; a log filter wasn't added because Caddy config can't be tested locally. Tokens now expire in 15 minutes.
- Floating base-image tags (`mysql:8.4`, `caddy:2.11-alpine`); Dependabot has no security-only mode for Docker. Images get base patches on every rebuild.
- Concurrent uploads per person aren't limited (the daily quota bounds storage; RAM-backed `/tmp` can still be pressured).
- Write endpoints returning records to holders of narrow custom permissions (`invoice:send` without `financial:view`).
- Site costs visible without `financial:view` (documented product decision).
- Uploaded photos keep EXIF data (location).
- Framework 404/405/415 bodies aren't in the uniform contract; logs are plain text without scopes.
- Activity feed counts the whole table for super admins; some dashboards load full history (fine at launch volume).
- nginx still resolves the API once at start: `restart: true` covers restarts made by compose, not ones Docker makes by itself after a crash (the API recovers; `docker compose restart web` clears the 502s). Monitoring catches it.
- Caddy's `encode` compresses API answers too; nothing secret is reflected next to user input, so BREACH-style attacks don't apply today.
- Sign-in and forgot-password look accounts up by any address with an `@` (not the strict format), so addresses saved before the stricter rule still sign in.
- Server hardening (SSH key restrictions, firewall, unattended upgrades, off-site backups) is outside the repositories.

## Verified secure (Info)

Token validation (HS256, issuer, audience, lifetime, key from environment ≥ 32 chars) · permissions read from the
database per request, never from token claims · fail-closed default policy and a unit test that every endpoint
declares its access · refresh tokens 256-bit, hashed, rotated atomically with reuse detection · refresh cookie
`HttpOnly; Secure; SameSite=Strict; Path=/api/auth` · CSRF not applicable (bearer header; the cookie endpoints are
SameSite=Strict) · CORS empty in production, https-only validated · PBKDF2 password hashing with dummy verification
against enumeration · reset/invitation tokens 256-bit, hashed, single use, expiring, revoked on a new link · every
domain id and child id checked in the service (404 outside the caller's domains) · raw SQL all parameterised ·
sorting by enum only · uploads typed by magic bytes (no SVG/HTML), `nosniff`, safe storage keys · emails and PDFs
encode or plain-text user content · no `dangerouslySetInnerHTML` on user data · access token in memory only, query
cache cleared on sign-out and user switch · CSP enforced, HSTS, frame denial, no source maps · GitHub Actions pinned
by SHA, least-privilege tokens, no secrets in PR workflows, pinned SSH host key · containers non-root, read-only, no
published ports besides Caddy · forwarded headers trusted from the proxy subnet only · no vulnerable NuGet or npm
packages.

## Launch readiness checklist

| Area | Result |
|---|---|
| Authentication | PASS |
| Authorization / RBAC | PASS WITH NOTES (cross-domain permission spread, M3) |
| Multi-tenancy (domain isolation) | PASS WITH NOTES (M3) |
| SQL injection / database access | PASS |
| XSS / output encoding | PASS |
| Input validation | PASS |
| CSRF | PASS (not applicable to bearer auth; cookie endpoints SameSite=Strict) |
| CORS | PASS |
| Security headers | PASS |
| Cookies | PASS |
| Rate limiting / abuse | PASS WITH NOTES (no per-account login throttle; upload concurrency) |
| Tokenized links | PASS |
| Error handling | PASS |
| Database indexing / performance | PASS WITH NOTES (full-history loads at scale) |
| Logging | PASS WITH NOTES (plain text, no log shipping) |
| Monitoring / alerting | FAIL until the external monitor is set up (M7) |
| Report a bug | PASS |
| Secrets / configuration | PASS WITH NOTES (`MYSQL_API_USER` to add on the server) |
| Dependencies | PASS WITH NOTES (EF Core 9 after 10 Nov 2026 until the move to EF Core 10, M16) |
| File uploads / storage | PASS WITH NOTES (backups on the same disk) |
| Cache security | PASS |
| API security | PASS |
| Frontend security | PASS |
| CI/CD security | PASS WITH NOTES (M17) |
| Blue-green / rollback | FAIL (H4, H5: held for a decision) |
| Security testing | PASS |

## Second pass

A separate review of every fix above found these, all fixed and tested before this branch was opened:

| Found | Fix |
|---|---|
| Hiding people you can't see from project pay crashed the page whenever a super admin had logged time in the period (a missing key). | Their entries are dropped with them. Test: `Project_pay_leaves_out_people_the_reader_cant_see`. |
| Moving someone to the other domain skipped the "hand out only what you hold" check that a role change has. | Same check on a move. Test: `Moving_someone_to_the_other_domain_needs_its_permissions`. |
| The revoked-permission check refused any role change for someone with a revoked permission, even a demotion. | It refuses only when the new role's defaults include a revoked permission. |
| A refresh token sent twice in a row (two tabs, a lost answer) signed the person out everywhere. | 30-second grace before a replay counts as theft; a concurrent rotation is just refused. |
| Your own password change could sign out the session that made it, late in a second. | The cut-off keeps the whole current second. |
| The strict email format on sign-in could lock out accounts saved before it. | Sign-in and forgot-password check only for an `@`. |
| The bug report address was fixed in code. | `BUG_REPORTS_TO` in the server's `.env` overrides it (default: the developer's address). |
| The database health check could pass on MySQL's first-start temporary server. | It checks over TCP. |
| The client cut email links at `#` and `&`, which real addresses can contain. | Only `?` (a query) is cut. |
