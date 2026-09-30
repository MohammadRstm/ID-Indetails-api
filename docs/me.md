# Self-service (`/api/me`)

What signed-in people see about themselves. These endpoints only ever return the caller's own data and never take a user id. Pay, hours, overtime and schedule serve Design people for now: they need `work_entry:view_own`, which only people of the Design domain hold. My requirements serves both domains (`requirement:view`). The caller's work entries, summary, projects and day are in [design.md](design.md); their profile is in [employees.md](employees.md), and their profile photo (`PUT` and `DELETE /api/me/photo`) in [files.md](files.md).

## My pay

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/pay?month=YYYY-MM` | `work_entry:view_own` | The caller's pay for a month (this month by default) |

- The month can't be in the future or before the caller joined (`400`). `joinedOn` is the day they joined and `firstMonth` its month.
- `salary` is exactly what the Salaries screen shows for the caller (`SalaryCalculator`, loaded through the same `IPayrollInputs`), and `null` when they have no pay record for the month (admins and super admins often have none).
  - **The break is paid.** Salaried staff are paid the whole day (7h required is an 8h paid day), and their hourly rate is salary ÷ 173.33. Hourly staff are paid the break of each working day they worked on top of the hours they log (`paidBreakDays`, `paidBreakPay`).
  - **Deductions** (salaried, `deduction` and `deductionMinutes`): in a closed month, the hours still short, each whole missed working day costing the full paid day (required hours plus the break); unpaid excuses are deducted the same way, an unpaid day off costing its full paid day. Past months never change.
  - While a salaried month runs:
    - `earnedSoFar` is the salary accrued so far plus overtime pay. It accrues with the hours required so far (required so far ÷ the whole month's) where the hours ledger applies, otherwise with the working days passed (none on the 1st).
    - `wouldLose` is what would be deducted if the month closed as short as it is now: the shortfall plus the paid break of each whole missed day, at the hourly rate, exactly as Salaries deducts it (`SalaryCalculator.ShortfallMinutes`).
    - `fullMonthEstimate` is salary plus overtime so far, less `wouldLose`.

    They are estimates: the month's pay is only final once it closes. Otherwise `earnedSoFar` is the month's total.
- `ledger`: the month's hours ledger (see My hours), null where it doesn't apply.
- `days`: the month's days up to today, newest first: what each asked for, what was logged, inside hours, overtime, and the difference (null for today, an excused day or a day off). Days off with nothing logged are left out.
- `excuses`: the month's excuses, paid or not, with their note. Who made them isn't shown.

## My requirements

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/requirements` | `requirement:view` | The caller's open requirements, soonest deadline first. In both domains: it is the whole of an Execution employee's My work page |

Rules (`Business/Requirements/MyRequirements.cs`):

- The requirements assigned to the caller on projects they can see that are active or on hold. Completed and archived projects are left out.
- Open ones are listed soonest deadline first; those without a deadline come last, oldest first. Each has `isOverdue` (open with a deadline before today), `daysLeft` (negative once overdue, null without a deadline), its project, and everyone else it is assigned to.
- Counts: `projects` with at least one of them, `overdue`, `dueSoon` (a deadline from today to 7 days ahead) and `done` (already done, not listed).

## My schedule

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/schedule?date=` | `work_entry:view_own` | The caller's week, the company policy in force, and one day's hours (today by default) |

- `schedule`: the caller's week as it stands today, with the same shape as the Schedules screen. It is their own schedule where they have one (`isOwn`, `ownSince`), otherwise the company week. Each working day has its start, end and required minutes, and `nextChangeOn` is a change dated in the future.
- `companyWeek`: the company policy in force today. Its break and overtime multiplier apply to everyone.
- `day`: the day asked for (`date`, today by default, from 2000 to 2100): whether it is a working day, its hours, required minutes and paid break, and what the caller has logged on it. `insideMinutes` is logged time that isn't overtime, and `leftMinutes` is what the day still requires. On a day off everything logged is overtime.
- `day.monthShortMinutes`: what that day's month is short of its required hours so far, from the hours ledger (null where it doesn't apply). Overtime logged in the month first makes this up, as normal hours.
- This is what the log-work form previews against (the prototype's overtime warning): the day's hours and what is already logged, with the entries themselves from `GET /api/me/day?date=`.

## My hours

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/hours?page=1` | `work_entry:view_own` | The caller's hours ledger, month by month, newest first |

- The ledger applies to Design employees only. For anyone else (admins included) `applies` is false and there are no months.
- The months run from this month back to the month the caller joined, 12 a page (`totalMonths` counts them all). Each is built exactly as on the Salaries screen (`HoursLedger`, through `SalaryCalculator.LedgerOf`):
  - `expectedMinutes`: the required hours of the working days so far (up to yesterday while the month runs). `expectedFullMonthMinutes` covers the whole month, from the day the caller joined.
  - `excusedMinutes` and `wholeMonthExcused`: excused time, paid or not.
  - `insideMinutes`: work within the caller's hours.
  - `madeUpMinutes`: overtime that made up missing hours, in date order.
  - `overtimePaidMinutes`: the rest of the overtime.
  - `shortMinutes` and `missedDays`: what is still missing, and the working days with no work within hours and no excuse.
  - `excuses`: the month's excuses, paid or not, with their notes. Who made them isn't shown.
- `status` is `excused` (the whole month), `short` (a closed month still short), `behind` (the month in progress is short so far), `on_track` or `complete` (`Business/Payroll/MyTime.cs`).
- Someone else's ledger, for their profile, is `GET /api/employees/{id}/hours` (`salary:view`; see [design.md](design.md)).

## My overtime

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/overtime?month=YYYY-MM` | `work_entry:view_own` | The caller's overtime in a month (this month by default), day by day |

- Overtime is time outside the caller's hours, all time on a day off, and time within hours beyond what the day requires (taken from the day's latest work).
- The month can't be in the future or before the caller joined (`400`).
- Totals: overtime, how much of it made up missing hours and how much is paid as overtime (both null where the ledger doesn't apply), and the number of entries.
- `days`: days with overtime, newest first, each with whether it was a day off and every entry: project (with its icon and tint), description, start and end, length, overtime and the part that made up missing hours. No money is shown.
- Someone else's overtime, for their profile, is `GET /api/employees/{id}/overtime` (`work_entry:view_all`; see [design.md](design.md)).

## My board layouts

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/board-layouts/{key}` | Signed in | The caller's saved card order for a board; `order` and `savedAt` are null while nothing is saved (the board shows its default) |
| `PUT /api/me/board-layouts/{key}` | Signed in | `{ order: [cardKey…] }` creates or replaces it |
| `DELETE /api/me/board-layouts/{key}` | Signed in | Goes back to the default order (`204`, also when nothing was saved) |

- The boards are the domain overviews and the company overview: `overview-design`, `overview-execution` and `company-overview` (`Constants/BoardKeys.cs`). Any other key is `404`.
- Only the order is kept (`user_board_layouts.layout` holds `{ "order": [...] }`), as in the prototype: a card takes the width of the slot it lands in. The client drops keys it no longer knows and places new cards at their default spot, so the server only checks the shape: 1 to 30 card keys, each lowercase letters, digits and dashes (40 at most), none twice (`400`).
- The client saves on every move and deletes the layout when the order matches the default again. Nobody reads or changes anyone else's layout.
