# Company layer

Settings, working time, personal schedules and company overhead. These belong to neither domain. Every permission here is a company permission, held by super admins by default and grantable to others (see [authorization.md](authorization.md)). Salaries, the company overview and project pay are documented separately when they are built.

## Endpoints

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/company/settings` | Signed in | Name, time zone, week start, time format, default currency |
| `PUT /api/company/settings` | `company_settings:manage` | Saves them; the first save creates the settings row |
| `GET /api/company/working-time` | Signed in | The policy in force today and every dated policy, newest first |
| `POST /api/company/working-time` | `working_time:manage` | A new working week, break and overtime multiplier from a date |
| `GET /api/company/schedules?domain=&search=` | `schedule:manage` | Everyone's week (their own or the company's) and the hours it requires |
| `PUT /api/employees/{id}/schedule` | `schedule:manage` | A person's own week, or back to the company week, from a date |
| `GET /api/company/overhead` | `company:view` or `overhead:manage` | Every overhead line, this month's totals per currency and per group |
| `GET /api/company/overhead/monthly?months=13` | `company:view` or `overhead:manage` | Overhead per month, per currency (1–36 months, ending this month) |
| `POST /api/company/overhead` | `overhead:manage` | Adds a line |
| `PUT /api/company/overhead/{id}` | `overhead:manage` | Renames it or changes its group |
| `POST /api/company/overhead/{id}/amounts` | `overhead:manage` | A new monthly amount from a month |
| `POST /api/company/overhead/{id}/cancel`, `/resume` | `overhead:manage` | Stops it after this month, or undoes that |

## Settings

- Until a super admin first saves them, `isSetUp` is false and the other values are defaults.
- The time zone comes from configuration (`Company:TimeZone`, default `Asia/Beirut`) and is shown, not edited: every calendar date in the system depends on it.
- The settings are cached and dropped when saved.

## Working time

- A policy has working days (0 = Sunday … 6 = Saturday), start and end (minutes after midnight), a break and an overtime multiplier (1–5).
- **The break is paid.** It only lowers the hours required on a day longer than 6 hours: a 9–5 day with a 60-minute break requires 7 hours and pays 8.
- Policies are **dated**. The one in force on a day is the latest dated on or before it. A change starts today or later, never earlier, so past months never change (`400 working_time_in_past`). A change dated later than today can be replaced by setting the same date again. One already starting today can't (`422 working_time_already_changed`).
- Policies are cached and dropped whenever one changes.

## Personal schedules

- A schedule lists the person's working days, each one span within the day (at least 30 minutes). A weekday not listed is a day off. Setting `followsDefault` returns them to the company week.
- The same dating rules as the company week apply: from today or later, and a future change can be replaced.
- The break always comes from the company policy.
- People outside the caller's domains read as `404`. Deactivated accounts can't be changed. Changing a super admin's schedule also needs `employee:invite_super_admin`.

## Company overhead

`Business/Company/OverheadRules.cs`:

- A line has a name, a group (`software` or `operating`), a currency (USD or LBP) and a monthly amount.
- It is charged **in full** for every month from its start month to its end month.
- **Amount changes are dated** from the first of a month, this month or later, and never before a later change that already exists. Past months keep their cost. A change for a month that already has one replaces it.
- **Cancelling** ends the line at the end of this month. It can be resumed until that date passes; after that, add a new line. A line that hasn't started yet is removed instead (`204`).
- **Totals are per currency** and never converted.
- Every change writes an `overhead_*` activity event (company level, no domain), with the amount in the `amount` column.
