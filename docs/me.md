# Self-service (`/api/me`)

What signed-in people see about themselves. These endpoints only ever return the caller's own data and never take a user id. For now they serve Design people: each needs `work_entry:view_own`, which only people of the Design domain hold. The caller's work entries, summary, projects and day are in [design.md](design.md); their profile is in [employees.md](employees.md).

## My pay

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/pay?month=YYYY-MM` | `work_entry:view_own` | The caller's pay for a month (this month by default) |

- The month can't be in the future or before the caller joined (`400`). `firstMonth` is the month they joined.
- `salary` is exactly what the Salaries screen shows for the caller (`SalaryCalculator`, loaded through the same `IPayrollInputs`), and `null` when they have no pay record for the month (admins and super admins often have none).
  - **The break is paid.** Salaried staff are paid the whole day (7h required is an 8h paid day), and their hourly rate is salary ÷ 173.33. Hourly staff are paid the break of each working day they worked on top of the hours they log (`paidBreakDays`, `paidBreakPay`).
  - **Deductions** (salaried, `deduction` and `deductionMinutes`): in a closed month, the hours still short, each whole missed working day costing the full paid day (required hours plus the break); unpaid excuses are deducted the same way, an unpaid day off costing its full paid day. Past months never change.
  - While a salaried month runs, `earnedSoFar` is the salary accrued with the hours required so far (required so far ÷ the whole month's) plus overtime pay, `fullMonthEstimate` is salary plus overtime so far less what the caller is behind at their hourly rate, and `wouldLose` is that amount. They are estimates: the month's pay is only final once it closes. Otherwise `earnedSoFar` is the month's total.
- `ledger`: the month's hours ledger (see My hours), null where it doesn't apply.
- `days`: the month's days up to today, newest first: what each asked for, what was logged, inside hours, overtime, and the difference (null for today, an excused day or a day off). Days off with nothing logged are left out.
- `excuses`: the month's excuses, paid or not, with their note. Who made them isn't shown.

## My requirements

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/requirements` | `work_entry:view_own` | The caller's open requirements, soonest deadline first |

Rules (`Business/Requirements/MyRequirements.cs`):

- The requirements assigned to the caller on projects they can see that are active or on hold. Completed and archived projects are left out.
- Open ones are listed soonest deadline first; those without a deadline come last, oldest first. Each has `isOverdue` (open with a deadline before today), `daysLeft` (negative once overdue, null without a deadline), its project, and everyone else it is assigned to.
- Counts: `projects` with at least one of them, `overdue`, `dueSoon` (a deadline from today to 7 days ahead) and `done` (already done, not listed).

## My schedule

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/schedule` | `work_entry:view_own` | The caller's week, the company policy in force, and today's hours |

- `schedule`: the caller's week as it stands today, with the same shape as the Schedules screen. It is their own schedule where they have one (`isOwn`, `ownSince`), otherwise the company week. Each working day has its start, end and required minutes, and `nextChangeOn` is a change dated in the future.
- `companyWeek`: the company policy in force today. Its break and overtime multiplier apply to everyone.
- `today`: whether today is a working day, its hours, required minutes and paid break, and what the caller has logged today. `insideMinutes` is logged time that isn't overtime, and `leftMinutes` is what the day still requires. On a day off everything logged is overtime.

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
- `status` is `excused` (the whole month), `short` (a closed month still short), `behind` (the month in progress is short so far), `on_track` or `complete` (`Business/Payroll/MyTime.cs`).

## My overtime

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/overtime?month=YYYY-MM` | `work_entry:view_own` | The caller's overtime in a month (this month by default), day by day |

- Overtime is time outside the caller's hours, all time on a day off, and time within hours beyond what the day requires (taken from the day's latest work).
- The month can't be in the future or before the caller joined (`400`).
- Totals: overtime, how much of it made up missing hours and how much is paid as overtime (both null where the ledger doesn't apply), and the number of entries.
- `days`: days with overtime, newest first, each with whether it was a day off and every entry: project, description, start and end, length, overtime and the part that made up missing hours. No money is shown.

## My board layouts

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/me/board-layouts/{key}` | Signed in | The caller's saved card order for a board; `order` and `savedAt` are null while nothing is saved (the board shows its default) |
| `PUT /api/me/board-layouts/{key}` | Signed in | `{ order: [cardKey…] }` creates or replaces it |
| `DELETE /api/me/board-layouts/{key}` | Signed in | Goes back to the default order (`204`, also when nothing was saved) |

- The boards are the domain overviews and the company overview: `overview-design`, `overview-execution` and `company-overview` (`Constants/BoardKeys.cs`). Any other key is `404`.
- Only the order is kept (`user_board_layouts.layout` holds `{ "order": [...] }`), as in the prototype: a card takes the width of the slot it lands in. The client drops keys it no longer knows and places new cards at their default spot, so the server only checks the shape: 1 to 30 card keys, each lowercase letters, digits and dashes (40 at most), none twice (`400`).
- The client saves on every move and deletes the layout when the order matches the default again. Nobody reads or changes anyone else's layout.
