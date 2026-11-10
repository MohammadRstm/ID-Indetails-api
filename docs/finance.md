# Money summaries

A domain's money as the dashboards, the Financials page, the projects list and the client pages show it. Every figure comes from the shared finance calculator (`Business/Finance/ProjectFinance.cs`, loaded by `IProjectFinanceService`; see [projects.md](projects.md), "Cost, profit and margin") and from the billing rules (`Business/Billing`, see [billing.md](billing.md)). Nothing here calculates cost, profit or margin a second way.

- **Currency:** every amount is USD at the rate of its day. Anything with no rate that day is listed as unconverted, in its own currency, never dropped silently.
- **Overhead** is never charged to a domain: it only appears at company level ([company.md](company.md)).
- **Loading:** every endpoint loads its projects in one batch (one query per cost source, plus invoices, payments and exchange rates), never a query per project or client.
- **One load per page:** the dashboard and the Financials page take their money from `overview`, which loads the domain once for all six parts. The separate endpoints load it each time, so a page asking for several of them loads it several times.

## Endpoints

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/finance/{domain}/overview?from=&to=&months=12` | `financial:view` | From one load: `summary` over the period, `monthly` and `billing` over the months, `projects`, `attention` and `clients`, each the same as its own endpoint (the dashboard and the Financials page) |
| `GET /api/finance/{domain}/summary?from=&to=` | `financial:view` | Revenue, cost, profit and margin over a period, with the billing position (dashboard money cards, Financials header) |
| `GET /api/finance/{domain}/monthly?months=12` | `financial:view` | Revenue against cost month by month, oldest first (1 to 36 months) |
| `GET /api/finance/{domain}/projects` | `financial:view` | Every project's all-time money, most profitable first, with totals (Financials projects tab, dashboard "profit by project") |
| `GET /api/finance/{domain}/projects/export?from=&to=` | `financial:view` | The Financials export (UTF-8 CSV, export rate limit): every project's all-time revenue, hours, labour, expenses, site expenses (the column is headed "Site expenses"), ground labour, cost, profit and margin, then the period's recognised revenue, cost, profit and margin |
| `GET /api/finance/{domain}/billing?months=12` | `financial:view` | Billing and collections (Financials billing tab, dashboard "money owed" and "invoiced against collected") |
| `GET /api/finance/{domain}/clients` | `financial:view` | Every client with projects and their money, most revenue first (Financials clients tab, dashboard "top clients") |
| `GET /api/finance/{domain}/costs?from=&to=` | `financial:view` | Costs over a period: by source, labour by person, expenses by category, the biggest expenses (Financials costs tab) |
| `GET /api/finance/{domain}/attention` | `financial:view` | The dashboard's money follow-ups, 6 at most |
| `GET /api/finance/insights/projects/{id}` | `financial:view` | One project's profitability, all time, with where its profit comes from (see [Financial Intelligence](#financial-intelligence)) |
| `GET /api/finance/insights/projects/{id}/scenario?…` | `financial:view` | One project's figures today and with simulated changes; nothing is saved (see [Scenarios](#scenarios)) |
| `GET /api/dashboard/{domain}` | `report:view` | The dashboard's cards without money or hours: active projects, clients, projects by status, longest running |
| `GET /api/dashboard/{domain}/deadlines` | `report:view` | Upcoming deadlines: open requirements due in the next 14 days, overdue ones included |
| `GET /api/projects` | `project:view` | Each row has `money` for callers with `financial:view` |
| `GET /api/clients`, `GET /api/clients/{id}` | `client:view` | `money` (and on the detail, `projectMoney`) for callers with `financial:view` |

- An Execution site's account (what the client owes and has paid, costs by label, category and payee, supervision taken and the cash that should be in the project account) is `GET /api/projects/{id}/account-figures` (`financial:view`; see [execution.md](execution.md#account-figures)). It takes its costs, profit, price and pocket moves from this calculator rather than working them out again, so its figures equal the project's own `money`.
- `{domain}` is `design` or `execution`. A domain the caller can't see, or any other value, answers `404`.
- Without `financial:view` the finance endpoints answer `403`, and the `money` fields of the project and client responses are left out entirely (not zeroed).
- Periods are company-time-zone days, both inclusive. They default to this quarter so far and never run past today.

## Rules

- **Revenue in a period** is the money received in it, net of TVA: the projects' live payments less each one's TVA share, each counted on the day it arrived (`PeriodMoney.Received`). The TVA itself is `tvaCollected` (see [billing.md](billing.md#tva)). It moves as soon as a payment is recorded, whatever a project's progress; completing a project changes nothing but stops its amounts changing. The company overview works the same way.
- **Expected revenue in a period** (`expectedRevenue`, `Business/Finance/ExpectedRevenue.cs`) is what was agreed in it, kept apart from what was received:
  - a client project's payment (Design) or fee (Execution) counts on the day it was set, and a change counts its difference on the day it changed (from the `revenue_set`, `revenue_changed` and `project_fee_set` events);
  - an Execution site's work price counts on the day a BOQ is agreed, and a variation order or a withdrawal counts its difference (the `project_fee_set` events), with the site's supervision on top at the rate it has now.
  The monthly figures carry it too, for charts. The company overview shows no expected revenue: only what was received, less costs.
- **Supervision taken** (the company withdrawing some of a site's supervision fee, [execution.md](execution.md#supervision-taken)) is not a cost and is not read here: it changes no revenue, cost, profit or margin, only the cash the account figures say is left.
- **Cost in a period** is every cost dated inside it, by source: labour (including everyone's paid breaks, hourly and salaried alike), expenses (Design's project expenses), site expenses (Execution's, dated by the day they were **spent**, `spentOn`) and ground labour. All four are always returned, so the parts add up to the total (the prototype's Execution sub-lines left materials and ground labour out). The period and company responses keep naming the site expense part `materials` in the JSON; a project's own cost response calls it `siteExpenses`.
- **Profit in a period** (`PeriodMoney.Of`) is revenue less the cost total rounded once to the cent, never less parts rounded one by one, so the summary, each month, each project's period, Proton and the company overview give the same cent for the same days. Pocket money plays no part in it: the site cash profit, all time and with the pocket's moves, is a project figure ([projects.md](projects.md), "Cost, profit and margin"). Proton names the two apart: `period_cash_profit` and `site_cash_profit`.
- **Margin** is profit ÷ revenue, to 4 decimal places, when there is revenue: one rule for projects and periods (`ProjectFinance.MarginOf`). For a project it only shows once the project is done, at least 5% along or has 10 hours logged (`marginKnown`). A **client** follows the same rule as a whole: done once every project is, progress = the share of their revenue recognised so far, hours across all their projects.
- **Billing** (`Business/Billing/ProjectBilling.cs`) is the one billing rule. The project page's `money.billing`, the project and client lists, the client pages, the Financials page and the company overview all use it, through the same batch load (`MoneyRollup`):
  - invoiced counts issued, live invoices; outstanding is what is left to pay on them; drafts are counted apart;
  - received counts every live payment; payments on account count as received but never reduce outstanding;
  - an invoice is **overdue** while it is sent or partly paid and its due date is before today (company time zone);
  - not yet invoiced = the client projects' revenue − invoiced − what is held on account, never below zero (the project page also has `leftToInvoice` = revenue − invoiced, what a new invoice can still be for);
  - invoices are converted at the rate of the day they were issued (drafts at today's); one with no rate is left out and listed in `unconvertedInvoices`.
- **Aging** puts every open invoice's balance in one of: not yet due, 1–30, 31–60, 61–90 and 90+ days past due.
- **Unconverted costs** are dated like every other cost: the summary and the costs tab list those dated inside the period (every source, site expenses included), and each month of `monthly` lists its own. The projects totals are all-time, like the rest of that tab.

- **Money follow-ups** (`Business/Finance/MoneyAttention.cs`, as in the prototype), 6 at most, in this order:
  - `overdue_invoice`: each overdue invoice, oldest due first, with its number, days overdue and balance;
  - `finished_owed`: completed or archived projects still owed on their invoices, when none of it is overdue (auto-archive doesn't wait for payment, so this keeps them in sight);
  - `no_payment`: active client projects with no payment (revenue) agreed;
  - `cost_high` (Design only): active client projects whose cost has passed 85% of the payment, with the cost, the payment and the whole percentage used. An Execution site's profit is watched by the profit floor instead.
  The payment is the agreed revenue (Execution: the agreed amount), not what was received. Projects come oldest first.
- **Upcoming deadlines** are the open requirements of the domain's active and on-hold projects with a deadline up to 14 days from today, overdue ones included, soonest first (then oldest added). `total` and `overdue` count them all; `items` holds the first 8, each with its project, assignees and days left (negative once overdue). Both domains have it.

## Response notes

- **Summary:** `revenue` (received), `expectedRevenue`, `cost { labour, expenses, materials, groundLabour, total }` (`materials` is the site expenses), `profit`, `margin`, `unconverted[]`, `unpricedLabourMinutes`, counts of client and internal projects, all-time client revenue and profit and internal cost, `stillToEarn` (for each client project, its agreed revenue minus what was received, never below zero, summed), and `billing` (the position as of today, whatever the period).
- **Monthly:** each month's revenue, cost by source, profit and `unconverted[]` (costs of that month with no rate).
- **Billing:** `position` (value, invoiced, issued count, received, payment count, on account, outstanding, overdue, overdue count, days overdue of the oldest, not yet invoiced, drafts), `aging[]`, `monthly[]` (invoiced by issue month, collected by the month received), `byClient[]` (clients with invoices or drafts, most outstanding first), `byMethod[]`, `openInvoices[]` (soonest due first) and `unconvertedInvoices[]`.
- **Costs:** labour by person is the 10 people who cost most; the biggest expenses are the 12 largest in USD (unconverted ones last); `unconverted[]` lists the period's costs with no rate, by source and currency.
- **Project list `money`:** revenue, recognised to date, cost, profit, margin, `marginKnown`, invoiced and outstanding, plus `hasUnconverted` (a cost or invoice of the project has no rate, so the USD figures leave it out) and `unpricedLabourMinutes` (time of people with no pay record). Sorting and filtering by money isn't offered on this paged list; the Financials projects table has every project.
- **Client `money`:** projects (active, completed), hours, revenue, recognised, cost, profit, margin, active and completed revenue, the margin of completed work, billing and `unconvertedInvoices[]`. The detail's `projectMoney` lists each project the same way, with the same `hasUnconverted` and `unpricedLabourMinutes` as the project list.

## Financial Intelligence

Judgements and simulations on one project, kept apart from the Financials page's established figures (`Controllers/FinanceInsightsController.cs`, `Services/Insights`). Their rules live in `Business/Finance/Intelligence` (`ProjectBridgeRules`, `ProjectScenarioRules`): pure, with no EF and no HTTP. They build only on the shared calculator (`IProjectFinanceService`, `ProjectFinance`) and on the pricing rules the real changes use (`ExecutionPricing`, `Tva`, `ProfitFloorRules`). There is no second formula ([proton-financial-intelligence-plan.md](proton-financial-intelligence-plan.md), principle 1). Nothing is cached and nothing is saved.

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/finance/insights/projects/{id}` | `financial:view` | One project's profitability, all time: agreed revenue, cost by source, agreed profit, where it comes from (the bridge), its standing against the profit floor, its cash, and Design's departments |
| `GET /api/finance/insights/projects/{id}/scenario?voAmount=&extraCost=&extraCostSource=&supervisionRate=&tvaRate=&settleOwed=&delayWeeks=` | `financial:view` | The project's figures today and with the changes asked for. Nothing is saved |

