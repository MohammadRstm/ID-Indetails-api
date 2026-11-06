# Projects

A project belongs to one domain. The rules live in `Business/Projects/`. A project of a domain the caller can't see reads as `404`, for every endpoint below. What is recorded on Execution sites (BOQs and variation orders, site expenses, ground labour) is in [execution.md](execution.md).

## Endpoints

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects?search=&status=&clientId=&billingType=&domain=&ownerId=&year=&quarter=&sort=start&page=1&pageSize=25` | `project:view` | Project list, newest start first unless sorted (see "The project list"). Each row has `money` (revenue, recognised, cost, profit, margin, invoiced, outstanding, `hasUnconverted`, `unpricedLabourMinutes`) only for callers with `financial:view` (see [finance.md](finance.md)), and Design rows have `work` only for callers with `work_entry:view_all` |
| `GET /api/projects/totals?…` | `financial:view` | The list's header: with the same filters as the list (paging aside), how many projects match (client and internal), their revenue and cost, the profit of the client ones, what the internal ones cost, and `hasUnconverted` when a cost or invoice has no rate. In USD |
| `GET /api/projects/{id}` | `project:view` | A project, with `heldSince` while it is on hold, and an Execution site's `team`: the people who recorded its site expenses or labour weeks (the 5 most recent, and how many in all), since projects have no member list. List rows carry the same `team`. `money` is included only for callers with `financial:view` |
| `GET /api/projects/{id}/report?kind=&from=&to=&sections=` | `financial:view` and `work_entry:view_all` | A Design project's report as a PDF, internal or the client summary ([documents.md](documents.md)) |
| `GET /api/projects/{id}/cost-timeline` | `financial:view` | Its cost and payments week by week, from its start to this week (or the week it was finalised), in USD: each week's hours, labour cost (paid breaks included) and total cost, and the cost and payments to date. Weeks start on the company's week start. For the payment-vs-cost and weekly labour charts and the project report. Either domain; Execution sites also have `/site-money/timeline` |
| `POST /api/projects` | `project:create` | Creates a project (below) |
| `PUT /api/projects/{id}` | `project:edit` | Replaces title, description, icon, tint, target date and owner. `startDate` (optional) moves the start date: never into the future, and on a site never past the week of its first ground labour reported (`400` on `startDate`); moving it earlier moves the site's first labour defaults with it |
| `PUT /api/projects/{id}/billing` | `project:edit` and `financial:view` | Design only: client work or internal, and the client. Becoming internal drops the payment to 0 |
| `PUT /api/projects/{id}/payment` | `project:edit` and `financial:write` | Design client projects only: `{ revenue, description }`, the customer payment and why it changes (see "Payment changes") |
| `PUT /api/projects/{id}/pricing` | `project:pricing_manage` | Execution only: `{ supervisionRate?, tvaRate?, description }`, the supervision rate and TVA (left out, one stays as it is), and why they change. The work price (`projectFee`) is never sent: `400`. There is no material markup any more |
| `GET /api/projects/{id}/payment-changes` | `financial:view` | Every change of the agreed payment, newest first (see "Payment changes") |
| `POST /api/projects/{id}/hold`, `/resume`, `/complete`, `/archive`, `/reopen` | `project:change_status` | Status changes (below) |

Employees hold `project:view`, so they see every project of their domain in any status, with the client by name and no money.

## The project list

- **Filters:** `search` (every word in the title or the description), `status`, `clientId`, `billingType`, `domain`, `ownerId`, and `year` with an optional `quarter` (1–4). `excludeArchived=true` leaves archived projects out when no `status` is asked for: the app's "All", where archived projects show only once Archived is chosen. A project was **open during** a period when it started by the period's end and wasn't completed before its start (as in the prototype); a period that hasn't started yet matches nothing. A quarter needs a year (`400`).
- **Sort** (`sort`): `start` (newest start first, the default), `activity` (most recent work first; a project without work counts from when it was added), `hours` (most logged time first), `progress` (furthest along first), `name` (A to Z) or `client` (A to Z, projects without a client last). Money isn't sortable here (decision M7).
- **`work`** (Design rows, `work_entry:view_all` only; left out otherwise): all-time minutes, the activity level and minutes of the last 14 days (busy from 45 h a week, steady from 15 h, quiet with any work), minutes of the last 8 weeks ending with this one (the company's week start), when the latest work started, the 4 people who logged the most time and how many logged any. Loaded for the whole page at once, not per row.

## Creating a project

- The start date is today (company time zone). The owner defaults to the caller.
- **Design:** `billingType` is `client` (a `clientId` is required, `revenue` optional) or `internal` (no client, no revenue). No work price. Sending a `revenue` needs `financial:write` (`403`; on an Execution site, `financial:view`): admins create the project and a super admin sets its customer payment later.
- **Execution:** always client work, with a `clientId`, and optionally a `supervisionRate` (a fraction, 0 to 1; left out, **10%**). Sending it needs `project:pricing_manage` (`403`). There is no `materialMarkup` any more (it is ignored if sent). The **work price** (`projectFee`) starts at 0 and is set only by agreeing a BOQ: sending `projectFee` is `400`. `revenue` is never sent; it is calculated. A project takes no payment when it is created: a down payment is recorded as a payment afterwards. A `supervisionRate` on a Design project is `400`.
- **TVA:** `tvaRate` (0 to 1, needs `financial:view`) on client work; left out, it is 11% (see [billing.md](billing.md#tva)). Its first labour defaults can be set too (`labourDefaults: { workers, dailyPay }`, needs `labour_report:manage`; see [execution.md](execution.md)).
- The client must belong to the project's domain (`400`) and not be archived (`422 client_archived`).
- **Owner:** an admin of the project's domain who isn't deactivated, or a super admin; never an employee (`422 owner_not_allowed`). The rule applies when a project is created and when its owner changes.
- **Departments (Design):** `departmentIds`, the Design teams working on it, in equal shares. The app asks for them on client work; left out, the project has none and works as before departments. See "Departments" below.

## Departments

A Design project is worked on by one or more departments (teams; the app calls them departments). Each has a **share** of the price, a fraction to four places, and the shares add up to exactly 1. One department means the whole price is its own. Every Design department on a project names it a **Full file** (`departmentsName`).

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /api/projects/{id}/departments` | `project:view` | The departments. With `financial:view`, each one's `share` and `money`: `budget` (the price before TVA × its share), `cost` and `minutes` of the hours logged in it, `left` (below zero when it is over its budget), and each person's hours and cost month by month with that month's hourly rate. `other` holds hours of people in no department, or in one no longer on the project. |
| `GET /api/projects/{id}/departments/history` | `financial:view` | Every change of the split, newest first: `kind` (`created`, `departments`, `shares`), the departments and shares `before` and `after` (named as they were then), the reason, who and when |
| `PUT /api/projects/{id}/departments` | `project:edit` | `{ departmentIds }`: the departments. Any change puts the shares back to equal; hours waiting for an added department go through (below). A department with hours logged on the project stays (`422 department_has_hours`). |
| `PUT /api/projects/{id}/departments/shares` | `financial:write` | `{ shares: [{ departmentId, share }], reason? }`: a share for each department, adding up to 100% (`400`); the reason is optional, kept in the history when given |

