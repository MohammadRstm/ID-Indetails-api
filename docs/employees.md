# People and teams

People management covers everyone with an account: super admins, admins and employees. Accounts are created by invitation (see [authentication.md](authentication.md)); this page covers what happens after that. The rules live in `Business/Employees/EmployeeRules.cs` and `Business/Pay/PayChangePolicy.cs`.

## Who can see whom

- Callers see people whose home domain is one of their **visible domains** (their own, plus the other once they hold one of its permissions; see [authorization.md](authorization.md)).
- **Super admins** belong to no domain. They are visible only to callers who see both domains.
- Anyone else reads as `404`, for every endpoint below.

## Endpoints

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/employees` | `employee:view` | The people list (below) |
| `GET /api/employees/{id}` | `employee:view` | A profile. `currentPay` is included only for callers with `financial:view` or `employee:pay_manage` |
| `PUT /api/employees/{id}` | `employee:manage` | Replaces first and last name, phone, position and team |
| `PUT /api/employees/{id}/photo`, `DELETE …` | `employee:manage` | `{ fileId }`: their profile photo, from the caller's upload ([files.md](files.md)) |
| `POST /api/employees/{id}/deactivate` | `employee:manage` | `{ lastWorkingDay }` (optional, today when empty): blocks sign-in, ends every session and ends their employment on that day |
| `POST /api/employees/{id}/reactivate` | `employee:manage` | `{ payGap }` (only when they left earlier this month): undoes a deactivation |
| `PUT /api/employees/{id}/email` | `employee:change_email` | `{ email }`: a new sign-in address (below) |
| `POST /api/employees/{id}/sessions/end` | `session:manage` | Signs them out everywhere and blocks their sign-in until it is restored. Never one's own |
| `POST /api/employees/{id}/sessions/restore` | `session:manage` | Lets them sign in again; the sessions that were ended stay ended |
| `PUT /api/employees/{id}/role` | `employee:manage` | Changes the role |
| `PUT /api/employees/{id}/domain` | `employee:manage` | Moves someone to the other domain |
| `GET /api/employees/{id}/pay` | `employee:pay_manage` | Pay history, newest first |
| `POST /api/employees/{id}/pay` | `employee:pay_manage` | Adds a pay change |
| `PUT /api/me/profile` | Signed in | Own first name, last name and phone |
| `GET /api/teams` | Signed in | Teams of the caller's visible domains, with member counts |
| `POST /api/teams` | `team:manage` | Creates a team |
| `PUT /api/teams/{id}` | `team:manage` | Renames a team |
| `DELETE /api/teams/{id}` | `team:manage` | Deletes an empty team |

Every change writes an `activity_events` row (`employee_updated`, `employee_deactivated`, `employee_reactivated`, `employee_email_changed`, `role_changed`, `employee_moved`, `pay_changed`, `team_created`, `team_renamed`, `team_deleted`). Pay events never carry amounts, because the activity feed is readable without financial access.

## The people list

`GET /api/employees?search=&teamId=&role=&domain=&status=&page=1&pageSize=25`

- `search` matches first name, last name, email and position; every word must match. Case is ignored.
- Deactivated people are left out unless `status=deactivated` is asked for. Invited people are included.
- Sorted by first name, then last name, then email (invited people without a name last). `pageSize` is at most 100.
- Returns `{ items, page, pageSize, totalCount }`. Each item has the person's role, domain, team, position, status, `isJoker`, joined date and last seen.

## Editing people

- Admins edit name, phone, position and team. Names are required once someone has signed up; an invited person's may stay empty. The team must belong to the person's domain.
- People edit their own name and phone (`PUT /api/me/profile`). Email, position, team and pay are managed by the company. Their own pay, hours, overtime, schedule and requirements are in [me.md](me.md). Their profile's work, overtime and hours balance, as others see them, are in [design.md](design.md).
- Only **super admins** change sign-in addresses (below).

## Changing a sign-in address

Whoever holds `employee:change_email` (super admins by default) sets someone's new address with `PUT /api/employees/{id}/email`, and their own with `POST /api/auth/change-email` (`{ email, currentPassword }`, [authentication.md](authentication.md)), never their own through the first. It takes effect at once (`Services/Employees/Implementation/SignInEmailService.cs`):

- The address must be new (`422 email_unchanged`) and nobody else's, invited or not (`409 email_already_registered`). A deactivated account's isn't changed (`422 user_deactivated`).
- The person's sessions end: refresh tokens are revoked and the access tokens they hold are refused from their next request, as when their sessions are ended, but their sign-in isn't suspended. Changing your own keeps only the session you are using.
- The new address is emailed inside the transaction, so if it can't be reached nothing changes (`503 email_delivery_failed`). The old address is then told it no longer signs in, without the new one, since whoever reads that mailbox now may not be the person. A failure there is only logged: a mailbox that no longer works is often the reason for the change.
- Someone still **invited** gets neither email: the link sent to the old address stops working and the invitation is sent again to the new one. If that email fails the address is still changed, and the `503` says to resend the invitation.
- The `employee_email_changed` event holds no addresses, because the activity feed is read more widely than the people list.

## Rules that protect accounts

- Changing a super admin's account in any way (profile, pay, status, role, sign-in address, sessions) also needs `employee:invite_super_admin`.
- Nobody deactivates, re-roles, moves or sets the pay of their own account, excuses their own hours or sets their own schedule (`403 employee_change_not_allowed`): all of these decide their own pay.
- Giving someone a role, or moving them to the other domain, needs every default of that role there; when those defaults include a permission taken from them by hand it also needs `permission:manage` ([authorization.md](authorization.md)).
- A super admin's pay is company-level: it shows (and `GET /api/employees/{id}/pay` answers) only with `salary:view` or `employee:invite_super_admin`. Their logged hours cost projects nothing; their salary is counted once, on the company overview.
- The **last active super admin** can never be deactivated or given another role, so the company always has one. Every active super admin's row is locked while this is checked, so two requests can't each remove "the other one".

## Deactivating and reactivating

Employment is kept as dated periods (`employment_periods`): accepting the invitation starts the first one, leaving ends it, and being taken back starts a new one. Pay, the hours ledger and project pay count only the days inside a period (`Business/Payroll/Employment.cs`, `Business/Employees/EmploymentRules.cs`).

- **Deactivating** sets the status to `deactivated`, records when (`users.deactivated_at`), and ends the person's period on their **last working day**: today unless given, never in the future (`400 last_working_day_invalid`) or before the period started, and never so far back that a closed month would change (`422 month_closed`; on the 1st, last month is still open). That day is paid. It also revokes every refresh token and any pending invitation link, and drops the cached permissions. The person's access token stops working at once: permission checks find no permissions, and `/auth/me` answers `401`. Their history is kept, and `leftOn` shows the last working day.
- **Reactivating** clears `deactivated_at` and makes them `active` again if they had signed up, or `invited` if they never did (send them a fresh link with `POST /api/invitations/{id}/resend`). For someone who worked here:
  - back the day after their last working day (or the same day): their period simply goes on;
  - away since earlier **this month**: `payGap` is required (`422 pay_gap_required`). `true` keeps their period going and excuses the days away as paid, so the hours ledger doesn't count them short; `false` starts a new period today, and the days away aren't paid;
  - away since an earlier month: a new period starts today and the time away is never paid (`payGap` is refused, `422 pay_gap_not_asked`).

## Changing a role

`PUT /api/employees/{id}/role` with `{ "role": "admin" }`:

- Between admin and employee the domain stays. To change domain as well, move them separately.
- Making someone a super admin removes their domain and team.
- A super admin becoming an admin or employee needs `domainCode` (a domain the caller can see).
- Making or unmaking a super admin needs `employee:invite_super_admin`.
- **Personal permission changes are cleared**, so the new role's defaults take over. This is the manual counterpart of the automatic role changes described in [authorization.md](authorization.md).

## Moving to the other domain

`PUT /api/employees/{id}/domain` with `{ "domainCode": "execution", "teamId": 7 }`:

- Only callers who see both domains can move people.
- The team is replaced (it must belong to the new domain, or be empty).
- Personal permission changes are cleared, because they were made for the old domain.
- Super admins have no domain and can't be moved.

## Pay

`POST /api/employees/{id}/pay` with `{ "basis": "monthly", "amount": 1200, "currency": "USD", "effectiveFrom": "2026-06-01", "note": "Raise" }`:

- `effectiveFrom` defaults to today in the company time zone and can never be in the future.
- It must be **after the latest existing change**. The history is only ever added to at the end, so nothing is ever rewritten or silently cancelled. A mistake is corrected with a new, later change.
- A change dated in the past still re-prices work done since that day, because salaries and labour costs are always calculated from the history.
- For monthly pay, the internal hourly cost is salary ÷ 173.33 (a 40-hour week; the break is paid). It is stored with the row so past project costs never change.

## Teams

- Each team belongs to one domain, and names are unique within a domain (ignoring case).
- The prototype's teams are seeded (`Constants/TeamSeed.cs`):
  - Design: Engineering, Design, Data, Marketing, Operations, Platform.
  - Execution: Site Operations, Procurement, Quantity Surveying, Safety, Project Management.
- A team can only be deleted while nobody is in it, including deactivated people (`422 team_not_empty`), and while its group chat has no messages (`422 team_has_chat`), so chat history is never lost. Rename it instead.
- `POST /api/teams` may leave out `domainCode` when the caller works in only one domain.

## Not built yet

- Profile photos (waiting for file storage).
