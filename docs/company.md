# Company layer

Settings, working time, personal schedules and company overhead. These belong to neither domain. Every permission here is a company permission, held by super admins by default and grantable to others (see [authorization.md](authorization.md)). Everything in the prototype's company area is covered.

## Endpoints

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/company/settings` | Signed in | Name, address and website (for the letterhead of invoices and BOQs), time zone, week start, time format, default currency |
| `PUT /api/company/settings` | `company_settings:manage` | Saves them; the first save creates the settings row. Address (up to 300 characters) and website (a web address such as `indetails.com`) are optional; leaving one out clears it |
| `PUT /api/company/logo`, `DELETE …` | `company_settings:manage` | `{ fileId }`: the company logo, from the caller's upload, whether or not the rest of the settings were saved ([files.md](files.md)) |
| `PUT /api/company/contact-email` | `company_settings:manage` or `website:manage` | `{ email }`: the company's own address, which the website shows and its contact form sends to ([website.md](website.md)). Empty clears it; it comes back as `contactEmail` on the settings |
| `PUT /api/company/proton-files` | `company_settings:manage` | `{ enabled }`: whether Proton may read project files, which sends their content to Anthropic, its model provider. It is off until someone turns it on, and comes back as `protonReadsFiles` on the settings ([proton.md](proton.md)) |
| `GET /api/company/branding`, `GET /api/company/logo` | Anyone | The name and whether there is a logo, and the logo's image, for the sign-in page; branding also carries the developer's WhatsApp number (`supportWhatsApp`, [me.md](me.md#contact-the-developer)) |
| `GET /api/company/working-time` | Signed in | The policy in force today and every dated policy, newest first |
| `POST /api/company/working-time` | `working_time:manage` | A new working week, break, overtime multiplier and overtime threshold from a date |
| `GET /api/company/schedules?domain=&search=` | `schedule:manage` | Everyone's week (their own or the company's) and the hours it requires |
| `GET /api/company/schedules/pdf?domain=&search=` | `schedule:manage` | The same list as a landscape PDF to print or keep (10 a minute per user); see [documents.md](documents.md) |
| `GET /api/company/schedules/export?domain=&search=` | `schedule:manage` | The same list as CSV, a row per person (10 a minute per user) |
| `PUT /api/employees/{id}/schedule` | `schedule:manage` | A person's own week, or back to the company week, from a date |
| `GET /api/company/overhead` | `company:view` or `overhead:manage` | Every overhead line with its category, this month's totals per currency and per category |
| `GET /api/company/overhead/monthly?months=13` | `company:view` or `overhead:manage` | Overhead per month, per currency (1–36 months, ending this month), with the month's one-off expenses (`oneOffTotals`) |
| `POST /api/company/overhead` | `overhead:manage` | Adds a line |
| `GET /api/company/overhead/one-offs?month=YYYY-MM` | `company:view` or `overhead:manage` | A month's one-off expenses (this month by default, never a future one), newest day first, with a total per currency and whether the month is still open |
| `POST /api/company/overhead/one-offs`, `PUT …/{id}`, `DELETE …/{id}` | `overhead:manage` | Adds, changes or removes a one-off expense (`201`, `200`, `204`); only while its month is the current one |
| `PUT /api/company/overhead/{id}` | `overhead:manage` | Renames it or changes its category |
| `GET /api/company/overhead/groups` | `company:view` or `overhead:manage` | The categories, first to last ("Other" last), with how many lines and one-offs use each |
| `POST /api/company/overhead/groups`, `PUT …/{id}`, `DELETE …/{id}` | `overhead_category:manage` | Adds (before "Other"), renames or removes (unused only) a category |
| `POST /api/company/overhead/{id}/amounts` | `overhead:manage` | A new monthly amount from a month |
| `POST /api/company/overhead/{id}/currency` | `overhead:manage` | `{ currency, amount, fromMonth }`: charges it in another currency from a month (this month when empty). Returns the line charged from then |
| `POST /api/company/overhead/{id}/cancel`, `/resume` | `overhead:manage` | Stops it after this month, or undoes that |
| `GET /api/account-labels?includeArchived=` | Signed in (`company_settings:manage` for `includeArchived=true`) | The account labels, first to last: `[{ id, name, position, archived, inUse }]`. `inUse`: payments or site expenses are filed under it |
| `POST /api/account-labels` | `company_settings:manage` | `{ name }`: a new label, last in the list |
| `PUT /api/account-labels/{id}` | `company_settings:manage` | `{ name }`: renames it |
| `POST /api/account-labels/{id}/archive`, `/restore` | `company_settings:manage` | Stops it being picked, or brings it back last. Repeating either changes nothing |
| `DELETE /api/account-labels/{id}` | `company_settings:manage` | Removes a label nothing is filed under (`204`); one that payments or site expenses carry is `422 account_label_in_use` and can only be archived |
| `PUT /api/account-labels/order` | `company_settings:manage` | `{ ids }`: the new order of the labels that can be picked, every one once (`422 account_label_order_stale` otherwise); answers every label, archived ones included |
| `GET /api/company/overview?from=&to=` | `company:view` | Both domains and the company for a period (this month to date by default) |
| `GET /api/company/overview/monthly?months=13` or `?year=2026` | `company:view` | Revenue, costs, overhead, one-off expenses (`oneOffExpenses`), fixed salaries (`fixedSalaries`), paid leave (`paidLeave`), the overtime premium (`overtimePremium`) and salary advances written off (`advanceWriteOffs`) per month: the profit before company expenses, after overhead and one-offs, and the net profit after all of them, as the overview's (whole months; the last 1–36 months, or a year's months from January, up to this month in the current year; a future year, or one before 2000, is `400`) |
| `GET /api/company/salaries?month=YYYY-MM&domain=&role=&search=&page=&pageSize=` | `salary:view` | Everyone's pay for a month, totals per currency and a summary; `role` (super_admin, admin, employee) narrows it to one role |
| `GET /api/company/salaries/{userId}?month=YYYY-MM` | `salary:view` | How one person's pay adds up, and where their hours went (`projects`) |
| `GET /api/company/salaries/export?month=YYYY-MM&domain=&role=&search=` | `salary:view` | The filtered list as CSV, with a total per currency (10 a minute per user) |
| `GET /api/company/salaries/pdf?month=YYYY-MM&domain=&role=&search=` | `salary:view` | The same filtered list as a landscape PDF to print or keep, with a total per currency and the people with no pay set (10 a minute per user); see [documents.md](documents.md) |
| `GET /api/company/salary-advances?view=pending\|open\|closed&page=&pageSize=` | `salary_advance:manage` or `salary:view` | Salary advances in the caller's domains: `pending` requests oldest first, `open` ones still to pay back (leavers with something unsettled included) and `closed` ones (paid back, settled, declined, withdrawn, cancelled), newest first. `pending` by default; page size 1–100 |
| `GET /api/company/salary-advances/{id}` | `salary_advance:manage` or `salary:view` | One advance, with its months, what is repaid and left, and what the caller may do with it |
| `POST /api/company/salary-advances` | `salary_advance:manage` | `{ userId, amount, givenOn?, months: [{ month, amount }], note? }`: records an advance a super admin handed over without a request. Returns `201` |
| `POST /api/company/salary-advances/{id}/approve` | `salary_advance:manage` | `{ amount?, months: [{ month, amount }], note? }`: approves a pending request, for what was asked or less, and sets the months it comes off |
| `POST /api/company/salary-advances/{id}/decline` | `salary_advance:manage` | `{ note? }`: declines a pending request |
| `PUT /api/company/salary-advances/{id}/months` | `salary_advance:manage` | `{ months }`: changes the months still open (this one on) |
| `POST /api/company/salary-advances/{id}/cancel` | `salary_advance:manage` | `{ note? }`: cancels an advance given by mistake, in the month it was given |
| `POST /api/company/salary-advances/{id}/settle` | `salary_advance:manage` | `{ kind: "cash" \| "written_off", note? }`: closes what is left |
| `GET /api/company/project-pay?unit=week\|month\|quarter&date=&projectId=` | `salary:view` | What each Design project paid its people in a period |
| `GET /api/company/project-pay/export?…` | `salary:view` | The same as CSV (10 a minute per user) |
| `POST /api/employees/{id}/hour-excuses` | `hours:excuse` | Excuses minutes, days or the whole month, paid or unpaid |
| `DELETE /api/hour-excuses/{id}` | `hours:excuse` | Removes an excuse (kept in history) |

## Settings

- Until a super admin first saves them, `isSetUp` is false and the other values are defaults. A logo can be set before that; it creates the settings with no name, so they still read as not set up.
- The time zone comes from configuration (`Company:TimeZone`, default `Asia/Beirut`) and is shown, not edited: every calendar date in the system depends on it.
- The settings are cached and dropped when saved.

## Account labels

One company-wide list of labels (`account_labels`: Concrete, Finishing and Permits are seeded) that payments and expenses are filed under. Super admins edit it in Settings; everyone signed in can read it. `Business/Company/AccountLabelRules.cs`:

- A name is trimmed, required and at most 40 characters. Names are unique whatever their case, archived labels included (`409 account_label_taken`).
- Archiving stops a label being picked but never removes it: what already carries it keeps it. Restoring puts it last.
- Only a label nothing is filed under (no payment, no site expense) can be deleted. One in use can only be archived, so no payment or expense ever loses its label; the database refuses it too (the foreign keys restrict). A deleted label's name is free again.
- Every change is an `account_label_changed` event (company level, `change` and `name` in the payload), so only `company:view` sees it in the feed.

## Working time

- A policy has working days (0 = Sunday … 6 = Saturday), start and end (minutes after midnight), a break, an overtime multiplier (1–5) and an overtime threshold (`overtimeThresholdHours`, 0–744; 0 is none): a salary's overtime is paid only past that many hours a month (below). Left out of a change, the threshold carries on from the working time in force that day. From 1 November 2026 it is 184 (the `OvertimeThreshold` migration added that working time).
- **The break is paid.** It only lowers the hours required on a day longer than 6 hours: a 9–5 day with a 60-minute break requires 7 hours and pays 8.
- **From 1 December 2026 the break is 0**, so the whole day is logged and required ([decision 25](business-rules-review.md#decided)). A 9–5 day then requires and pays 8 hours: logging all of it is no overtime, and no paid break is added to hourly pay, missed-day deductions or project cost. The rules didn't change for this: a day with no break already works this way, and November 2026 and earlier keep their paid break.
- Policies are **dated**. The one in force on a day is the latest dated on or before it. A change starts today or later, never earlier, so past months never change (`400 working_time_in_past`). A change dated later than today can be replaced by setting the same date again. One already starting today can't (`422 working_time_already_changed`).
- Policies are cached and dropped whenever one changes.

## Personal schedules

- A schedule lists the person's working days, each one span within the day (at least 30 minutes). A weekday not listed is a day off. Setting `followsDefault` returns them to the company week.
- The same dating rules as the company week apply: from today or later, and a future change can be replaced.
- The break always comes from the company policy.
- People outside the caller's domains read as `404`. Deactivated accounts can't be changed. Changing a super admin's schedule also needs `employee:invite_super_admin`.

## Company overhead

`Business/Company/OverheadRules.cs`:

- A line has a name, a **category** (`groupId`), a currency (USD or LBP) and a monthly amount.
- **Categories** (`overhead_groups`) are listed first to last, "Other" always last. Thirteen are seeded (Software & services, Operating costs, Rent & utilities, Internet & phone, Marketing & advertising, Professional fees, Insurance, Office supplies, Equipment & maintenance, Vehicles & transport, Bank fees, Taxes & licences, Training & memberships) and then Other. Lines made before categories existed are in Software & services or Operating costs, as their old group was.
  - Whoever holds `overhead_category:manage` adds, renames or removes one (`403` for anyone else, `overhead:manage` included). A new one goes before Other. Names are unique, whatever their case (`409 overhead_group_exists`), and at most 60 characters.
  - One that any line or one-off expense uses, past ones included, can't be removed (`422 overhead_group_in_use`): move them first. A line put in a category that no longer exists is `400 overhead_group_not_found`.
- It is charged **in full** for every month from its start month to its end month.
- **Amount changes are dated** from the first of a month, this month or later, and never before a later change that already exists. Past months keep their cost. A change for a month that already has one replaces it.
- **Changing the currency** applies from the first of a month, this month or later (`400 overhead_same_currency` when it doesn't change, `422 overhead_already_cancelled` on a cancelled line). A line already charged before that month ends the month before, and a new line with the same name and category takes over in the new currency, so past months keep theirs. A line not charged before it just changes, with the new amount throughout.
- **Cancelling** ends the line at the end of this month. It can be resumed until that date passes; after that, add a new line. A line that hasn't started yet is removed instead (`204`).
- **Totals are per currency** and never converted.
- Every change writes an `overhead_*` activity event (company level, no domain), with the amount in the `amount` column.

### One-off expenses

A company cost paid once (a refrigerator, a repair), beside the recurring lines above. `Business/Company/OneOffExpenseRules.cs`:

- It has a name (up to 150 characters), a **category** (the same `overhead_groups`), a currency (USD or LBP), an amount above zero and the **day it was spent** (`spentOn`). It belongs to the company, never a project or a domain.
- It counts **in full on its day** (like a project cost), not spread over the month. Totals are per currency in the list and never converted; the overview converts each one at the rate of its own day.
- **The day** is from the 1st of the current company month up to today (`400 one_off_outside_this_month`, field `spentOn`).
- **Closed months are locked.** It can be changed or removed only while its day is in the current company month (`422 one_off_locked`), and a change must still pass the rule above. So a past month's profit never moves.
- A category that doesn't exist is `400 overhead_group_not_found`; a category any one-off uses can't be removed (`422 overhead_group_in_use`), and the categories list counts one-offs among its lines. An unknown id is `404`.
- The list answers `month` (the first of it), `isOpen` (it is the current month), the items newest day first (`spentOn`, then id), and a total per currency. Each item says `canEdit`: the caller holds `overhead:manage` and its month is open. A future month is `400`.
- Every change writes an `overhead_one_off_added`, `overhead_one_off_changed` or `overhead_one_off_removed` activity event (company level, no domain, subject type `one_off_expense`), with the amount in the `amount` column (and, for a change, the amount before it in `prev_amount`); the name, currency, day and category are in the payload. They show in the feed's Company group, like the `overhead_*` events.
- Proton lists them, and adds, changes and removes them ([proton.md](proton.md)).

## Salaries

Always calculated from the records, never stored (`Business/Payroll/`). The month defaults to this one and can't be in the future.

**Who is listed:** every person with a pay record who was employed on some day of the month (a period of employment overlapping it, see [employees.md](employees.md#deactivating-and-reactivating)), in the caller's domains. Super admins appear only if they have pay and the caller sees both domains. People who joined after the month, left before it, or were away all of it are left out. The list, the detail, the CSV, the totals and the month-end short-hours alert all follow this. People of the filtered list who had joined by then but have no pay set are named in `withoutPay`, so a missing salary shows.

**The year** (`GET /api/company/salaries/year?year=&domain=&role=&search=`, `salary:view`): what each person is owed in every month of the year up to this month (each month as the list works it out: basis, the salary or rate in force, and the total), and a total per month and currency. This year by default; never a future year (`400 salary_month_in_future`).

**Leavers:** a person's final month ends on their last working day (included). Monthly pay is pro-rated to it the way a joining month is (`leftOn` on the row, and `workingDaysPaid` of `workingDays`), and their hours ledger ends there too, so the month counts as closed for them once they have left. Someone taken back later isn't paid, or counted short, for the days they were away between two periods: a month partly away is pro-rated, a month wholly away isn't listed.

**Salary advances** ([below](#salary-advances)) change no pay: a row's `total`, the hourly rate and the overtime are the same with or without one. They only come off what is handed over:
- A row has `advanceRepayment` (what comes off this month for the person's advances; null when nothing does) and `toHandOver`: the month's pay less the advance, never below 0. The pay is `fullMonthEstimate ?? total` while the month runs, otherwise `total`, so `toHandOver` = max(0, base − advance).
- `totals[]` carry `advanceRepayment` (0 when nothing comes off) and `toHandOver`, per currency, as the rows add up.
- The response's `advances` is a summary for the caller's domains: `pending` (requests waiting for a decision), `left` (what is still to pay back, per currency) and `unsettled` (people who left with something still to settle).
- The detail's `advances` lists the person's advances that are waiting, open or come off that month, as `GET /api/company/salary-advances/{id}` shows them.
- The CSV and the PDF add an "Advance" and a "To hand over" column. The PDF adds them only when someone listed has an advance, narrowing the other columns to fit ([documents.md](documents.md)).

**Each row** also says the month's pay as a monthly amount (`monthlyPay`: the salary, or an hourly rate × the month's paid hours on their schedule) and, while a salaried month runs, what is earned so far (`earnedSoFar`) beside what the month comes to as things stand (`fullMonthEstimate`); once a month has closed, `earnedSoFar` is the total. The totals carry `earnedSoFar` too (`Business/Payroll/MyTime.cs`, `EstimateOf`):
- `earnedSoFar` is the salary accrued so far plus overtime pay. It accrues with the hours required so far (required so far ÷ the whole month's) where the hours ledger applies, otherwise with the working days passed (none on the 1st).
- `fullMonthEstimate` is the salary plus overtime so far, less what would be deducted if the month closed as short as it is now: the shortfall plus the paid break of each whole missed day, at the hourly rate, exactly as a closed month deducts it (`SalaryCalculator.ShortfallMinutes`).

**Terms** are the pay in force at the end of the month (today while it is in progress). An hour of monthly pay (`hourlyRate`) is the salary ÷ the paid hours the person's schedule gives that month, so the salary stays fixed whatever the month's length; overtime is paid at the pay's overtime rate (`overtimeRate`): the salary ÷ the overtime threshold's hours (184) unless an admin set another with the pay. See [employees.md](employees.md#pay).

**Overtime** (`Overtime.cs`), day by day, using the person's schedule or the company week in force that day:
- all time on a day off;
- time before the day's start or after its end;
- time within hours beyond the day's required hours, taken from the latest work of the day.

**The hours ledger** (`HoursLedger.cs`) applies to Design employees only, from the day they joined, and up to today while the month is in progress:
- *Expected* is the required hours of every working day.
- *Excused* minutes and days (paid or not) reduce what is owed; a whole-month excuse clears it. Days of granted time off count as excused days ([time-off.md](time-off.md)).
- Overtime first *makes up* any shortfall, taken from the month's overtime in date order; the rest is paid as overtime.
- A closed month still short is *flagged*; the month in progress is *behind*.

**The overtime threshold** (decision 26, from 1 November 2026): a salary's overtime is paid only for the hours counted past the month's threshold.
- *Threshold:* the working time's `overtimeThresholdHours` in force on the month's first day (184), or the month's expected hours on the person's calendar if those are more, so overtime is only paid once the month's own hours are done. It is fixed: joining or leaving mid-month doesn't lower it, so a joiner rarely reaches it.
- *Counted:* every minute logged, normal or overtime, plus paid excuses (minutes, days at their required hours, a whole month at its expected hours) and paid time off (its working days, as paid day excuses). Unpaid excuses and unpaid time off don't count.
- *Paid:* what is counted past the threshold, taken from the month's latest overtime first, and never more than the overtime left after making up a short month. It is paid at the pay's overtime rate × the multiplier of its day. The month in progress pays it as soon as the running total passes the threshold.
- Overtime still *makes up* a short month first, so deductions are unchanged; anyone past the threshold isn't short.
- *Not for* hourly pay (overtime as before, at the rate × multiplier) or fixed salaries (no overtime). A threshold of 0 turns it off: all overtime is paid as before, as in October 2026 and earlier.
- *Who sees it:* Salaries only. Rows carry `overtimeMinutes` (overtime logged, after making up), `overtimePaidMinutes` (paid past the threshold), `overtimeThresholdMinutes` and `countedMinutes` (null where no threshold applies); the detail's `overtimeWork[].paidMinutes` says which entries were paid; the CSV has both "Overtime" and "Overtime paid". Employees see their overtime exactly as before (My work, the Overtime panel, Hours balance), with no pay.
- *Projects:* the premium is paid for the month as a whole, a company cost, so a salary paid past a threshold puts no overtime premium on projects: project cost, Project pay and the hours by project on Salaries price all its hours at the hourly rate (`EntrySplit.NoPremium`). The company overview takes the premium off its net profit (`overtimePremium`, below).

**Pay** (`SalaryCalculator.cs`):
- **Monthly:** the salary, pro-rated on the paid hours of the person's own working days if they joined or left mid-month, plus overtime at the overtime rate × the multiplier in force that day (only past the overtime threshold from 1 November 2026, above).
  - In a closed, flagged month the shortfall is deducted at the month's hourly rate, so a whole missed day costs exactly its share of the salary. Whole missed days still owed also lose their paid break, so each costs the full paid day (8h on a 9–5 day); a partly made-up remainder costs only its hours. Example: 21 missed days with one hour made up is 20 whole days (8h each) plus 6h.
  - Unpaid excuses are deducted too: unpaid minutes at the hourly rate, each unpaid day off as the full paid day, an unpaid whole month as the whole salary. The deduction never exceeds the salary.
  - Time off is read as day excuses: a paid day off changes nothing, and each working day of unpaid time off is deducted as the full paid day.
- **Hourly:** hours within schedule and made-up hours at the rate in force each day, plus the paid break of each working day they worked, plus overtime at the rate × multiplier. Excuses don't change their pay (they are paid for what they log), but each working day of **paid time off** pays their normal paid day, the day's whole span at the rate in force that day (`timeOffPay`, part of the base pay); unpaid time off pays nothing. It is a company cost and never lands on a project ([time-off.md](time-off.md)).
- Execution staff don't log hours: they are paid their salary.
- **Admins and super admins** are paid a fixed monthly salary, required when they are invited (an hourly pay is refused, `400 fixed_salary_paid_monthly`, on a pay change or an invitation). They log no hours, so their pay never depends on hours: any logged before that rule earn no overtime or paid breaks (and count as normal hours on Project pay). It is pro-rated on their working days in a month they join or leave, and unpaid excuses are deducted, as for anyone on a salary. Their rows say hours aren't tracked (`tracksHours` is true for Design employees only). Their salaries are a company cost: they come off the company's net profit (below), and any hours they logged in those months cost their projects nothing, so their pay is never counted twice.
- **Role changes go by month.** A month in which someone was an admin or super admin on any day is paid as a fixed salary, whatever their role is now: no hours ledger, no overtime, and their hours that month cost projects nothing (`RoleHistory.FixedSalaryMonth`). So the month of a promotion is fixed from its first day, a demoted admin keeps the fixed salary until the month ends, and employee rules start on the 1st of the next month. The months before a promotion keep their own role: promoting or demoting someone never changes their past months on Salaries, Project pay, project cost or the company overview. The role on each day comes from the recorded changes (`role_changed`, `role_auto_promoted`, `role_auto_demoted`, in the company time zone). A month's salary is the pay in force at its end, so a pay change made on the day of a promotion sets that month's fixed salary.
- Totals are per currency and never converted.

**Excuses:** `kind` is `minutes` (with `minutes`), `days` (with days of that month) or `whole_month`; `isPaid` defaults to true; the note defaults by kind. Never for a future month (`400 excuse_month_in_future`). Removing one is a soft delete. A day inside granted time off can't be excused, nor a month holding any excused whole (`422 excuse_on_time_off`).

**Time off** ([time-off.md](time-off.md)) is decided on its own page, not here. Its working days reach the calculator as day excuses (`SalaryCalculator.Excuse.TimeOff`), so the ledger, the missed-hours checks, the log reminders, the overtime threshold and the unpaid deduction apply unchanged. Unlike an excuse, it may be for a future month. The salary detail carries a read-only `timeOff` list (`requestId`, `firstDay`, `lastDay`, `isPaid` and `days`, its working days in the month) beside `excuses`, and `timeOffPay`.

**Where the hours went** (the detail's `projects`): the month's work grouped by project, most time first: entries, hours within schedule, overtime that made up a shortfall, the rest of the overtime, and what those hours were paid, as Project pay prices them (below). Paid breaks and deductions belong to no project, so the rows don't add up to the salary. A person paid in two currencies in the month has a row per currency. Empty for people who don't log hours.

**Export:** UTF-8 CSV with a BOM. Values that start with `=`, `+`, `-` or `@` are prefixed with `'` so spreadsheets don't run them as formulas.