- Execution sites have no departments (`422 not_for_this_domain`). A finished project's departments and shares are locked with its money (`422 project_locked`).
- **Each hour stays with its department.** A work entry keeps its logger's department when it was logged (`work_entries.team_id`): their cost counts in that budget even after they move to another department. Moving an entry to another project counts it in the logger's department then. Costs are the project's labour costs as they are (pay, overtime, paid breaks), only grouped. Expenses stay with the project; the project's profit and its floor are unchanged, while a department may go over its budget.
- **Logging on a project with departments needs one of them.**
  - Hours logged by someone whose department isn't on the project are **kept**, not logged: `422 department_not_on_project`, as a blocked change of kind `work_entry_department`. The project's owner and every super admin get `log_department_blocked`.
  - Once that department is added, the hours go through on their own, as the person who logged them and through the same checks. They hear it (`work_entry_released`, `released: true`). Hours that no longer fit (they overlap newer ones, the month closed, the profit floor refused them) are dropped and the person is told why (`released: false`, `reason`), to log them again.
  - Such hours are never forced (`422 department_still_missing`); whoever sees the money may dismiss them.
  - Someone with no department logs only on projects with none (`422 department_required`). Moving hours to a project without the logger's department is refused (`422 department_not_on_project`).
- A team that is a department of a project can't be deleted (`422 team_on_projects`): rename it instead.