- Without `financial:view` both answer `403`; the service checks it again.
- A project in a domain the caller can't see answers `404`, like one that doesn't exist.
- Both domains, client work and internal work.
- Amounts are USD, net of TVA unless said otherwise.

### A project's profitability

`GET /api/finance/insights/projects/{id}`:

- **The project:** `projectId`, `title`, `domain`, `status`, `billingType`.
- **Agreed money:**
  - `agreedRevenue`: Design's payment with its applied VOs; a site's work + supervision + permits. 0 for internal work. `agreedWithTva` is what the client is agreed to pay, TVA included (0 for internal work).
  - `cost`: the project's cost by source, as its own `money` shows it: `labour`, `expenses`, `siteExpenses`, `groundLabour`, `total`, `unconverted[]` and `unpricedLabourMinutes`.
  - `agreedProfit` = agreed revenue − cost + pocket money (paid by the pocket − moved to it), all time (owner decision Q1). Overhead, fixed salaries, paid leave and the overtime premium are company costs, never a project's.
  - `margin` = profit ÷ revenue (`ProjectFinance.MarginOf`). It shows only once the project is done, at least 5% along or has 10 hours logged (`marginKnown`); otherwise `null`.
  - `standing` against the profit floor (`ProfitFloorRules.StandingOf`): `fine`, `close` (a margin of 10% or less) or `at_zero` (profit at zero or below). `null` where the floor doesn't apply: internal work, or no agreed amount yet (Design's payment or a site's work price at 0).
  - `progressPct`, `recognisedToDate` (revenue × progress, all of it once done) and `projected` (`cost`, `profit`, `margin`): cost ÷ progress, a straight line, for open client projects at least 5% along; `null` otherwise. The same rules as [projects.md](projects.md), "Cost, profit and margin".
