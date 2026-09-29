# Company layer

Settings, working time, personal schedules and company overhead. These belong to neither domain. Every permission here is a company permission, held by super admins by default and grantable to others (see [authorization.md](authorization.md)). Everything in the prototype's company area is covered.

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
| `POST /api/company/overhead/{id}/currency` | `overhead:manage` | `{ currency, amount, fromMonth }`: charges it in another currency from a month (this month when empty). Returns the line charged from then |
| `POST /api/company/overhead/{id}/cancel`, `/resume` | `overhead:manage` | Stops it after this month, or undoes that |
| `GET /api/company/overview?from=&to=` | `company:view` | Both domains and the company for a period (this month to date by default) |
| `GET /api/company/overview/monthly?months=13` | `company:view` | Revenue, costs and overhead per month: the profit before and after company expenses (1–36 months) |
| `GET /api/company/salaries?month=YYYY-MM&domain=&search=&page=&pageSize=` | `salary:view` | Everyone's pay for a month, totals per currency and a summary |
| `GET /api/company/salaries/{userId}?month=YYYY-MM` | `salary:view` | How one person's pay adds up |
| `GET /api/company/salaries/export?month=YYYY-MM&domain=&search=` | `salary:view` | The filtered list as CSV, with a total per currency (10 a minute per user) |
| `GET /api/company/project-pay?unit=week\|month\|quarter&date=&projectId=` | `salary:view` | What each Design project paid its people in a period |
| `GET /api/company/project-pay/export?…` | `salary:view` | The same as CSV (10 a minute per user) |
| `POST /api/employees/{id}/hour-excuses` | `hours:excuse` | Excuses minutes, days or the whole month, paid or unpaid |
| `DELETE /api/hour-excuses/{id}` | `hours:excuse` | Removes an excuse (kept in history) |

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
- **Changing the currency** applies from the first of a month, this month or later (`400 overhead_same_currency` when it doesn't change, `422 overhead_already_cancelled` on a cancelled line). A line already charged before that month ends the month before, and a new line with the same name and group takes over in the new currency, so past months keep theirs. A line not charged before it just changes, with the new amount throughout.
- **Cancelling** ends the line at the end of this month. It can be resumed until that date passes; after that, add a new line. A line that hasn't started yet is removed instead (`204`).
- **Totals are per currency** and never converted.
- Every change writes an `overhead_*` activity event (company level, no domain), with the amount in the `amount` column.

## Salaries

Always calculated from the records, never stored (`Business/Payroll/`). The month defaults to this one and can't be in the future.

**Who is listed:** every person who has joined, has a pay record and was active at some point in the month, in the caller's domains: people still active, and people deactivated during or after the month (`users.deactivated_at`). Super admins appear only if they have pay and the caller sees both domains. People who joined after the month, or left before it, are left out. Someone deactivated before the leaving day was recorded (no `deactivated_at`) is left out, as before. The list, the detail, the CSV, the totals and the month-end short-hours alert all follow this.

**Leavers:** a person's final month ends on the day they left (the company-time-zone day of `deactivated_at`, included). Monthly pay is pro-rated to it the way a joining month is (`leftOn` on the row, and `workingDaysPaid` of `workingDays`), and their hours ledger ends there too, so the month counts as closed for them once they have left. Reactivating clears the date; the months in between then read as if they had stayed.

**Terms** are the pay in force at the end of the month (today while it is in progress). An hour of monthly pay is salary ÷ 173.33.

**Overtime** (`Overtime.cs`), day by day, using the person's schedule or the company week in force that day:
- all time on a day off;
- time before the day's start or after its end;
- time within hours beyond the day's required hours, taken from the latest work of the day.

**The hours ledger** (`HoursLedger.cs`) applies to Design employees only, from the day they joined, and up to today while the month is in progress:
- *Expected* is the required hours of every working day.
- *Excused* minutes and days (paid or not) reduce what is owed; a whole-month excuse clears it.
- Overtime first *makes up* any shortfall, taken from the month's overtime in date order; the rest is paid as overtime.
- A closed month still short is *flagged*; the month in progress is *behind*.

**Pay** (`SalaryCalculator.cs`):
- **Monthly:** the salary, pro-rated on the person's own working days if they joined or left mid-month, plus overtime at the multiplier in force that day.
  - In a closed, flagged month the shortfall is deducted at the hourly rate. Whole missed days still owed also lose their paid break, so each costs the full paid day (8h on a 9–5 day); a partly made-up remainder costs only its hours. Example: 21 missed days with one hour made up is 20 whole days (8h each) plus 6h.
  - Unpaid excuses are deducted too: unpaid minutes at the hourly rate, each unpaid day off as the full paid day, an unpaid whole month as the whole salary. The deduction never exceeds the salary.
- **Hourly:** hours within schedule and made-up hours at the rate in force each day, plus the paid break of each working day they worked, plus overtime at the rate × multiplier. Excuses don't change their pay (they are paid for what they log).
- Admins who log hours are paid overtime the same way, without an hours ledger. Execution staff don't log hours: they are paid their salary.
- Totals are per currency and never converted. Deactivated people aren't listed (there is no record of when they were deactivated yet).

**Excuses:** `kind` is `minutes` (with `minutes`), `days` (with days of that month) or `whole_month`; `isPaid` defaults to true; the note defaults by kind. Never for a future month (`400 excuse_month_in_future`). Removing one is a soft delete.

**Export:** UTF-8 CSV with a BOM. Values that start with `=`, `+`, `-` or `@` are prefixed with `'` so spreadsheets don't run them as formulas.

## Company overview

Always calculated, from the shared finance calculator (`Business/Finance/`), in USD.

- **Period:** `from` and `to` are company dates, both included. `to` defaults to today and is never later; `from` defaults to the first of `to`'s month.
- **Revenue recognised** (`Recognition.cs`): each client project's revenue recognised to date (revenue × progress, all of it once completed or archived) is spread over the months of its work: by employee labour cost when it has any, otherwise by its other costs (expenses, materials, ground labour). With no priced cost at all (work only by people with no pay, say), it follows the hours logged, and without those the month it was completed, so it always lands in some month (30 Sep 2026; the prototype dropped it). A period takes each month's share by the part of the month's elapsed days it covers.
- **Cost:** every project cost dated inside the period, converted at the rate of its day. Costs with no rate that day are listed per domain in `unconvertedCosts[]`, never counted as 0.
- **Invoices** are converted at the rate of their issue day. Those with no rate are left out of invoiced and outstanding and listed per currency in `unconvertedInvoices[]`, per domain and for the company.
- **Per domain:** revenue, cost, profit and margin; projects with work or costs in the period and active projects; active employees and who logged work; clients served and all clients; invoiced (issued in the period), collected (received in the period) and outstanding today.
- **Company:** both domains' profit less **company overhead**, charged by the calendar days of each month the period covers and converted at the rate on the first of the month. Overhead with no rate for its month is listed as unconverted instead of being added in.
- **Monthly:** each month's recognised revenue, labour, expenses, materials, ground labour and overhead, with the profit before and after company expenses. This feeds the chart on the company expenses page.

## Project pay

What each Design project paid its people in a week, month or quarter (`Business/Payroll/ProjectPay.cs`).

- **Period:** the week (following the company's week start), month or quarter containing `date` (today by default). Never a future period (`400 project_pay_period_in_future`).
- **Each work entry** is paid at the person's hourly rate in force that day (salary ÷ 173.33 for monthly pay): its hours within schedule, plus any overtime that made up a shortfall in the month, at the plain rate, and the rest of its overtime at the multiplier.
- **Left out:** paid breaks and salary deductions. They aren't worked on any project, so project pay differs from Salaries for hourly staff by exactly their paid breaks. Project cost is different: it counts everyone's paid break as labour ([projects.md](projects.md)).
- Overtime and made-up hours are worked out from the person's whole day and month, whichever projects they worked on. A leaver's hours ledger ends on the day they left, as in Salaries.
- Grouped by project, then person and currency; projects are ordered by USD pay. Someone with no pay record is listed with `priced: false` and paid 0.