## Salary advances

Part of someone's pay handed over early. It comes off what they are handed in the months chosen for it. The rules are `Business/Payroll/AdvanceRules.cs` (pure) and `SalaryAdvanceService`; an advance is a `salary_advances` row with a `salary_advance_repayments` row for each month it plans to take from ([database.md](database.md)).

**What never changes.** An advance changes when cash leaves, not what anything costs: what a person earns in a month, the hourly rate, overtime, deductions, project labour cost, Project pay, Financials, the profit floor and every figure of the company overview stay as they were. The only cost is an advance **written off** (below), a company expense on its day. Salaries shows what comes off each month and what is left to hand over ([above](#salaries)).

**Who asks.** Anyone with pay set today (employees, site staff, admins, super admins), from `/api/me/salary-advances` ([me.md](me.md#my-salary-advances)): an amount and a reason, in the currency of their pay, with no account label. The form never shows or checks their pay, so the cap is checked when a request is approved. A person has one pending request at a time (`422 salary_advance_pending`) and none while an earlier advance still has something to pay back (`422 salary_advance_open`); with no pay set it is `422 salary_advance_no_pay`. They can withdraw a pending request.

**Who gives and decides.** Holders of `salary_advance:manage`, a company permission super admins hold by default ([authorization.md](authorization.md)). Reading the list and one advance is also open to `salary:view`.
- A holder approves a request (for what was asked or less, never more: `400 salary_advance_more_than_asked`, and with the months it comes off), declines it, or **records** an advance directly, for cash already handed over: the person, the amount, the day given, the months and a note.
- Nobody gives or decides their own (`403 employee_change_not_allowed`), and a super admin's needs someone who also holds `employee:invite_super_admin` (`403 forbidden`).
- Holders see and decide only people of the domains they can see; anyone else's advance reads as `404`.
- The person and then the advance are locked (`SELECT … FOR UPDATE`), always in that order, so two decisions at once run one after the other and the first wins (`422 salary_advance_decided` for the second); a request, an approval and a recording can't overlap.
- A recorded advance for someone with a request waiting is `422 salary_advance_pending`: decide that request instead.

**The cap.** An advance is at most the person's monthly pay as Salaries shows it (`monthlyPay`: the salary, or an hourly rate × the month's paid hours), and so is each month's repayment (`422 salary_advance_over_pay`). With no pay set today nothing can be approved or recorded (`422 salary_advance_no_pay`).

**One at a time.** An advance is open while it is given, not settled and has something left once the months before this one are counted. Asking, approving or recording for someone with an open one is `422 salary_advance_open`; a new one waits until the last has been paid back, settled or cancelled.

**The months.** The advance comes off in one to twelve months:
- each is the first day of a month, from the month it was given in (this month for an approval) to within twelve months of it, chosen once, above zero to the cent, and none above the monthly pay;
- together they add up to the amount (`400 salary_advance_months_total`); any other fault is `400 salary_advance_months_invalid`;
- a recorded advance's `givenOn` is from the first of this month up to today, today when left out (`400 salary_advance_given_on`), and its months start at its month.

**How each month takes its share** (`AdvanceRules.Repayments`, worked out from what Salaries says each month pays, never stored):
- A month takes what is planned for it plus what earlier months couldn't take, but never more than that month's pay (the full-month estimate while it runs, `total` once closed, rounded down to the cent). Whatever didn't fit moves on to the next month by itself, past the last planned month if need be, until it is paid.
- In a month the person's employment ended in, everything left is due, up to the final pay.
- The month an advance is settled in still takes its share (a leaver's final pay first), never more than the settlement left to it; nothing comes off after it.
- Several advances of one person are taken oldest first from each month's pay.
- A month paid in another currency, or one the person wasn't employed in, gives nothing: the share moves on. A month something is due in with no pay worked out is reported as missing until the pay is given.
- The month in progress shows its share as things stand; it is final once the month closes.

**Changing and closing.** Only a given advance that isn't settled can be changed (`422 salary_advance_closed` otherwise).
- **Months** (`PUT …/months`): the open months (this one on) are replaced, and must add up to what the closed months didn't plan (`422 salary_advance_nothing_left` when every planned month has closed: what they couldn't take moves on by itself, or can be settled). Closed months never change.
- **Cancel:** an advance given by mistake, only in the month it was given (`422 salary_advance_cancel_closed`). Nothing is repaid and the person can ask again.
- **Settle** (`cash` or `written_off`): closes what is left after this month's share, as things stand: the amount less what every month up to this one took (`422 salary_advance_nothing_left` with nothing left). This month still takes its share, so a leaver's final pay comes first, and nothing comes off pay after it; the person can ask again. Paid in cash changes no company figure. **Written off** is a company expense on the day it was settled, in the advance's currency, converted at that day's rate ([Company overview](#company-overview)); it is never put on a project.

**Leaving.** In the last month everything left comes due, up to the final pay. Whatever is still left stays open, and deactivating someone with an advance still to pay back sends `salary_advance_unsettled` to the other holders of the permission ([notifications.md](notifications.md)). The advance shows `unsettled` until a super admin marks it repaid in cash or writes it off. Someone taken back before it is settled keeps it, and a super admin can set months to take it from their new pay ([employees.md](employees.md#deactivating-and-reactivating)).

**What is seen.** Super admins see every field of an advance: who asked or recorded it, who decided it, who settled it, what each closed month took (`taken`), what is repaid and left, `behind` (a closed month couldn't take all it was due), `unsettled` and what the caller may do (`canDecide`, `canChange`, `canCancel`). The person sees their own list only ([me.md](me.md#my-salary-advances)). Nothing in the activity feed or a notification carries an amount.

**Activity and notifications.** Each step writes a company-level activity event with no amount, like `pay_changed`: `salary_advance_requested`, `_withdrawn`, `_approved`, `_declined`, `_recorded`, `_months_changed`, `_cancelled` and `_settled` (subject type `user`, the person; the advance and its status in the payload). Only people with `salary:view` or `salary_advance:manage` see them in the feed. The six notifications are in [notifications.md](notifications.md).

**Proton** never reads or proposes advances: pay amounts never reach it ([proton.md](proton.md)).

**Errors:**

| Status and code | When |
|---|---|
| `422 salary_advance_no_pay` | The person has no pay set today |
| `422 salary_advance_pending` | A request is already waiting |
| `422 salary_advance_open` | The last advance isn't paid back yet |
| `422 salary_advance_decided` | The request was already decided or withdrawn |
| `422 salary_advance_over_pay` | The advance, or a month's repayment, is above the monthly pay |
| `400 salary_advance_more_than_asked` | An approval above what was asked (field `amount`) |
| `400 salary_advance_months_invalid` | No months, more than twelve, before the first allowed month, too far ahead, repeated, or not above zero (field `months`) |
| `400 salary_advance_months_total` | The months don't add up to what is to be paid back (field `months`) |
| `400 salary_advance_given_on` | A recorded day outside the first of this month to today (field `givenOn`) |
| `422 salary_advance_closed` | The advance is settled, cancelled or wasn't given |
| `422 salary_advance_nothing_left` | Every planned month has closed, or nothing is left to settle |
| `422 salary_advance_cancel_closed` | The month it was given in has closed |
| `403 employee_change_not_allowed` | Giving or deciding one's own advance |
| `403 forbidden` | A super admin's advance without `employee:invite_super_admin`, or an endpoint without `salary_advance:manage` |
| `404 not_found` | Unknown, or another person's advance (their own list), or one outside the caller's domains |

## Company overview

Always calculated, from the shared finance calculator (`Business/Finance/`), in USD. Each domain's money comes from the same batch load as its Financials page (`MoneyRollup`): revenue, cost, profit and margin from the same period calculation (`PeriodMoney.Of`, see [finance.md](finance.md#rules)), outstanding from the billing rule (`ProjectBilling`). So the overview and a domain's Financials summary agree to the cent for the same days.

- **Period:** `from` and `to` are company dates, both included. `to` defaults to today and is never later; `from` defaults to the first of `to`'s month.
- **Revenue** is the money received in the period, each payment on the day it arrived, so profit moves as soon as a payment is recorded or a cost is added. Expected revenue (what projects agreed) isn't shown here; the domains' summaries have it ([finance.md](finance.md)).
- **Cost:** every project cost dated inside the period, converted at the rate of its day. Costs with no rate that day are listed per domain in `unconvertedCosts[]`, never counted as 0.
- **Invoices** are issued, live ones (drafts aren't counted here), converted at the rate of their issue day. Those with no rate are left out of invoiced and outstanding and listed per currency in `unconvertedInvoices[]`, per domain and for the company.
- **Per domain:** revenue, cost, profit (revenue less the cost total rounded once) and margin; projects with work or costs in the period and active projects; active employees and who logged work; clients served and all clients; invoiced (issued in the period), collected (the period's payments net of TVA, the same as revenue) and outstanding today.
- **Company:** both domains' profit (`grossProfit`, the domains' profits added up) less its **expenses** (`expenses`): the **company overhead**, charged by the calendar days of each month the period covers and converted at the rate on the first of the month, the **one-off expenses** (below), the **fixed salaries**, **paid leave**, the **overtime premium** and **salary advances written off**. `netProfit` is `grossProfit` less `expenses`, so the client never adds them up. Each counts the pay of its months, as Salaries works it out, for the share of the month's calendar days the period covers, converted at the rate on the first of the month. Overhead or salaries with no rate for their month, and one-offs with none on their day, are listed as unconverted instead of being added in.
  - **One-off expenses** (`oneOffExpenses`, `unconvertedOneOffExpenses`): the one-offs spent from `from` to `to`, each in full on its day and converted at the rate in force on that day, the rate lookup the project costs use. A closed month's figure never changes, because a one-off can't be changed once its month has closed.
  - **Fixed salaries** (`fixedSalaries`): the pay of everyone on a pure monthly salary who logs no hours, so no project carries it: admins and super admins (any month they held that role on a day, see above) and Execution's site staff (employees outside Design). Someone promoted on Monday 16 February 2026 has all of February as a fixed salary: the whole February salary is a company cost, and none of their February hours cost projects anything. Their months before February stay employee months. `adminSalaries` (and `unconvertedAdminSalaries`) carry the same figure under its old name for the phone app, until it moves.
  - **Paid leave** (`paidLeave`): what Design staff were paid for paid time off (`SalaryCalculator.PaidLeavePay`). On a salary it is the paid excused minutes and each paid day off (approved time off included) at its full paid day, at the month's hourly rate, as unpaid ones are deducted; a whole month off with pay is the month's base pay. On hourly pay it is their paid time off (`timeOffPay`); paid excuses pay hourly staff nothing. Nothing is logged for it, so no project carries it.
  - **Overtime premium** (`overtimePremium`): what Design salaries paid for overtime past the month's threshold (above) beyond the hourly rate: the overtime pay less those minutes at the hourly rate (`SalaryCalculator.OvertimePremium`). Projects carry every logged hour of such a month at the hourly rate, so the rest reaches no project. Months with no threshold (before November 2026), hourly pay and fixed salaries have none: their overtime is on projects, or there is none.
  - **Advance write-offs** (`advanceWriteOffs`, `unconvertedAdvanceWriteOffs`): the salary advances written off (settled as `written_off`) from `from` to `to`, each in full on the day it was settled and converted at the rate in force that day, as one-off expenses are. It is part of `expenses` and comes off `netProfit`. Nothing else about an advance reaches the overview: a given advance, its repayments and a cash settlement change no figure ([Salary advances](#salary-advances)).
  - Design employees' other labour is already in the domains' costs, through their hours.
  - Payroll takes each person's current domain for past months too (no dated record of domain moves), as Salaries does.
- **Monthly:** each month's revenue (money received), labour, expenses, site expenses (`materials` in the JSON), ground labour, overhead and one-off expenses (`oneOffExpenses`), with the profit before company expenses, after overhead and one-offs (`profitAfterOverhead`), and the net profit after all of them, which takes off the month's advance write-offs (`advanceWriteOffs`, each at the rate of the day it was settled) as well. This feeds the chart on the company expenses page, a year at a time.
  - Each domain's month is worked out as its Financials monthly chart works it out, and `profitBeforeOverhead` adds up the domains' profits (each one's received less its cost total rounded once). So a whole month's `profitBeforeOverhead` equals the overview's `grossProfit` for that month to the cent.
  - Each cost column is the domains' costs of that source added up, then rounded. As on the domain charts, the rounded columns can add up to a few cents away from revenue − profit.

## Project pay

What each Design project paid its people in a week, month or quarter (`Business/Payroll/ProjectPay.cs`).

- **Period:** the week (following the company's week start), month or quarter containing `date` (today by default). Never a future period (`400 project_pay_period_in_future`).
- **Each work entry** is paid as Salaries pays it: its hours within schedule, plus any overtime that made up a shortfall in the month, at the person's hourly rate that day (a salary's rate of that month), and the rest of its overtime at the overtime rate × the multiplier. A salary paid past an overtime threshold that month has no premium on projects: all its hours are at the hourly rate (the threshold, above). Project cost prices labour the same way ([projects.md](projects.md)), so the two agree on every entry; Project pay leaves out the paid break.
- **Left out:** paid breaks and salary deductions. They aren't worked on any project, so project pay differs from Salaries for hourly staff by exactly their paid breaks. Project cost is different: it counts everyone's paid break as labour ([projects.md](projects.md)).
- Overtime and made-up hours are worked out from the person's whole day and month, whichever projects they worked on. A leaver's hours ledger ends on the day they left, as in Salaries.
- Grouped by project, then person and currency; projects are ordered by USD pay. Someone with no pay record is listed with `priced: false` and paid 0.

