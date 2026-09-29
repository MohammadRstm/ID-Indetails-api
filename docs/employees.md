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
| `POST /api/employees/{id}/deactivate` | `employee:manage` | Blocks sign-in and ends every session |
| `POST /api/employees/{id}/reactivate` | `employee:manage` | Undoes a deactivation |
| `PUT /api/employees/{id}/role` | `employee:manage` | Changes the role |
| `PUT /api/employees/{id}/domain` | `employee:manage` | Moves someone to the other domain |
| `GET /api/employees/{id}/pay` | `employee:pay_manage` | Pay history, newest first |
| `POST /api/employees/{id}/pay` | `employee:pay_manage` | Adds a pay change |
| `PUT /api/me/profile` | Signed in | Own first name, last name and phone |
| `GET /api/teams` | Signed in | Teams of the caller's visible domains, with member counts |
| `POST /api/teams` | `team:manage` | Creates a team |
| `PUT /api/teams/{id}` | `team:manage` | Renames a team |
| `DELETE /api/teams/{id}` | `team:manage` | Deletes an empty team |

Every change writes an `activity_events` row (`employee_updated`, `employee_deactivated`, `employee_reactivated`, `role_changed`, `employee_moved`, `pay_changed`, `team_created`, `team_renamed`, `team_deleted`). Pay events never carry amounts, because the activity feed is readable without financial access.

## The people list

`GET /api/employees?search=&teamId=&role=&domain=&status=&page=1&pageSize=25`

- `search` matches first name, last name, email and position; every word must match. Case is ignored.
- Deactivated people are left out unless `status=deactivated` is asked for. Invited people are included.
- Sorted by first name, then last name, then email (invited people without a name last). `pageSize` is at most 100.
- Returns `{ items, page, pageSize, totalCount }`. Each item has the person's role, domain, team, position, status, `isJoker`, joined date and last seen.

## Editing people

- Admins edit name, phone, position and team. Names are required once someone has signed up; an invited person's may stay empty. The team must belong to the person's domain.
- People edit their own name and phone (`PUT /api/me/profile`). Email, position, team and pay are managed by the company.
- **Nobody** can change an email address. There is no endpoint for it.

## Rules that protect accounts

- Changing a super admin's account in any way (profile, pay, status, role) also needs `employee:invite_super_admin`.
- Nobody deactivates, re-roles, moves or sets the pay of their own account.
- The **last active super admin** can never be deactivated or given another role, so the company always has one. Every active super admin's row is locked while this is checked, so two requests can't each remove "the other one".

## Deactivating and reactivating

- Deactivating sets the status to `deactivated`, revokes every refresh token and any pending invitation link, and drops the cached permissions. The person's access token stops working at once: permission checks find no permissions, and `/auth/me` answers `401`. Their history is kept.
- Reactivating makes them `active` again if they had signed up, or `invited` if they never did (send them a fresh link with `POST /api/invitations/{id}/resend`).

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
- A team can only be deleted while nobody is in it (including deactivated people) and no group chat belongs to it.
- `POST /api/teams` may leave out `domainCode` when the caller works in only one domain.

## Not built yet

- Profile photos (waiting for file storage).
- Overtime and labour cost on the profile. Hours worked are available from `GET /api/employees/{id}/work-summary` (see [design.md](design.md)); overtime and pay figures are on the company Salaries page (see [company.md](company.md)).
