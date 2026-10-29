# Projects

A project belongs to one domain. The rules live in `Business/Projects/`. A project of a domain the caller can't see reads as `404`, for every endpoint below. What is recorded on Execution sites (materials, ground labour, client statements) is in [execution.md](execution.md).

## Endpoints

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects?search=&status=&clientId=&billingType=&domain=&ownerId=&year=&quarter=&sort=start&page=1&pageSize=25` | `project:view` | Project list, newest start first unless sorted (see "The project list"). Each row has `money` (revenue, recognised, cost, profit, margin, invoiced, outstanding, `hasUnconverted`, `unpricedLabourMinutes`) only for callers with `financial:view` (see [finance.md](finance.md)), and Design rows have `work` only for callers with `work_entry:view_all` |
| `GET /api/projects/totals?…` | `financial:view` | The list's header: with the same filters as the list (paging aside), how many projects match (client and internal), their revenue and cost, the profit of the client ones, what the internal ones cost, and `hasUnconverted` when a cost or invoice has no rate. In USD |
| `GET /api/projects/{id}` | `project:view` | A project, with `heldSince` while it is on hold, and an Execution site's `team`: the people who recorded its materials or labour weeks (the 5 most recent, and how many in all), since projects have no member list. List rows carry the same `team`. `money` is included only for callers with `financial:view` |
| `GET /api/projects/{id}/report?kind=&from=&to=&sections=` | `financial:view` and `work_entry:view_all` | A Design project's report as a PDF, internal or the client summary ([documents.md](documents.md)) |
| `GET /api/projects/{id}/cost-timeline` | `financial:view` | Its cost and payments week by week, from its start to this week (or the week it was finalised), in USD: each week's hours, labour cost (paid breaks included) and total cost, and the cost and payments to date. Weeks start on the company's week start. For the payment-vs-cost and weekly labour charts and the project report. Either domain; Execution sites also have `/site-money/timeline` |
| `POST /api/projects` | `project:create` | Creates a project (below) |
| `PUT /api/projects/{id}` | `project:edit` | Replaces title, description, icon, tint, target date and owner |
| `PUT /api/projects/{id}/billing` | `project:edit` and `financial:view` | Design only: client work or internal, and the client. Becoming internal drops the payment to 0 |
| `PUT /api/projects/{id}/payment` | `project:edit` and `financial:view` | Design client projects only: `{ revenue, description }`, the customer payment and why it changes (see "Payment changes") |
| `PUT /api/projects/{id}/pricing` | `project:pricing_manage` | Execution only: `{ projectFee, materialMarkup, tvaRate?, description }`, the agreed amount, default material markup and TVA, and why they change |
| `GET /api/projects/{id}/payment-changes` | `financial:view` | Every change of the agreed payment, newest first (see "Payment changes") |
| `POST /api/projects/{id}/hold`, `/resume`, `/complete`, `/archive`, `/reopen` | `project:change_status` | Status changes (below) |

Employees hold `project:view`, so they see every project of their domain in any status, with the client by name and no money.

## The project list

- **Filters:** `search` (every word in the title or the description), `status`, `clientId`, `billingType`, `domain`, `ownerId`, and `year` with an optional `quarter` (1–4). A project was **open during** a period when it started by the period's end and wasn't completed before its start (as in the prototype); a period that hasn't started yet matches nothing. A quarter needs a year (`400`).
- **Sort** (`sort`): `start` (newest start first, the default), `activity` (most recent work first; a project without work counts from when it was added), `hours` (most logged time first), `progress` (furthest along first), `name` (A to Z) or `client` (A to Z, projects without a client last). Money isn't sortable here (decision M7).
- **`work`** (Design rows, `work_entry:view_all` only; left out otherwise): all-time minutes, the activity level and minutes of the last 14 days (busy from 45 h a week, steady from 15 h, quiet with any work), minutes of the last 8 weeks ending with this one (the company's week start), when the latest work started, the 4 people who logged the most time and how many logged any. Loaded for the whole page at once, not per row.

## Creating a project

- The start date is today (company time zone). The owner defaults to the caller.
- **Design:** `billingType` is `client` (a `clientId` is required, `revenue` optional) or `internal` (no client, no revenue). No fee or markup. Sending a `revenue` needs `financial:view` (`403`): admins create the project and a super admin sets its customer payment later.
- **Execution:** always client work, with a `clientId`, and optionally a `projectFee` (the site's **agreed amount**, before materials) and a `materialMarkup` (a fraction: `0.10` is 10%, 0 to 5). Sending either needs `project:pricing_manage` (`403`); left out, the site starts at 0 and with no markup until someone with pricing sets them. `revenue` is never sent; it is calculated. A project takes no payment when it is created: a down payment is recorded as a payment afterwards.
- **TVA:** `tvaRate` (0 to 1, needs `financial:view`) on client work; left out, it is 15% (see [billing.md](billing.md#tva)). Its first labour defaults can be set too (`labourDefaults: { workers, dailyPay }`, needs `labour_report:manage`; see [execution.md](execution.md)).
- The client must belong to the project's domain (`400`) and not be archived (`422 client_archived`).
- **Owner:** an admin of the project's domain who isn't deactivated, or a super admin; never an employee (`422 owner_not_allowed`). The rule applies when a project is created and when its owner changes.

## Money

- **Design revenue** is the agreed payment, typed in. **Execution revenue** is the agreed amount: the project fee (optional, 0 when there is none) plus the client prices of the materials **sent to the client** on a statement. A material counts from the first time a statement listing it is sent (emailed, or a WhatsApp send or a download recorded: any delivery); until then its cost counts but its price doesn't (`money.unsentMaterialCharge` and `money.unsentMaterials` say what is waiting). A material's client price is its manual price, otherwise cost × (1 + markup), rounded to cents, then converted to USD at the exchange rate in force on the day the material was added; once stated, it is frozen. Ground labour is never billed.
- The Execution agreed amount is calculated live while the project is open, and frozen into `projects.revenue` when it is finalised. Finalising is refused while a material has no exchange rate for its day (`422 exchange_rate_missing`), or while one hasn't been sent to the client (`422 materials_not_sent`): send a statement first, so nothing is left out of what the client owes.
- While a project is finalised (`money.isLocked`), billing, revenue, fee and markup can't change (`422 project_locked`).
- **Payment changes** (`project_payment_changes`): every change of a client project's agreed payment is kept with why it changed, for the project report. Never edited or deleted.
  - Each row has `amount` (Design's customer payment, Execution's project fee, USD), `previousAmount`, for Execution `materialMarkup` and `previousMaterialMarkup` (fractions), `description` (required, at most 500 characters), `changedAt` and `changedBy` (`{ id, name }`, null once the person is gone).
  - The first row is the payment agreed at the start (`previousAmount` null, "Agreed at the start"), written when a client project is created. Projects from before this history got one with the payment they had then.
  - `PUT …/payment` and `PUT …/pricing` must change an amount: the same payment again is `400` (`revenue` or `projectFee`). An internal project has no payment (`400 revenue`: make it client work first). An Execution site's payment is its fee and markup, so `PUT …/payment` on one is `422 not_for_this_domain`.
  - Turning a client project into internal work writes a row dropping the payment to 0 ("Turned into internal work"). Becoming client work again starts at no payment, set with `PUT …/payment`.
  - The money activity events (`revenue_set`, `revenue_changed`, `project_fee_set`, `material_markup_set`) are still written, as expected revenue is built from them, but never carry the description: the activity feed shows its details to people without `financial:view`.
- A project with any invoice or payment can't move to another client or become internal (`422 project_has_billing`), because invoices and payments keep the client they were made for.
- `money.billing` is the project's cash position in USD, kept apart from revenue: `invoiced`, `outstanding`, `received`, `onAccount`, `notYetInvoiced` (revenue − invoiced − on account), `leftToInvoice` (revenue − invoiced), `owed`, `paidAhead`, `issuedCount`, `paymentCount`, `overdue`, `overdueCount`, `oldestOverdueDays`, `drafts` and `unconvertedInvoices[]` (invoices with no rate on their issue day, or drafts with none today, left out of the USD figures). It comes from the same billing rule and batch load as the lists and the Financials page (`Business/Billing/ProjectBilling.cs`; see [billing.md](billing.md) and [finance.md](finance.md)).
- Cost, profit, margin and the projections come from the shared finance calculator (below).

## Cost, profit and margin

Every project money figure comes from one place: `Business/Finance/ProjectFinance.cs`, loaded by `IProjectFinanceService` with one query per cost source however many projects are asked for. Domain features add their costs to its sources; they never calculate their own.

- **Cost** (`money.cost`) = employee labour + expenses + materials + ground labour, in USD.
  - **Labour:** each work entry's hours × the person's hourly cost in force on the day it was worked (company time zone). Monthly pay costs the salary ÷ that month's paid hours on the person's schedule, so a full-time month's labour adds up to the salary; the overtime premium is not part of project cost. **The paid break** is labour cost too, for everyone with a pay record, hourly and salaried alike (7 required hours are 8 paid hours): on each working day with some in-hours work, the day's paid break (none on days of 6 hours or less), at that day's rate, shared across the day's projects by the in-hours minutes worked on each (`Business/Finance/PaidBreaks.cs`). Project pay (see [company.md](company.md)) still leaves breaks out; it is a separate report of what each project paid for the hours worked on it. Time before someone's first pay record uses their earliest rate; time of someone with no pay record can't be priced and is counted in `unpricedLabourMinutes`. Hours and breaks in a month the person was an admin or super admin on any day cost nothing: that month is a fixed salary, a company cost (see [company.md](company.md)). A promotion therefore frees its whole month, but never changes the labour of the months before it (`ProjectFinance.RatesOf`).
  - **Expenses and materials:** converted to USD at the company exchange rate in force on their day (`spentOn`, the day a material was added). An amount with no rate that day is left out and listed in `cost.unconverted`, in its own currency.
  - **Ground labour:** workers × daily pay, in USD.
- **Profit** = revenue − cost. Internal projects have no revenue.
- **Margin** = profit ÷ revenue, only once it means something: the project is completed or archived, at least 5% along, or has at least 10 hours logged. Otherwise `null`.
- **Projected** cost, profit and margin = cost ÷ progress, for open client projects at least 5% along.
- **Recognised to date** = revenue × progress, or all of it once completed or archived.
- **Execution:** `cashProfit` = received − cost, and `materialProfit` = what the materials sent to the client earn (their charge − their cost).
- **Cash by week** (`ProjectFinance.CashByWeek`): payments received and cost added up week by week, Monday to Sunday. Execution sites show it on `GET /api/projects/{id}/site-money/timeline` (see [execution.md](execution.md)).

## Status

`Business/Projects/ProjectStatusRules.cs`:

| From | Allowed |
|---|---|
| active | hold, complete, archive |
| hold | resume, complete, archive |
| completed | archive, reopen |
| archived | reopen |

Anything else is `422 invalid_status_change`, and asking for the current status is `422 status_unchanged`.

- **Auto-archive:** while a domain's `autoArchive` setting is on, the scheduler (every hour) archives its completed projects when the completion, the latest activity event (invoices, payments, edits…) and the latest client portal message or (not deleted) comment are all at least 90 days old. It is a system event: `project_archived` with no actor and `automatic: true` in the payload, and no notification.
- **Finalising** happens when a project is completed, or archived without being completed first. Revenue is locked (`revenue_locked_at`, `finalized_at`, `completed_at`), the Execution agreed amount is frozen into `projects.revenue`, and every open requirement is ticked as done with `auto_done` set.
- **Reopening** clears those dates, returns Execution revenue to being calculated live, and reopens exactly the `auto_done` requirements.
- Archiving a completed project keeps its finalisation. Resuming from hold changes nothing else.

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

`project_created`, `project_updated`, `project_owner_set`, `project_client_set`, `revenue_set`, `revenue_changed`, `project_fee_set`, `material_markup_set`, `project_hold`, `project_resumed`, `project_completed`, `project_archived`, `project_reopened`, `project_finalized` (the frozen revenue in `amount`), `progress_set`. Amounts go in the event's `amount` and `prev_amount` columns, never in the payload. The markup is a fraction (0.125 is 12.5%), so `material_markup_set` keeps it in the payload as `markup` and `prevMarkup` (as the prototype does), with no amount; the feed shows that payload only with `financial:view`.
