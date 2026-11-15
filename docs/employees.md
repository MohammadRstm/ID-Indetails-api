# People and departments

The app calls teams **departments**; the API, the tables and the codes keep the name `team`.

People management covers everyone with an account: super admins, admins and employees. Accounts are created by invitation (see [authentication.md](authentication.md)); this page covers what happens after that. The rules live in `Business/Employees/EmployeeRules.cs` and `Business/Pay/PayChangePolicy.cs`.

## Who can see whom

- Callers see people whose home domain is one of their **visible domains** (their own, plus the other once they hold one of its permissions; see [authorization.md](authorization.md)).
- **Super admins** belong to no domain. They are visible only to callers who see both businesses.
- **Website staff** belong to the website domain and are admins only ([website.md](website.md#the-website-domain)).
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
| `GET /api/employees/{id}/pay` | `employee:pay_manage` | Pay history, newest first, superseded entries included (`supersededAt`, `supersededById`) |
| `POST /api/employees/{id}/pay` | `employee:pay_manage` | A pay change from a day: it replaces every active entry from that day on |
| `POST /api/employees/{id}/pay/preview` | `employee:pay_manage` | What that same change would do, without making it |
| `PUT /api/me/profile` | Signed in | Own first name, last name and phone |
| `GET /api/teams` | Signed in | Teams of the caller's visible domains, with member counts |
| `POST /api/teams` | `team:manage` | Creates a team |
| `PUT /api/teams/{id}` | `team:manage` | Renames a team |
| `DELETE /api/teams/{id}` | `team:manage` | Deletes an empty team |

Every change writes an `activity_events` row (`employee_updated`, `employee_deactivated`, `employee_reactivated`, `employee_email_changed`, `role_changed`, `employee_moved`, `pay_changed`, `team_created`, `team_renamed`, `team_deleted`). Pay events never carry amounts, because the activity feed is readable without financial access.

## The people list

`GET /api/employees?id=&search=&teamId=&role=&domain=&status=&page=1&pageSize=25`

- `id` keeps just that person (still only if the caller may see them). It is where an alert about someone leads, for example the time-off alerts to the People page ([time-off.md](time-off.md)).

- `search` matches first name, last name, email and position; every word must match. Case is ignored.
- Deactivated people are left out unless `status=deactivated` is asked for. Invited people are included.
- Sorted by first name, then last name, then email (invited people without a name last). `pageSize` is at most 100.
- Returns `{ items, page, pageSize, totalCount }`. Each item has the person's role, domain, team, position, status, `isJoker`, joined date and last seen.

## Editing people

- Admins edit name, phone, position and team. Names are required once someone has signed up; an invited person's may stay empty. The team must belong to the person's domain.
- People edit their own name and phone (`PUT /api/me/profile`). Email, position, team and pay are managed by the company. Their own hours, overtime, schedule and requirements are in [me.md](me.md); employees don't see their own pay. Their profile's work, overtime and hours balance, as others see them, are in [design.md](design.md).
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
- A super admin's pay is company-level: it shows (and `GET /api/employees/{id}/pay` answers) only with `salary:view` or `employee:invite_super_admin`. They log no hours (any logged before that rule cost projects nothing); their salary is counted once, on the company overview.
- The **last active super admin** can never be deactivated or given another role, so the company always has one. Every active super admin's row is locked while this is checked, so two requests can't each remove "the other one".

## Deactivating and reactivating

Employment is kept as dated periods (`employment_periods`): accepting the invitation starts the first one, leaving ends it, and being taken back starts a new one. Pay, the hours ledger and project pay count only the days inside a period (`Business/Payroll/Employment.cs`, `Business/Employees/EmploymentRules.cs`).

- **Time off** ends with the employment: pending requests are cancelled and approved time off stops today, keeping the days already taken ([time-off.md](time-off.md)).
- **A salary advance** still to pay back is not touched: the final month takes what it can of what is left, up to the final pay, and anything still left stays open and shows as unsettled. Deactivating such a person sends `salary_advance_unsettled` to the other holders of `salary_advance:manage`, without an amount, so a super admin can mark the rest repaid in cash or write it off ([company.md](company.md#salary-advances)). Someone taken back before it is settled keeps it.
- **Deactivating** sets the status to `deactivated`, records when (`users.deactivated_at`), and ends the person's period on their **last working day**: today unless given, never in the future (`400 last_working_day_invalid`) or before the period started, and never so far back that a closed month would change (`422 month_closed`; on the 1st, last month is still open). That day is paid. It also revokes every refresh token and any pending invitation link, and drops the cached permissions. The person's access token stops working at once: permission checks find no permissions, and `/auth/me` answers `401`. Their history is kept, and `leftOn` shows the last working day.
- **Reactivating** clears `deactivated_at` and makes them `active` again if they had signed up, or `invited` if they never did (send them a fresh link with `POST /api/invitations/{id}/resend`). For someone who worked here:
  - back the day after their last working day (or the same day): their period simply goes on;
  - away since earlier **this month**: `payGap` is required (`422 pay_gap_required`). `true` keeps their period going and excuses the days away as paid, so the hours ledger doesn't count them short; `false` starts a new period today, and the days away aren't paid;
  - away since an earlier month: a new period starts today and the time away is never paid (`payGap` is refused, `422 pay_gap_not_asked`).

## Changing a role

`PUT /api/employees/{id}/role` with `{ "role": "admin" }`:

- Between admin and employee the domain stays. To change domain as well, move them separately.
- Making someone a super admin removes their domain and team.
- Design's employees and admins each need a department. `teamId` (optional) gives them one of the domain they end up in with the change, replacing their current one: it is needed when the new role needs a department and they have none, or when a super admin joins Design. Without one, the change is refused (`400 department_required` on `teamId`); a department of another domain is refused as for any move.
- A Design employee who becomes an admin or super admin has their time off ended: pending requests are cancelled, approved time off stops today, and the super admins are told ([time-off.md](time-off.md)).
- A super admin becoming an admin or employee needs `domainCode` (a domain the caller can see).
- Making or unmaking a super admin needs `employee:invite_super_admin`.
- `position` optionally gives them a new job title with it (an empty one clears it); without it, the title stays. Pay is changed separately, from a date (see Pay below).
- **Personal permission changes are cleared**, so the new role's defaults take over. This is the manual counterpart of the automatic role changes described in [authorization.md](authorization.md).
- When an employee becomes an admin or super admin, the response's `fixedPayFrom` is the first of this month: their pay is a fixed salary for the whole month and after (past months don't change, see [company.md](company.md)). The client then asks the caller to confirm their monthly salary, filled in with `currentPay`.

## Moving to the other domain

`PUT /api/employees/{id}/domain` with `{ "domainCode": "execution", "teamId": 7 }`:

- Only callers who see both domains can move people, and only between Design and Execution: the website's staff aren't moved.
- The team is replaced (it must belong to the new domain, or be empty).
- Personal permission changes are cleared, because they were made for the old domain.
- Super admins have no domain and can't be moved.
- A Design employee moved to Execution has their time off ended, as on a role change ([time-off.md](time-off.md)). Moving a Design admin into Design needs a `teamId` like an employee.

## Pay

Everyone is paid a monthly salary: there is no hourly pay (removed 15 Nov 2026; the `basis` column went with it, and the one hourly row in production, a test account, became a monthly row with its amount unchanged).

`POST /api/employees/{id}/pay` with `{ "amount": 1200, "currency": "USD", "effectiveFrom": "2026-06-01", "note": "Raise" }`:

- `effectiveFrom` defaults to today in the company time zone. It may be any earlier day, closed months included, but never a future one (`400 pay_date_in_future`).
- **A change dated D replaces every active entry from D onward** (15 Nov 2026). Those entries are kept, marked `superseded_at` and `superseded_by_id`, and the entries before D stay. A mistake is fixed by a change dated on the wrong entry's day; a change dated before someone joined simply sets their first salary, as days outside employment are never paid. Superseded entries are hidden from everything but the pay history (a global query filter), and a unique index on (`user_id`, `active_effective_from`) keeps one active entry per day.
- **A change of currency starts on the 1st of a month** (`400 pay_currency_change_not_first`), so each month is paid in one currency. The first salary may start any day.
- Everything worked out from pay follows the new history at once: Salaries, its PDF and CSV, the year, project labour and Project pay, the company overview, MyTime and advance repayments. Nothing is stored but an advance's settled amount and write-off.
- **Pay is per day** (see [company.md](company.md), "Salaries"): each working day someone is employed earns the salary in force that day × its paid minutes ÷ the month's, so a month with a change is split at it.
- Only a change that lands notifies the employee (`pay_changed`, with the day and no amount).
- **The profit floor:** a change that would take a running client project's profit to zero or below is held for the super admins, who alone force or dismiss it (`422 pay_change_held`, see [projects.md](projects.md), "Profit floor").

**The preview** (`POST /api/employees/{id}/pay/preview`, the same body) makes the change inside a transaction, measures it, and rolls it back, so it shows exactly what the change lands:
- `replaces`: the active entries from its day on that it would replace.
- `months`: each month from its day's month to this one, with `salaryBefore`/`salaryAfter` and `currencyBefore`/`currencyAfter` as Salaries works them out (null where no pay covers the month), and what salary advances take from it (`advanceBefore`, `advanceAfter`, `advanceCurrency`). Repayments of closed months are recalculated too; an advance's stored settled amount isn't.
- `projects`: for callers with `financial:view` in the project's domain, every project whose labour moves, with its `labourBefore`/`labourAfter` and `profitBefore`/`profitAfter` in USD, and `reachesZero` when a running client project held to the profit floor would drop to zero or below. Worst profit first.
- **A salary has no fixed hourly rate.** An hour of it in a month is the salary ÷ the paid hours (break included) the person's schedule gives that month (`PayRules.HourlyRate`): 3,000 a month on Monday–Friday, 9 to 5, is 18.75 an hour in February 2026 (160 paid hours) and 16.30 in July (184). The salary stays the same every month; this rate prices deductions and the normal hours of project labour.
- **Overtime has its own rate, stored with the pay** (`overtime_rate`, 28 Oct 2026). `overtimeRate` on a pay change or an invitation's pay is what an hour of a salary's overtime is paid at, before the company's multiplier. Left empty, it is the salary ÷ the overtime threshold's hours in force when the pay starts (184; 184 too when none is set), to the cent, for everyone whatever their schedule (`PayRules.SuggestedOvertimeRate`): 4,232 a month gives 23 an hour. It must be more than zero and no more than the salary (`400`). It follows each pay change; to change only the overtime rate, add a pay change with the same salary. Execution staff, admins and super admins earn no overtime, but their records get the suggested rate too. Pay records returned by the API carry `overtimeRate`.
- **Admins and super admins are paid a fixed salary** (`PayRules.HasFixedSalary`, by role): their pay never depends on hours. Inviting one needs their salary (`400 validation_failed` on `pay`).

## Teams (departments)

- Each team belongs to one domain, and names are unique within a domain (ignoring case).
- One department per person. A **Design employee or admin must have one** (since 9 Nov 2026): an employee's hours are counted in it, and its people and admins hear when someone in it is off ([time-off.md](time-off.md)). Inviting one, editing their profile, moving them into Design or changing their role into one without a `teamId` is `400 department_required`. Super admins and Execution staff need none. There is no backfill: a Design admin who has none is asked for one the next time they are edited.
- The prototype's teams are seeded (`Constants/TeamSeed.cs`):
  - Design: Engineering, Design, Data, Marketing, Operations, Platform.
  - Execution: Site Operations, Procurement, Quantity Surveying, Safety, Project Management.
- A team can only be deleted while nobody is in it, including deactivated people (`422 team_not_empty`), while it is no project's department (`422 team_on_projects`, see [projects.md](projects.md#departments)), and while its group chat has no messages (`422 team_has_chat`), so chat history is never lost. Rename it instead.
- `POST /api/teams` may leave out `domainCode` when the caller works in only one business.

## Not built yet

- Profile photos (waiting for file storage).