- **Cash:**
  - `received`: what the client paid, TVA included. `receivedTva` is the TVA in it.
  - `owed`: what the client still owes, TVA included: agreed with TVA − received, never below 0 (0 for internal work).
  - `siteCashProfit`: a site's received net of TVA − cost − moved to the pocket + paid by it, all time (Proton's `site_cash_profit`). `null` on Design.
  - `toPocket`, `fromPocket`: what was moved to the pocket money and what it paid in (0 on Design).
  - `supervisionRate` (a site's; `null` on Design) and `tvaRate`.
- **The bridge** (`bridge[]`, `ProjectBridgeRules`): where the agreed profit comes from, step by step. Each step is a `kind` and an `amount` in USD net of TVA: positive adds to the profit, negative takes from it.

  | `kind` | Amount |
  |---|---|
  | `base_price` | The price first agreed: Design's payment, a site's client BOQ. It is the remainder: the agreed revenue less every other price step below. Client work only |
  | `variation_orders` | Design: the applied VOs. A site: its agreed VOs, settlement and closing VOs left out |
  | `settlement` | What a settlement at completion took off the price (negative). Design: the net of what was settled (`settledOwedUsd`). A site: its "Excused at completion" and "Unpaid, written off at completion" VOs with the supervision they took off, so the step is all the settlement lowered the revenue by. On Design with untaxed VOs it is close, and `base_price` takes any difference |
  | `paid_ahead` | A site's "Paid beyond the agreed price" VO (or client BOQ, on a site with none agreed before), with its supervision. On Design an overpayment kept is folded into the payment, so it stays in `base_price` |
  | `supervision`, `permits` | A site's supervision on its base price and VOs (a settlement or a kept overpayment carries its own), and its permits |
  | `labour`, `expenses`, `site_expenses`, `ground_labour` | Each cost source (negative) |
  | `pocket` | What the pocket money paid in less what was moved to it |

  - The steps always add up to `agreedProfit`.
  - A step of 0 is left out, except `base_price` on client work. Internal work has only its costs and pocket money.
- **Departments** (`departments[]`, Design only; `null` on a site): from the departments service without its people, so no figure here sits beside a person. Each row has:
  - `id`, `name` and `sharePercent` (its part of the price, as a percentage);
  - `budget`, `cost`, `left` (below 0 when it is over its budget) and `minutes`;
  - `labourSharePercent`: its cost ÷ the project's labour cost, to 1 decimal; `null` while there is no labour cost;
  - `unconverted[]`: labour logged in it with no exchange rate on its day, per currency, left out of `cost` and `left`.

  Hours of people in no department, or in one no longer on the project, are a last row named "Other hours", with no `id` and no `sharePercent`.
- **`basis`:** `method` (the profit formula above), `assumptions` (the margin rule, and the projection as a straight line when there is one) and `leftOut`: costs with no exchange rate on their day (amount, currency and source), hours of people with no pay record, and for internal work that it has no revenue, margin or floor.

### Scenarios

`GET /api/finance/insights/projects/{id}/scenario` takes its changes (owner decision Q18) as query parameters. It is a GET and writes nothing, so it has no `ProtonCoverage` entry. At least one change is needed, and they can be combined.

| Parameter | What it simulates |
|---|---|
| `voAmount` | A variation order in USD, net of TVA; negative takes work out. A site: on the work price, with its supervision and TVA (`ExecutionPricing.PriceOf`). Design: on the price, with the project's TVA (`Tva.Gross`). Not 0, at most 100,000,000 either way |
| `extraCost` with `extraCostSource` | An extra cost in USD (above 0, at most 100,000,000) and where it goes: `labour` or `expenses` on Design, `site_expense` or `ground_labour` on a site |
| `supervisionRate` | A site's supervision rate, a fraction (0.10 is 10%), 0 to 1 with at most 4 decimals. Sites only |
| `tvaRate` | The project's TVA rate, a fraction, 0 to 1. TVA is never revenue, so it changes what the client pays and owes (`agreedWithTva`, `owed`), never revenue or profit |
| `settleOwed` | `excused` or `written_off`: what the client still owes settled, so the price is lowered to what was received, as completing does ([projects.md](projects.md#status)). A site's work price comes from `ExecutionPricing.WorkFor`, Design's payment from `Tva.NetFor`. Both give the same figures. It can't be combined with `voAmount` (the settlement sets the price, so the VO would change nothing), and it isn't held to the profit floor (Q10) |
| `delayWeeks` | Finishing 1 to 104 weeks later, at the project's average weekly cost over its last 4 full weeks (Monday to Sunday, before this one; fewer for a younger project, and 0 when there are none) |

- **`before` and `after`**, each with `agreedRevenue`, `agreedWithTva`, `cost`, `agreedProfit` (revenue − cost + pocket money), `margin` (profit ÷ revenue while there is revenue, without the `marginKnown` rule), `owed` (agreed with TVA − received, never below 0) and `standing` (as above; `null` where the floor doesn't apply).
- **`profitChange`** = the profit after − the profit before.
- **`floorWouldRefuse`:** as real changes, the ones other than a settlement would be refused by the profit floor (`422 profit_floor`) and kept as a blocked change. That is when the floor applies to the project today, and those changes take its profit to 0 or below and lower it. It is worked out without the settlement, which isn't held to the floor.
- **`weeklyCost`:** with `delayWeeks`, the weekly cost the delay was priced at; `null` otherwise.
- **`basis`:** the method, each change in words, and what was left out, as above. On a finished project the price is locked, and the basis says the changes show what they would have done.

**Errors:**

- `400 validation_failed`: no change asked for; an extra cost without its source, or a source without an amount; `voAmount` with `settleOwed`; a value out of its range.
- `422 scenario_not_applicable`: `voAmount`, `tvaRate`, `supervisionRate` or `settleOwed` on internal work, which has no price. Only an extra cost or a delay can be simulated there.
- `422 not_for_this_domain`: `supervisionRate` on Design, or a cost source of the other domain.
- `422 nothing_owed`: `settleOwed` when the client owes nothing.
- `422 owed_beyond_work`: `settleOwed` on a site whose client paid less than its permits alone.
