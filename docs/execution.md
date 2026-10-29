# Execution sites

What site staff and admins record on an Execution project, and how it is priced for the client. Execution projects themselves, their work price and supervision, and their cost, profit and margin are in [projects.md](projects.md); how the client is charged (work, supervision, TVA) is in [billing.md](billing.md#tva); how the work price is set, by BOQs, is in the section below. A project of a domain the caller can't see reads as `404`, and Design projects have none of this (`422 not_for_this_domain`). Meetings work on sites exactly as on Design projects (see [design.md](design.md#meetings)).

**A finalised project is locked.** Once a site is completed or archived, nobody (admins included) adds, changes or removes its site expenses until it is reopened (`422 project_locked`). Projects on hold stay open. This lock, and the rule that people change only what they recorded unless they hold the manage permission, are in `Business/Execution/SiteRules.cs`.

## Site expenses

Everything an Execution site spends, other than ground labour, is a site expense: materials bought, a contractor, equipment, fees. It is a **cost only**: no client price, no markup, nothing charged to the client (the price of the work is in the BOQs). It replaced the materials list, and for Execution it replaces the "project expense" too (see below).

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects/{id}/site-expenses?type=&supplier=&addedById=&category=&accountLabelId=&from=&to=&search=&page=1&pageSize=30` | `project:view` | Newest recorded first, with totals, types, suppliers and who added them |
| `POST /api/projects/{id}/site-expenses` | `material:create` | `{ items: [{ name, quantity, unit, unitCost, amount, supplier, note, currency, spentOn, category, accountLabelId, clientRef }] }`, 1–100 at a time. Answers the expenses in request order |
| `PUT /api/projects/{id}/site-expenses/{expenseId}` | `material:create` (own) or `material:manage` (anyone's) | Replaces the same fields (without `clientRef`) |
| `DELETE /api/projects/{id}/site-expenses/{expenseId}` | `material:create` (own) or `material:manage` (anyone's) | Soft delete (`deleted_at`) |

**The old `/materials` paths are a temporary alias for the phone app.** `GET`, `POST`, `PUT` and `DELETE` on `/api/projects/{id}/materials` (and `/{id}`) are served by the same controller (`SiteExpensesController`) with the same shapes, so the phone keeps recording until it moves to `/site-expenses` in its own PR; then delete the second `[Route]` on the controller. A stray `clientPrice` in a phone batch is ignored (unknown JSON properties are skipped), and `PUT …/client-price` is gone on both paths (`404`). The permissions did not change: they keep the codes `material:create` and `material:manage`.

Rules (`Business/SiteExpenses/SiteExpenseRules.cs`):

- An expense needs an **item** (`name`, "Item" on screen) and a **cost**: quantity × unit cost when both are given, otherwise the total (`amount`). Quantity (3 decimals), unit and unit cost (4 decimals) are optional. A total that doesn't match quantity × unit cost sets the unit cost to total ÷ quantity. `supplier` is "Supplier / payee" on screen.
- **`currency`** is `USD` (the default) or `LBP`, **`spentOn`** is the day the money was spent (default today in the company time zone; between 2000 and 2100; not necessarily the day it was recorded), and **`category`** is `materials` (the default), `contractor`, `equipment`, `fees` or `other`. A change that leaves out any of the three keeps what the expense has. An expense in LBP needs an exchange rate in force on its `spentOn` (`422 exchange_rate_missing`): without one the site's money couldn't convert it, so it would never reach profit or the profit floor. Its activity events carry the `currency` in their payload, since their amount is in the expense's own currency, and a person's site records (`/api/me/site-records`) count it in dollars at that day's rate.
- **`accountLabelId`** files it under one of the company's [account labels](company.md). It must exist (`400 validation_failed` naming `items[i].accountLabelId`), and an archived label can't be newly picked (`422 account_label_archived`); an expense keeps an archived label it already has. A change sets the label to what it says, so leaving it out clears it. The response shows `accountLabel: { id, name }` or `null`.
- People change and remove only what they recorded; `material:manage` changes anyone's (`403 forbidden`). `canEdit` on each row says whether the caller may.
- An expense is a plain cost: it can be changed or removed while the site is open. The response has no client price, price source or markup, and the list no charge or default markup. Everyone who can see the project sees the costs (site staff type them).
- **Totals.** Amounts in different currencies are never added together: each totals block (`matching` covers everything the filters match, `all` the whole project) has the row `count`, `cost` (the rows in USD) and `byCurrency` (one total per currency, USD included). A type's `cost` is likewise its USD rows.
- **Filters.** `from` and `to` are days **spent** (`spentOn`), `category` and `accountLabelId` are exact. Types and suppliers are grouped by their text trimmed, with single spaces, in lower case (`type` and `supplier` filters take these keys); a type is labelled as most recently typed. The list is ordered by when the expense was recorded, newest first.
- Events keep their stored names: `material_added`, `material_updated`, `material_removed` (cost in the `amount` columns, subject type `material`) with the item's name in the payload. Older rows `material_priced` and `material_markup_set` stay in the feed as history; nothing writes them any more.
- Adding expenses sends one `materials_added` notification (the count in the payload; it reads "Expenses added" on a phone) to every active person who holds `material:manage` and can see the domain (super admins included), except the person who added them.
- **Sent twice.** The phone app records expenses offline and may send a batch again when its answer was lost. Each item can carry `clientRef`, a UUID the app gives the expense once. If the caller already added an expense with that ref, it is returned in the item's place: nothing is added, and no event or notification is sent. A batch whose items all exist changes nothing, and answers even on a site finalised since; one with new items follows the usual rules for those. A ref belongs to the person who added it (two people may use the same one), appears once in a batch, and is never reused on another site (`400`). The website sends none. An expense the phone recorded offline gets the day it is sent as its `spentOn` unless the phone names one.
- **The profit floor** guards adding and changing them (stored kinds `material_add` and `material_update`, said as "expense" in the blocked change's summary): see [projects.md](projects.md#profit-floor).

Site expenses feed the shared finance calculator (`Business/Finance/ProjectFinance.cs`) as the cost source `site_expense`: a cost is converted at the rate of the day it was **spent**, and one with no rate that day is listed as unconverted (`ProjectCostResponse.siteExpenses`, `unconverted[].source = "site_expense"`).

**Project expenses are Design's.** `POST /api/projects/{id}/expenses` on an Execution project is refused with `422 expense_execution_uses_site_expenses`; Design project expenses are unchanged (see [projects.md](projects.md#expenses)). The migration moved an Execution site's existing project expenses into site expenses: label to item, amount, currency, day, note, who recorded them (the project's owner when no one is recorded) and when, `deleted_at` kept; the categories became `contractor` for contractor, `equipment` for assets and hardware, `fees` for services and software, and `other` for travel and anything else.

## Ground labour

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects/{id}/labour-weeks` | `project:view` | Every week from the project's first to the current one (or the one it was finalised in), newest first, reported or not, with the labour total |
| `GET /api/projects/{id}/labour-weeks/{weekStart}` | `project:view` | One week's days. A week not yet reported comes back filled in from the defaults, up to today (`isReported: false`). `version` is `"0"` until it is reported, then changes whenever it does |
| `PUT /api/projects/{id}/labour-weeks/{weekStart}` | `labour_report:create` (new or own weeks, on a site the caller oversees: otherwise `403 not_on_site_team`) or `labour_report:manage` (anyone's, any site) | `{ days: [{ date, workers, dailyPay }], note, expectedVersion }` submits the week, or replaces the days of one already reported. `expectedVersion` is optional |
| `GET /api/projects/{id}/labour-defaults` | `project:view` | The defaults in force today and every change |
| `POST /api/projects/{id}/labour-defaults` | `labour_report:manage` (admins) | `{ workers, dailyPay }`, applying from today |

Rules (`Business/Labour/LabourRules.cs`):

- A **site week** runs Monday to Saturday; `weekStart` is its Monday (`400` otherwise). Sunday is never a labour day. Each day is a different day of the week (`400 labour_day_outside_week`) and no later than today (`400 labour_day_in_future`).
- Weeks can be reported from the project's first week to the current one (`422 labour_week_out_of_range`). There is one report per project and week; submitting it again replaces its days and keeps who first submitted it (the event records who changed it).
- A day costs **workers × daily pay**, in USD (labour is recorded without a currency). Ground labour is never billed to the client; it feeds the shared finance calculator as ground-labour cost, dated by each day.
- **Defaults** (the crew size and day rate new weeks start from) are admin-only and dated: a change applies from today, and a second change the same day replaces the first. An unreported week is filled in from the defaults in force on each of its days; before the first dated default, the first one applies. With no defaults, days start at zero. Weeks already reported keep their own numbers. An Execution project can be created with its first defaults (`labourDefaults` on `POST /api/projects`, needs `labour_report:manage`).
- People change only the weeks they submitted; `labour_report:manage` changes anyone's (`403 forbidden`). `canEdit` says whether the caller may. Everyone who can see the project sees workers and day rates.
- Events: `labour_week_submitted` and `labour_week_updated` (the week's total, and the previous one, in the amount columns) and `labour_defaults_set` (the daily pay in the amount columns, the crew sizes in the payload).
- Submitting or updating a week sends a `labour_week_submitted` notification to every active person who holds `labour_report:manage` and can see the domain (super admins included), except the person who did it.
- **Sent again, or after a change** (`Business/Labour/LabourResubmission.cs`). The phone app submits weeks offline and sends the `version` it loaded as `expectedVersion`:

  | Situation | Answer |
  |---|---|
  | The stored days and note already equal the submission | `200` with the week: nothing saved, no event, no notification. The website gets this too, so an identical save no longer re-notifies the managers |
  | `expectedVersion` sent, the week changed since, and the days or note differ | `409 labour_week_changed`; the app shows both and lets the person resend against the new version or keep theirs |
  | Otherwise (the website sends no version) | Saved as before |

  The version is the time of the week's last change in milliseconds; each change is recorded at least a millisecond after the previous one, so it always moves.

## BOQs and variation orders

A site's price is built line by line in bills of quantities (BOQs), not typed in. There are three kinds (`kind`):

- **`internal`**: an admin's private working draft. Never numbered, sent or agreed, and it raises no events or notifications.
- **`client`**: the BOQ the client is sent, as a PDF, as often as needed. It stays a draft until a super admin agrees it. One per project can be agreed (`422 boq_already_agreed`); the other client drafts stay drafts.
- **`vo`**: a variation order, a later change of price. It needs the client BOQ agreed first (`422 boq_not_agreed_yet`, even for a VO made before a withdrawal) and has a required `reason`. It is the only kind that can lower the price: negative rates, or a lower total.

BOQs exist only on Execution client projects. A project the caller can't see reads as `404`; any other project is `422 boq_execution_only`. Writes need the project open (`422 project_locked` once it is finalised, like the rest of a site's records).

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects/{id}/boqs` | `boq:create`, `financial:view` or `project:pricing_manage` | `{ internal, client, vos, price }`, newest first. `internal` holds the caller's own drafts (all of them for `project:pricing_manage`). `client`, `vos` and `price` are empty / `null` without `financial:view` or `project:pricing_manage` |
| `POST /api/projects/{id}/boqs` | `boq:create` (internal) or `project:pricing_manage` (client, vo) | `{ kind, title?, reason?, fromBoqId? }`. `fromBoqId` starts it with all the lines of a BOQ the caller can read on the same project. Numbers a client BOQ or VO. `201`, the BOQ |
| `GET /api/boqs/{boqId}` | as the list | The BOQ with its lines, totals and deliveries |
| `PUT /api/boqs/{boqId}` | the author of an internal draft, or `project:pricing_manage` | Saves the whole document: `{ title, reason?, adjustmentType, adjustmentValue?, lines: [{ id?, code?, description, quantity, wasteRate, unit?, rate }], updatedAt? }`. Lines are replaced in the order given; a line with an `id` keeps it, one left out is removed. `updatedAt` is the BOQ's as the editor loaded it (the web editor always sends it): if someone saved since, it is `409 boq_changed` and nothing is saved, so an older copy can't undo their changes |
| `POST /api/boqs/{boqId}/copy-lines` | as the save | `{ fromBoqId, lineIds? }` appends lines (all, or the ones named, in the source's order) of another BOQ of the project |
| `POST /api/boqs/{boqId}/duplicate` | `boq:create` (internal) or `project:pricing_manage` | A new draft of the same kind with the title plus " (copy)", the reason, the adjustment and the lines, and a new number. `201` |
| `DELETE /api/boqs/{boqId}` | as the save | Drafts only (`204`). Soft delete: what was sent stays on record |
| `POST /api/boqs/{boqId}/agree` | `project:pricing_manage` | No body. Locks the BOQ and sets the work price |
| `POST /api/boqs/{boqId}/withdraw-agreement` | `project:pricing_manage` | `{ reason }`. Returns it to draft and takes it out of the work price |
| `GET /api/boqs/{boqId}/pdf` | `financial:view` or `project:pricing_manage` | A client BOQ or VO as a PDF (`bq-2026-001.pdf`, `vo-2026-001.pdf`). Downloading records nothing |
| `POST /api/boqs/{boqId}/email` | `project:pricing_manage` | `{ to, note? }` emails it to the client with its PDF attached and records an `email` delivery. If sending fails, nothing is recorded (`503 email_delivery_failed`) |
| `POST /api/boqs/{boqId}/deliveries` | `project:pricing_manage` | `{ channel: email \| whatsapp \| download, recipient?, note? }` records a send made outside Hourline. Repeatable |

Rules (`Business/Boqs/BoqRules.cs`, pure; the totals are recomputed from the lines on every save and never taken from the client):

- **A line** has a `code` (optional, e.g. "2.1.1"), a `description`, an exact `quantity`, a `wasteRate` (a fraction, default 0), a `unit` and a `rate`. `finalQuantity` = quantity × (1 + waste), rounded to 2 decimals; the line total is `finalQuantity` × rate, rounded to cents. A BOQ has at most 1,000 lines. A negative rate is allowed only on a VO (`422 boq_negative_rate`).
- **Adjustment** of the whole BOQ: `none`, `percent` (the value is a fraction from -0.99 to 1, e.g. -0.05) or `total` (the value is the new total, not negative except on a VO). `adjustmentUsd` = subtotal × percent, or value − subtotal; `totalUsd` = subtotal + adjustment.
- **Supervision, TVA and the client's total** are shown with each BOQ at the project's rates as they are now: `supervisionUsd` = total × supervision rate, `tvaUsd` = total × TVA rate (never on supervision), `clientTotalUsd` = total + supervision + TVA.
- **Rights.** Internal drafts: written by whoever holds `boq:create`; its author reads and edits their own, and holders of `project:pricing_manage` read, copy from and edit or delete any of them. Another author's draft is `404` to everyone else. Client BOQs and VOs: every write (create, save, copy lines into, duplicate, delete, email, record a delivery, agree, withdraw) needs `project:pricing_manage`; reading them, the price summary and the PDF needs `financial:view` or `project:pricing_manage` (`403` otherwise). Both permissions are super-admin defaults; a super admin can grant `boq:create` to an admin person by person.
- **Numbers** are `BQ-YYYY-NNN` (client) and `VO-YYYY-NNN`, one sequence per prefix and year, taken from `boq_sequences` under a row lock so a number is never handed out twice. Internal drafts have none. A duplicate takes the next number.
- **Agreed BOQs are locked** (`422 boq_locked`: no save, copy into, delete, or a second agree). Drafts stay editable after being sent; each delivery keeps the BOQ's total when it was sent.
- **Sending** (email or a recorded delivery) works on drafts and agreed BOQs, never on an internal draft (`422 boq_internal`) or an empty one (`422 boq_empty`). Agreeing an empty BOQ is `422 boq_empty` too.
- **The work price.** Agreeing or withdrawing recomputes `projects.project_fee` = the agreed client BOQ's total + the agreed VOs' totals (0 when nothing is agreed). It is the only way the price changes. It writes what a fee change always wrote: a `project_fee_set` event (new and previous price), and a payment-history row whose reason is the BOQ's number when agreeing, or the reason given when withdrawing. Withdrawing the client BOQ while VOs are agreed on it is `422 boq_has_agreed_vos`; withdrawing a BOQ that is not agreed is `422 boq_not_agreed`. Agreeing or withdrawing a VO that would take the price below nothing is refused (`422 boq_price_below_zero`). A site priced before BOQs existed keeps its typed-in work price (and the price summary shows it) until its first client BOQ is agreed, which replaces it. An internal draft's detail leaves out the site's rates and the client figures (`supervisionRate`, `supervisionUsd`, `tvaRate`, `tvaUsd`, `clientTotalUsd` are `null`) for a caller without `financial:view` or `project:pricing_manage`, and a withdrawal's reason is left out of the activity feed for whoever doesn't see money.
- **The profit floor** holds agreeing and withdrawing like any other change: a recompute that leaves the profit at zero or below is refused with `422 profit_floor` and a `blockedChangeId`, and the change is kept (kinds `boq_agree` and `boq_withdraw`) for someone who sees the money to force through or dismiss (see [finance.md](finance.md)). Only a lower price is held to it: a rise always passes, the first agreement included (the floor is off until a price is agreed), and withdrawing the whole price always needs forcing.
- **The PDF** shows the company header, the client and project, the number, date and title (a VO also its reason), then each line's code, description, final quantity, unit, rate and total, and the subtotal, adjustment (when there is one), total, supervision (x%), TVA (y%) and total to pay. It never shows the exact quantity or the waste. A draft is stamped DRAFT. See [documents.md](documents.md).
- Events (client BOQs and VOs only; the number and kind in the payload, the total in the `amount` column): `boq_created`, `boq_sent` (and the channel), `boq_agreed`, `boq_agreement_withdrawn` (and the reason). There are no notifications for BOQs: the client portal does not show them yet, so they reach the client as a PDF. Proton has no BOQ actions: every BOQ write endpoint is a `Never` entry in `ProtonCoverage`.

Client statements, which listed materials supplied and froze their prices, are gone: nothing is in production, so their tables went with no data migration. A site expense is a plain cost.

## Site staff and daily logs

Site staff are Execution employees. They log no hours (they are paid monthly: an hourly pay is refused with `400 fixed_salary_paid_monthly`), record no site expenses by default (`material:create` is no longer an employee default; admins record them, or someone given the permission), and on the sites they oversee they report ground labour and write daily logs.

**Who oversees a site** (`project_staff`):
- `POST /api/projects` for an Execution site requires `staffIds`: at least one, each an active Execution employee, each once (`400 staffIds`). A Design project takes none.
- `PUT /api/projects/{id}/staff` (`project:edit`) `{ staffIds }` replaces the list (at least one). Someone taken off keeps their row with `removedAt`, so their past records still say who they were; put back, the same row is used again.
- The project shows them as `staff` (everyone, in the order they were put on) and as its `team` (the first 5 and the count), Execution only.
- Sites that existed before got, as their staff, the Execution employees who had recorded their expenses or labour.

**Daily logs** (`site_logs`): one thing noted on a day ("the concrete arrived with 3 bags missing"). A day can have many, each its own; the writer changes one only on the company day it was written (`422 site_log_locked`), and none is deleted: losses are analysed from them later.

| Method and path | Needs | Purpose |
|---|---|---|
| `POST /api/projects/{id}/site-logs` | `site_log:create`, on a site the caller oversees (`403 not_on_site_team`), while it is open | `{ body }` (1 to 2000 characters) |
| `PUT /api/site-logs/{id}` | `site_log:create`, the writer, on the day | `{ body }`. Someone else's log reads as `404` |
| `GET /api/me/site-logs?projectId=&from=&to=&search=&page=&pageSize=` | `site_log:create` | The caller's logs, newest first. Days are company days, both inclusive; `search` matches the log or the site's title |
| `GET /api/site-logs?userId=&projectId=&from=&to=&search=&page=&pageSize=` | `site_log:view_all` (Execution admins) | Everyone's logs on sites the caller can see |
| `GET /api/me/sites` | `site_log:create` or `labour_report:create` | The sites the caller oversees, newest assignment first |

Each log is `{ id, project: { id, title }, person: { id, name }, body, createdAt, updatedAt, canEdit }`.

## Site staff views

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects/site-summary?ids=1&ids=2` | `project:view` | One card per site among the ids (1–100, typically a page of the project list): expense count and cost (the JSON keeps `materialCount` and `materialCost`), reported weeks, ground labour, and when the caller last recorded something there. Projects the caller can't see and Design projects are left out |
| `GET /api/me/site-records` | `material:create`, `material:manage`, `labour_report:create` or `labour_report:manage` | What the caller recorded |
| `GET /api/employees/{userId}/site-records` | `employee:view` | What someone recorded. People of a domain the caller can't see read as `404` |

- **Cards:** site expense cost and ground labour come from the shared finance calculator (in USD).
- **Records** (`Business/Execution/SiteRecords.cs`): the site expenses a person added (kept as `materials` in the JSON, dated by when they were added, not by the day spent) and the labour weeks they submitted (dated by when they were submitted, valued at the week's total), on sites the caller can see. The response has the totals, this calendar quarter's value, the days with anything recorded, the last 16 weeks (oldest first, ending with the current one), a row per site (most recently recorded first) and the 20 latest records.

## Site money

A site's money (received, cost by source, cash profit, expected profit as `profit`, owed or paid ahead) is on `GET /api/projects/{id}` for callers with `financial:view`, from the shared finance calculator. Over time:

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects/{id}/site-money/timeline` | `financial:view` | Cash week by week, and when profit would reach zero |

- **Weeks** run Monday to Sunday, from the project's first week to the current one (or the one it was finalised in). Each has what was received, spent and paid in ground labour up to its end, the profit so far (received − spent), and what that week added in payments and ground labour. They come from the finance calculator (`ProjectFinance.CashByWeek`): costs on their day at that day's rate, payments by the day received.
- **Projection** (`LabourRules.ProjectZero`): at today's labour defaults (workers × daily pay a working day), how many working days (Monday to Saturday, from tomorrow) until cash profit reaches zero, and until expected profit would, once the client has paid everything agreed. `state` is `done` for a finalised site, `losing` when cash profit is already below zero, `no_defaults` without a crew and day rate, otherwise `ok`. It assumes the crew, their pay and the money received stay as they are, and no new materials.

## Pocket money

Money super admins set aside from Execution sites, and pay back to sites that run short. It replaced the Execution
box, which pooled every client payment automatically; the box never reached production and is gone, migration and
all.

Its ledger is `pocket_entries`: one row per movement, never edited, signed from the pocket's side. Its balance is the
sum of the rows. A `contribution` is positive and a `payout` is negative. Sites ask for money in `pocket_requests`.

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/execution/pocket` | `financial:view`, Execution visible | Balance, totals in and out, and the requests waiting for a decision (each with what it suggests today) |
| `GET /api/execution/pocket/entries?kind=&projectId=&from=&to=&search=` | `financial:view` | Movements, newest first. `kind=contribution` is what came in and `kind=payout` is what went out. Days are company days, both inclusive. The search matches words of the site's title, the note or the maker's name. |
| `POST /api/execution/pocket/contributions` | `financial:view` | Moves `{ projectId, amount, note? }` from a site into the pocket |
| `POST /api/execution/pocket/requests` | `financial:view` | Asks for money for a site: `{ projectId, note? }` |
| `POST /api/execution/pocket/requests/{id}/approve` | `financial:view` | Pays `{ amount, note? }` to the site |
| `POST /api/execution/pocket/requests/{id}/deny` | `financial:view` | `{ note? }` |

- **A site's money** here is its profit on its agreed amount (the work price plus supervision). Pocket money changes
  a site's **profit**, never what the client owes:
  - a contribution lowers it and a payout raises it;
  - both show on the project as `money.toPocket` and `money.fromPocket`;
  - both count in its cash profit (received − cost − to the pocket + from it) and in its weekly cash on the day they moved.
- **In:**
  - Only by hand, for an Execution client site.
  - Never so much that the site's profit reaches zero (`422 pocket_would_empty_site`).
  - The site is locked while its profit is checked.
- **Out:**
  - A site asks for money (`POST .../requests`); the system will also raise requests when a site gets close to zero.
  - A change that takes a site to 10% of its agreed amount or less raises a request by itself, with no requester and the note "Close to zero profit", unless one is pending already (see [projects.md](projects.md), "Profit floor").
  - The request suggests what brings the site back to a 10% margin (`PocketRules.TargetMargin`); a pending request shows that suggestion as it stands today. Whoever sees Execution's money is notified (`pocket_requested`).
  - A site has at most one pending request: asking again returns it.
  - A super admin approves the amount they choose, at most the balance (`422 pocket_short`), or denies it. Either way it is decided once (`422 pocket_request_decided`).
  - The request row is locked, and so is the pocket (`SELECT … FOR UPDATE` on `company_settings`), so a request is paid once and two payouts never spend the same balance.
