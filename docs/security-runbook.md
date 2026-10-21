# Security runbook: what to do when…

Short answers for the security problems you are most likely to meet, with where to look and what to run. How each
mechanism works is in [authentication.md](authentication.md), [authorization.md](authorization.md),
[client-portal.md](client-portal.md), [proton.md](proton.md) and [files.md](files.md); the audit behind most of it is
[security-audit.md](security-audit.md).

## Where to look first

- **The API's security log:** warnings in the category `ID_Indetails.api.Security` (`Authorization/SecurityLog.cs`):
  refused sign-ins with the user id or IP, accounts paused after wrong passwords, refused rate limits (429) and refresh
  tokens presented twice (theft). On the server: `docker compose logs api | grep ID_Indetails.api.Security`.
- **Proton's log:** category `ID_Indetails.api.Proton`: every tool call, refusals as warnings, one line per question
  with its cost. Question and answer text never go to the logs.
- **Proton's audit:** `GET /api/proton/actions/all` (`proton_budget:manage`) lists everyone's last 50 proposed changes,
  who proposed them and what became of them.
- **Health:** `GET /api/health` answers when the API and its database are up.

## A staff member can't sign in

1. **Paused after wrong passwords?** Five wrong passwords in a row pause sign-in for that account for 10 minutes, from
   any device (`Business/Auth/SignInLockout.cs`). While paused, even the right password gets the same "Email or password
   is incorrect" as an unknown email, on purpose. The security log says `User {id} paused after repeated wrong passwords`.
2. **The fix: they reset their password** ("Forgot password" on the sign-in page). A successful reset ends the pause at
   once (`PasswordResetService.ResetAsync`), signs them out everywhere else, and lets them in with the new password.
   Waiting 10 minutes also works.
3. **The same account paused again and again** means someone is guessing its password on purpose. The reset still
   gets the person in; their new password is what protects them. Look at the IPs in the security log; the sign-in
   endpoints are already limited to 10 attempts a minute per IP.
4. **They can't reach their email to reset:** a super admin can clear the pause on the database (the server's MySQL,
   as the schema owner):

   ```sql
   UPDATE users SET failed_sign_ins = 0, locked_until = NULL WHERE email = 'person@example.com';
   ```

5. **Not paused, and still refused:** they may be deactivated (Employees page), their sessions may have been ended (see
   below), or they are typing an address that isn't their sign-in address.

## A client can't sign in to the client portal

- Ten wrong passwords pause a portal account for 15 minutes (`Portal:LockoutAttempts`, `Portal:LockoutMinutes`). A
  password reset from the portal's sign-in page ends the pause, as for staff.
- No project left: an account whose last project access ended is signed out and sees no project. Staff invite it again.

## A device, a password or a session may be stolen (staff)

1. **End their sessions:** on the person's page in Employees, or `POST /api/employees/{id}/sessions/end` (needs
   `session:manage`). Every session ends at once, on the website and the phone app, access tokens included
   (`users.sessions_valid_from`), and their sign-in stays blocked until someone restores it
   (`POST /api/employees/{id}/sessions/restore`). Use it first: it is instant and undoable.
2. **Then a new password:** they reset it from their email (which signs them out everywhere too), or change it in their
   profile.
3. **If the person themselves is the problem:** deactivate them (Employees). Their tokens are refused from then on.
4. **What the system already does on its own:** access tokens last 15 minutes; a refresh token presented twice is
   treated as theft and ends every session of that person (after a 30-second grace for two tabs refreshing at once).

## A client portal account may be misused

- **End its access to a project:** the project's Client portal tab, or `POST /api/projects/{id}/portal/access/{accessId}/revoke`.
  The project disappears from the client's portal at once; if it was their last project they are signed out.
- **The person no longer works for the client:** take their email off the client (Clients page). That ends their access
  to all of the client's projects and cancels their pending invitations in the same save.
- **The whole client:** archiving the client ends every contact's access; restoring gives nothing back.

## Proton is doing something it shouldn't

Fastest first:

1. **Switch it off:** set `PROTON_ENABLED=false` in the server's `.env` and restart the API
   (`docker compose up -d api`). Proton answers `503 proton_unavailable`; nothing else changes. Voice alone:
   `PROTON_VOICE_ENABLED=false`.
2. **Or switch off only its changes:** `Proton:Actions:Enabled=false` stops every proposal; each family (`Money`,
   `Messaging`, `Deletes`, `Access`, `CompanyRules`, `PayRules`, `Lifecycle`) has its own switch, and a card waiting when
   its family is switched off is refused at Confirm.
3. **One person:** revoke their `proton:use` permission.
4. **Find out what happened:** the Proton audit (`GET /api/proton/actions/all`) and the `proton_actions` table hold every
   proposal with its exact arguments, what the card showed, who confirmed it and the outcome. Messages Proton sent carry
   `messages.proton_action_id`.
5. **Spending too fast:** the monthly budget stops everyone once used; the budget page shows who spent what. Also keep a
   spend limit on the Anthropic workspace as a backstop.

## A secret may have leaked

Change it at its source first, then in the server's `.env`, then restart what uses it (`docker compose up -d api`).

| Secret (`.env`) | Change it at | What changing it does |
|---|---|---|
| `JWT_SIGNING_KEY` | anything long and random | access tokens signed with the old key stop working. Sessions continue (refresh tokens are kept in the database), so browsers just refresh. To force people out, end their sessions |
| `PROTON_API_KEY` | the Anthropic console | Proton keeps working with the new key |
| `PROTON_VOICE_API_KEY` | the OpenAI project | voice keeps working with the new key; running voice sessions may drop |
| SMTP password | the mail provider | email keeps working |
| content store keys (R2) | Cloudflare | uploads and downloads keep working; links already handed out expire within minutes |
| `MYSQL_API_PASSWORD` | `.env` only (letters and digits), then deploy | each deployment sets the API user's password from `.env` (`deploy/api-db-user.sh`), and the API connects with it |
| `SETUP_SUPER_ADMIN_KEY` | leave it **empty** | with it empty, the setup endpoint doesn't exist. Set it only while creating the first super admin |
| `WATERMARK_KEY` | **don't change it** | pictures marked with the old key can no longer be read. A leaked key lets someone read or strip the mark, nothing more |

## Someone is overloading the server

- Every endpoint is rate limited per signed-in person, or per IP (`429 too_many_requests`, in the security log).
- Uploads: photos up to 10 MB and 50 megapixels (bigger ones are refused before they are read), videos up to 100 MB,
  and 2 GB per person per day. The API container is limited to 2 GB of memory, so if it runs out, it is the API that
  restarts (in seconds), never MySQL.
- A burst from one IP: block it at the server's firewall (`ufw deny from <ip>`); Caddy sees the real address.

## Before a risky deployment

Some migrations are destructive: they can't be rolled back without the backup. Before deploying one, run
`./backup.sh` on the server by hand (see [deployment.md](deployment.md)). A deployment restarts the stack in place, so
expect up to a minute of errors while it does.