## Design variation orders

A Design client project's price changes with **variation orders** (VOs): lines before TVA that add work (above zero) or take it out (below). The pencil on the customer payment stays for correcting mistakes only. Each VO is numbered `VO-YYYY-NNN` in a series of Design's own (Execution's BOQ variation orders have theirs).

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /api/projects/{id}/design-vos` | `financial:view` | The project's VOs, newest first: lines, total before TVA, the TVA at the project's rate, each department's part, who made, sent, applied or cancelled it and when |
| `GET /api/projects/{id}/design-vos/outside-scope` | `financial:view` | `{ from, minutes }`: the hours logged outside the agreed scope since the last applied VO, or the project's start |
| `POST /api/projects/{id}/design-vos` | `financial:write` | `{ title, lines: [{ description, amount }], split, allocations, showOutsideScope, apply, chargeTva? }` (`201`). `apply: true` adds it to the price now; otherwise it is **pending**, sent for the client to confirm. `chargeTva: false` leaves the TVA off it |
| `PUT /api/design-vos/{id}/tva` | `financial:write` | `{ chargeTva }`: adds the project's TVA to a pending or applied VO, or takes it off (`422 design_vo_not_pending` on a cancelled one). Writes a `design_vo_tva_set` event when it changes |
| `POST /api/design-vos/{id}/confirm` | `financial:write` | The client agreed: the pending VO joins the price (`422 design_vo_not_pending` otherwise) |
| `POST /api/design-vos/{id}/cancel` | `financial:write` | Takes a pending VO back; its number isn't used again |
| `GET /api/design-vos/{id}/pdf` / `POST …/email` | `financial:view` / `financial:write` | Its PDF, or emailed to the client with it (`{ to, note }`, `204`); see [documents.md](documents.md) |

- **Applying** adds the VO's total to the price, with a payment change (`VO-2026-003: Guest house`) and a `revenue_changed` event, through the profit floor: a VO that lowers the price may be refused (`422 profit_floor`). It then stays pending as a blocked change of kind `design_vo`, which whoever sees the money can force. No VO takes the price below zero (`422 design_vo_below_zero`). Lines are to the cent, never zero, and can't add up to nothing.
- **Split between the departments:**
  - `shares` (the default) divides the total by the project's shares when it is applied, to the cent (what rounding leaves goes to the biggest share). Those amounts are kept: changing the shares later never moves money a VO already gave.
  - `departments` gives amounts by hand (`allocations: [{ departmentId, amount }]`) to some of the project's departments, adding up to the total.
  - A project with no departments keeps the VO's total at project level.
- **Budgets:** a department's budget is the price before VOs × its share, plus what applied VOs gave it. A department with a VO's money stays on the project (`422 department_has_hours`).
- **Outside-scope hours:** with `showOutsideScope`, the VO shows the hours logged outside the agreed scope since the last applied VO (or the project's start), fixed when it is made.
- **TVA:** a VO carries the project's TVA unless whoever writes the money (the super admin) leaves it off, when it is made or later. One without TVA (`tvaRate` 0) is in the price as it is: what the client pays is the VOs without TVA plus the rest with its TVA, each payment's TVA share is its part of the TVA there is, and its PDF reads "TVA: None".

## Money

- **Design revenue** is the agreed payment, typed in. **Execution revenue** is the agreed amount: the **work price** (`projectFee`, set only by agreeing or withdrawing BOQs and 0 until the first is agreed; see [execution.md](execution.md#boqs-and-variation-orders)) plus the company's **supervision** (`work × supervisionRate`, `money.supervisionUsd`). TVA is on the work, never on supervision (see [billing.md](billing.md#tva)): `revenueWithTva` = revenue + work × `tvaRate`. A site with no price yet is all 0. Site expenses are plain costs: nothing is charged to the client for them, and the money response has no markup or material-charge fields. Ground labour is never billed. The profit floor measures profit on this agreed amount (work plus supervision, net of TVA).
- The Execution agreed amount is calculated live while the project is open, and frozen into `projects.revenue` when it is finalised. Finalising needs nothing to have been sent or converted: the price needs no exchange rate.
- While a project is finalised (`money.isLocked`), billing, revenue, fee and rates can't change (`422 project_locked`).
- **Payment changes** (`project_payment_changes`): every change of a client project's agreed payment is kept with why it changed, for the project report. Never edited or deleted.
  - Each row has `amount` (Design's customer payment, Execution's project fee, USD), `previousAmount`, `tvaRate` and `previousTvaRate`, `materialMarkup` and `previousMaterialMarkup` (history only: rows written before markups went carry them, newer rows have none), for Execution `supervisionRate` and `previousSupervisionRate` (fractions, the rate after the change and before it, like TVA's; null on Design rows and on rows from before supervision), `description` (required, at most 500 characters), `changedAt` and `changedBy` (`{ id, name }`, null once the person is gone).
  - The first row is the payment agreed at the start (`previousAmount` null, "Agreed at the start"), written when a client project is created. Projects from before this history got one with the payment they had then.
  - `PUT …/payment` and `PUT …/pricing` must change something: the same payment again is `400` (`revenue`, or `supervisionRate` for pricing, which is the first of its two rates). A change of supervision alone is a change: it adds a row with its reason, carrying the new and previous supervision rate, and a `supervision_rate_set` event (the rates in its payload, shown only with `financial:view`). An internal project has no payment (`400 revenue`: make it client work first). An Execution site's payment is its work price, set by agreeing BOQs, so `PUT …/payment` on one is `422 not_for_this_domain`. Agreeing or withdrawing a BOQ adds a row (`amount` the new work price, `previousAmount` the old one, the BOQ's number or the withdrawal reason as `description`, the rates unchanged) and a `project_fee_set` event.
  - Turning a client project into internal work writes a row dropping the payment to 0 ("Turned into internal work"). Becoming client work again starts at no payment, set with `PUT …/payment`.
  - The money activity events (`revenue_set`, `revenue_changed`, `project_fee_set`, `supervision_rate_set`; older feeds also hold `material_markup_set`) are still written, as expected revenue is built from them, but never carry the description: the activity feed shows its details to people without `financial:view`.
- A project with any invoice or payment can't move to another client or become internal (`422 project_has_billing`), because invoices and payments keep the client they were made for.
- `money.billing` is the project's cash position in USD, kept apart from revenue: `invoiced`, `outstanding`, `received`, `onAccount`, `notYetInvoiced` (revenue − invoiced − on account), `leftToInvoice` (revenue − invoiced), `owed`, `paidAhead`, `issuedCount`, `paymentCount`, `overdue`, `overdueCount`, `oldestOverdueDays`, `drafts` and `unconvertedInvoices[]` (invoices with no rate on their issue day, or drafts with none today, left out of the USD figures). It comes from the same billing rule and batch load as the lists and the Financials page (`Business/Billing/ProjectBilling.cs`; see [billing.md](billing.md) and [finance.md](finance.md)).
- Cost, profit, margin and the projections come from the shared finance calculator (below).

## Cost, profit and margin

Every project money figure comes from one place: `Business/Finance/ProjectFinance.cs`, loaded by `IProjectFinanceService` with one query per cost source however many projects are asked for. Domain features add their costs to its sources; they never calculate their own.

- **Cost** (`money.cost`) = employee labour + expenses + site expenses + ground labour, in USD. `cost.siteExpenses` is Execution's (what was recorded as [site expenses](execution.md#site-expenses)); `cost.expenses` is Design's project expenses.
  - **Labour:** each work entry's hours × the person's hourly cost in force on the day it was worked (company time zone). Monthly pay costs the salary ÷ that month's paid hours on the person's schedule, so a full-time month's labour adds up to the salary. **Overtime** is priced as Salaries pays it (28 Oct 2026): the part of an entry paid as overtime costs the pay's overtime rate × the company's multiplier that day, while overtime that made up a short month costs the normal rate. From 1 November 2026 a salary is paid its overtime only past the month's overtime threshold, for the month as a whole, so its hours carry no overtime premium on any project: they all cost the normal rate, and the premium is a company cost ([company.md](company.md), "The overtime threshold"). Hourly pay keeps its premium. Overtime is decided per day across all the person's projects, so their whole months are loaded (`IPayrollInputs.EntrySplitsAsync`). The timesheet export shows each entry's hourly rate, overtime paid hours, overtime rate (× the multiplier) and cost. **The paid break** is labour cost too, for everyone with a pay record, hourly and salaried alike (7 required hours are 8 paid hours; from 1 December 2026 the break is 0, the whole day is logged on projects, and there is no break to share): on each working day with some in-hours work, the day's paid break (none on days of 6 hours or less), at that day's rate, shared across the day's projects by the in-hours minutes worked on each (`Business/Finance/PaidBreaks.cs`). Project pay (see [company.md](company.md)) still leaves breaks out; it is a separate report of what each project paid for the hours worked on it. Time before someone's first pay record uses their earliest rate; time of someone with no pay record can't be priced and is counted in `unpricedLabourMinutes`. Hours and breaks in a month the person was an admin or super admin on any day cost nothing: that month is a fixed salary, a company cost (see [company.md](company.md)). A promotion therefore frees its whole month, but never changes the labour of the months before it (`ProjectFinance.RatesOf`).
  - **Expenses and site expenses:** converted to USD at the company exchange rate in force on their day (`spentOn`, the day the money was spent). An amount with no rate that day is left out and listed in `cost.unconverted`, in its own currency (`source` is `expenses` or `site_expense`).
  - **Ground labour:** workers × daily pay, in USD.
- **Profit** = revenue − cost. Internal projects have no revenue.
- **Margin** = profit ÷ revenue, only once it means something: the project is completed or archived, at least 5% along, or has at least 10 hours logged. Otherwise `null`.
- **Projected** cost, profit and margin = cost ÷ progress, for open client projects at least 5% along.
- **Recognised to date** = revenue × progress, or all of it once completed or archived.
- **Execution:** `cashProfit` = received net of TVA − cost − moved to the pocket money + paid by it.
- **Cash by week** (`ProjectFinance.CashByWeek`): payments received and cost added up week by week, Monday to Sunday. Execution sites show it on `GET /api/projects/{id}/site-money/timeline` (see [execution.md](execution.md)).

## Profit floor

A client project's profit may not reach zero unless someone who sees its money says so (`Business/Projects/ProfitFloorRules.cs`, `Services/Projects/Implementation/ProfitFloor.cs`).

- **Which projects:** client work with an agreed amount: Design's payment (`revenue > 0`), an Execution site's work price (`projectFee > 0`). A site with no price yet is outside the floor: no refusals and no `profit_low` or `profit_at_zero`. Internal work is cost by nature and isn't held to it.
- **Which profit:** the project's profit with the pocket money counted, on its agreed amount: Design's payment, an Execution site's work price plus supervision (net of TVA, which is never part of it).
- **Guarded changes:** adding an expense, adding or changing site expenses (kinds `material_add` and `material_update`, as they were stored), submitting a labour week, logging or changing hours, lowering the payment or changing a site's supervision or TVA, and agreeing or withdrawing a BOQ or VO (`boq_agree`, `boq_withdraw`: the price they leave is what the floor compares, so a withdrawal that takes the price away, or a VO that lowers it past the costs, is refused and kept for forcing; see [execution.md](execution.md#boqs-and-variation-orders)). Each takes the profit before it (with the project locked, except hours) and checks it again after saving, inside its transaction.
  - A change that **raises** the profit, or leaves it as it was, always passes, so a project at zero or below can still be helped (a higher payment, a removed expense).
  - A change that **lowers it to zero or below** is refused: `422 profit_floor`, with `blockedChangeId` in the error body. Nothing of it is saved. The error handler then keeps the request as a **blocked change** (`blocked_changes`: the request as JSON with its route ids, who, when) and sends `change_blocked` to whoever sees the domain's money (`financial:view`), the requester aside.
  - A change that lowers it to **10% of the agreed amount or less** passes and sends `profit_low`, once per project. An Execution site also asks the pocket money for help (a pending request with no requester, "Close to zero profit"; see [execution.md](execution.md)).
- **Blocked changes** (`financial:view`, in a domain the caller sees):

  | Method and path | Purpose |
  |---|---|
  | `GET /api/blocked-changes/{id}` | The change in words (`summary`, e.g. "Expense: Tiles, 1,000.00 USD"), its `kind`, project, `requestedBy`, `status` (`pending`, `forced`, `dismissed`), who decided it, and `profitNow` |
  | `POST /api/blocked-changes/{id}/force` | Makes the change again through the same service and checks, past the floor. Hours are logged as the employee who logged them (`ICurrentUser.ActAs`). Then `profit_at_zero` goes once to whoever sees the money |
  | `POST /api/blocked-changes/{id}/dismiss` | Drops it |

  - A change is decided **once**: it is marked forced or dismissed before anything else (an atomic update on a pending row), so a second force or dismiss is `422 blocked_change_decided`. If the forced change fails on its own checks (the project closed meanwhile, the labour week changed: `409 labour_week_changed`), it is pending again and the error is returned.
  - Its `resultId` is what forcing made: the expense, the first site expense, the work entry.
- **Changes nobody made** (pay, schedules, exchange rates, a voided payment) aren't refused: the hourly scheduler sends `profit_low` and `profit_at_zero` for them, under the same keys, so nobody hears it twice (see [notifications.md](notifications.md)).

## Status

`Business/Projects/ProjectStatusRules.cs`:

| From | Allowed |
|---|---|
| active | hold, complete, archive |
| hold | resume, complete, archive |
| completed | archive, reopen |
| archived | reopen |

Anything else is `422 invalid_status_change`, and asking for the current status is `422 status_unchanged`.

- **Keeping what was paid ahead:** `POST /api/projects/{id}/complete` takes an optional `{ keepPaidAhead: true }`. When a client project's client paid more than its price (TVA included), completing it first raises the price to exactly what was received, so it closes on what it really earned: a site gets an agreed VO "Paid beyond the agreed price" (a client BOQ when none was agreed), sized so work + supervision + TVA equals what was received; a Design project's payment is raised the same way. Both are written to the payment history and the feed like any price change. Without it, what was paid ahead stays a balance owed back and doesn't count in the profit.
- **Excusing or writing off what is still owed:** the same request takes an optional `{ settleOwed: "excused" }` or `{ settleOwed: "written_off" }`. When a client project's client still owes part of its price (TVA included), completing it first lowers the price to exactly what was received, so the client owes nothing more. It is always all of what is owed.
  - **Excused** is a discount agreed with the client (the CEO forgave it). **Written off** is revenue lost: the client never paid. Both lower the revenue and the profit by the same amount (TVA excluded); they differ in what they are called.
  - A site gets an agreed VO "Excused at completion" or "Unpaid, written off at completion", with a negative amount, sized as above; a Design project's payment is lowered the same way. Both are written to the payment history and the feed like any price change, plus an `owed_excused` or `owed_written_off` event whose `amount` is what was owed.
  - The project keeps what was settled and how (`settled_owed_usd`, `settled_owed_as`), shown in its money as `settledOwedUsd` and `settledOwedAs`. Reopening the project keeps the lowered price, as it keeps a raised one; a payment that arrives later shows as paid ahead.
  - Only whoever changes the project's money may: `financial:write` on Design, `project:pricing_manage` on a site (`403` otherwise). A client who owes nothing is `422 nothing_owed`; a site whose client paid less than its permits alone is `422 owed_beyond_work`.
  - Without it, what is owed stays owed after the project closes. Archiving and Proton never settle it.
- **Auto-archive:** while a domain's `autoArchive` setting is on, the scheduler (every hour) archives its completed projects when the completion, the latest activity event (invoices, payments, edits…) and the latest client portal message or (not deleted) comment are all at least 90 days old. It is a system event: `project_archived` with no actor and `automatic: true` in the payload, and no notification.
- **Finalising** happens when a project is completed, or archived without being completed first. Revenue is locked (`revenue_locked_at`, `finalized_at`, `completed_at`), the Execution agreed amount is frozen into `projects.revenue`, and every open requirement is ticked as done with `auto_done` set.
- **Reopening** clears those dates, returns Execution revenue to being calculated live, and reopens exactly the `auto_done` requirements.
- Archiving a completed project keeps its finalisation. Resuming from hold changes nothing else.

## Deleting a project

Only a project made by mistake (a flake) is ever deleted, with everything on it. Old real projects are never deleted: they stay archived, and their records are small. If space ever runs short, the files are what take it (photos, attachments, backups), and clearing an old project's files would be its own action, never this one.

| Endpoint | Permission | What it does |
|---|---|---|
| `GET /api/projects/{id}/deletion` | `project:delete` | `{ canDelete, blockers, removes }`: why it can't be deleted, one line each, or what goes with it, in words ("3 site expenses") |
| `DELETE /api/projects/{id}` | `project:delete` | `{ title }`, its name typed exactly (spaces around it aside), else `400 validation_failed`. `204` once deleted |

- `project:delete` is the super admins' by default, and can be given to anyone.
- **Only an archived project**, and only one that never held anything real. Each of these keeps it archived for good (`422 project_not_deletable`, saying which):
  - issued invoices, voided ones included, since their numbers must stay;
  - payments received;
  - pocket money moves;
  - supervision taken;
  - hours logged. Hours in a closed month never change, so they keep it for good; this month's are moved to the right project first, after reopening it.
- **What goes with it:** its requirements, meetings, staff, departments and shares, BOQs and VOs (with their lines and deliveries), Design VOs, price changes, project and site expenses, weeks of labour, site logs, draft invoices, voided payments, removed supervision withdrawals and deleted hours entries, waiting price changes and pocket money requests, its notifications and its activity, and its client portal (items, files, messages, comments, uploads, invitations and the clients' access to it). The portal's stored files are deleted from the content store once the transaction commits; the weekly check removes any left behind.
- **What stays:** the client and the people. A website page that showed it stays, no longer linked to it. A chat message that shared one of its deleted hours entries keeps its text.
- **The one trace it leaves:** a `project_deleted` event with no `project_id`, the deleted project's id as `subject_id`, and its `title` and what it held (`removed`) in the payload.
- It can't be undone, except from a backup.

## Requirements

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects/{id}/requirements` | `requirement:view` | Open requirements first (oldest first), then done ones (most recently done first), with totals, the overdue count and progress |
| `POST /api/projects/{id}/requirements` | `requirement:manage` | `{ text, deadline, assigneeIds }` |
| `PUT /api/projects/{id}/requirements/{reqId}` | `requirement:manage` | Replaces text, deadline and assignees |
| `POST /api/projects/{id}/requirements/{reqId}/done`, `/undone` | `requirement:manage` or `requirement:complete_own` | Ticks or unticks. Asking for the current state changes nothing. A manual tick clears `autoDone` |
| `DELETE /api/projects/{id}/requirements/{reqId}` | `requirement:manage` | Soft delete (`deleted_at`) |

- Requirements of a completed or archived project can't change until it is reopened (`422 project_closed`). Projects on hold stay editable.
- **Assignees** are employees (not admins) of the project's domain who are active or invited (`422 assignee_not_allowed`). They don't have to be project members. With `requirement:complete_own` (every employee by default) an assignee ticks their own requirement and unticks it only if they ticked it themselves; anything else is `403`. Editing, assigning and deleting stay with `requirement:manage`.
- A requirement is **overdue** when it is open and its deadline is before today (company time zone).
- Every change writes `requirement_added`, `requirement_updated`, `requirement_done`, `requirement_reopened` or `requirement_deleted` (the requirement is the subject, the text is in the payload).
- People newly assigned (on creation or an edit) get a `requirement_assigned` notification with the requirement text, deadline and project title. Adding a requirement also bumps the domain's rolling `requirements_added` notification for the project, and ticking one sends `requirement_done` (see [notifications.md](notifications.md)). Removing someone, unticking and deleting send none.

## Expenses

These are **Design** projects' expenses. An Execution site records its costs as [site expenses](execution.md#site-expenses) instead: `POST …/expenses` on a site is refused with `422 expense_execution_uses_site_expenses`.

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects/{id}/expenses` | `expense:manage` or `financial:view` | Newest first, with one total per currency |
| `POST /api/projects/{id}/expenses` | `expense:manage` | `{ label, amount, currency, category, spentOn, note }` |
| `DELETE /api/projects/{id}/expenses/{expId}` | `expense:manage` | Soft delete (`deleted_at`) |

- Only while the project is open and its revenue isn't locked (`422 project_closed`). Expenses can't be edited: remove and add again.
- `amount` is more than zero with at most 2 decimals. `currency` is USD or LBP. `spentOn` defaults to today.
- `category` is one of `assets`, `services`, `contractor`, `software`, `travel`, `hardware` or `other` (the default).
- **Totals are per currency** and are never converted or added across currencies. The project's `money.expenseTotals` shows the same totals.
- Events: `expense_added` and `expense_removed`, with the amount in the `amount` column and the label, category and currency in the payload.

## Progress

`progress_pct` is a cache of done ÷ live requirements, rounded to a whole percentage (`Business/Projects/ProgressCalculator.cs`). A project with no requirements is 0% while open and 100% once completed or archived. It is recalculated in the same transaction as every requirement or status change, which writes a `progress_set` event (new value in `amount`, previous in `prev_amount`) whenever it changes.

## Activity

`project_created`, `project_updated`, `project_owner_set`, `project_client_set`, `revenue_set`, `revenue_changed`, `project_fee_set`, `supervision_rate_set`, `project_hold`, `project_resumed`, `project_completed`, `project_archived`, `project_reopened`, `project_finalized` (the frozen revenue in `amount`), `progress_set`. Amounts go in the event's `amount` and `prev_amount` columns, never in the payload. A rate is a fraction (0.125 is 12.5%), so `supervision_rate_set` keeps it in the payload as `rate` and `prevRate`, with no amount; the feed shows that payload only with `financial:view`. `material_markup_set` is no longer written: older rows keep their `markup` and `prevMarkup` payload, with the same rule.
