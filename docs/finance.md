# Money summaries

A domain's money as the dashboards, the Financials page, the projects list and the client pages show it. Every figure comes from the shared finance calculator (`Business/Finance/ProjectFinance.cs`, loaded by `IProjectFinanceService`; see [projects.md](projects.md), "Cost, profit and margin") and from the billing rules (`Business/Billing`, see [billing.md](billing.md)). Nothing here calculates cost, profit or margin a second way.

- **Currency:** every amount is USD at the rate of its day. Anything with no rate that day is listed as unconverted, in its own currency, never dropped silently.
- **Overhead** is never charged to a domain: it only appears at company level ([company.md](company.md)).
- **Loading:** every endpoint loads its projects in one batch (one query per cost source, plus invoices, payments and exchange rates), never a query per project or client.

## Endpoints

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/finance/{domain}/summary?from=&to=` | `financial:view` | Revenue, cost, profit and margin over a period, with the billing position (dashboard money cards, Financials header) |
| `GET /api/finance/{domain}/monthly?months=12` | `financial:view` | Revenue against cost month by month, oldest first (1 to 36 months) |
| `GET /api/finance/{domain}/projects` | `financial:view` | Every project's all-time money, most profitable first, with totals (Financials projects tab, dashboard "profit by project") |
| `GET /api/finance/{domain}/billing?months=12` | `financial:view` | Billing and collections (Financials billing tab, dashboard "money owed" and "invoiced against collected") |
| `GET /api/finance/{domain}/clients` | `financial:view` | Every client with projects and their money, most revenue first (Financials clients tab, dashboard "top clients") |
| `GET /api/finance/{domain}/costs?from=&to=` | `financial:view` | Costs over a period: by source, labour by person, expenses by category, the biggest expenses (Financials costs tab) |
| `GET /api/dashboard/{domain}` | `report:view` | The dashboard's cards without money or hours: active projects, clients, projects by status, longest running |
| `GET /api/dashboard/{domain}/deadlines` | `report:view` | Upcoming deadlines: open requirements due in the next 14 days, overdue ones included |
| `GET /api/projects` | `project:view` | Each row has `money` for callers with `financial:view` |
| `GET /api/clients`, `GET /api/clients/{id}` | `client:view` | `money` (and on the detail, `projectMoney`) for callers with `financial:view` |

- `{domain}` is `design` or `execution`. A domain the caller can't see, or any other value, answers `404`.
- Without `financial:view` the finance endpoints answer `403`, and the `money` fields of the project and client responses are left out entirely (not zeroed).
- Periods are company-time-zone days, both inclusive. They default to this quarter so far and never run past today.

## Rules

- **Revenue in a period** is what the projects recognised then: each project's recognised revenue (revenue × progress, or all of it once done) is spread over the months of its costs (`Recognition.ByMonth`: labour months, or other cost months when there is no labour), and each month counts for the share of it the period covers, up to today. The company overview works the same way. The prototype counted whole calendar months instead.
- **Cost in a period** is every cost dated inside it, by source: labour (including everyone's paid breaks, hourly and salaried alike), expenses, materials and ground labour. All four are always returned, so the parts add up to the total (the prototype's Execution sub-lines left materials and ground labour out).
- **Margin** is profit ÷ revenue when there is revenue. For a project it only shows once the project is done, at least 5% along or has 10 hours logged (`marginKnown`). A **client** follows the same rule as a whole: done once every project is, progress = the share of their revenue recognised so far, hours across all their projects.
- **Billing** (`Business/Billing/ProjectBilling.cs`) is the one billing rule. The project page's `money.billing`, the project and client lists, the client pages and the Financials page all use it, through the same batch load (`MoneyRollup`):
  - invoiced counts issued, live invoices; outstanding is what is left to pay on them; drafts are counted apart;
  - received counts every live payment; payments on account count as received but never reduce outstanding;
  - an invoice is **overdue** while it is sent or partly paid and its due date is before today (company time zone);
  - not yet invoiced = the client projects' revenue − invoiced, never below zero;
  - invoices are converted at the rate of the day they were issued (drafts at today's); one with no rate is left out and listed in `unconvertedInvoices`.
- **Aging** puts every open invoice's balance in one of: not yet due, 1–30, 31–60, 61–90 and 90+ days past due.
- **Unconverted costs** are dated like every other cost: the summary and the costs tab list those dated inside the period (every source, materials included), and each month of `monthly` lists its own. The projects totals are all-time, like the rest of that tab.

- **Upcoming deadlines** are the open requirements of the domain's active and on-hold projects with a deadline up to 14 days from today, overdue ones included, soonest first (then oldest added). `total` and `overdue` count them all; `items` holds the first 8, each with its project, assignees and days left (negative once overdue). Both domains have it.

## Response notes

- **Summary:** `revenue`, `cost { labour, expenses, materials, groundLabour, total }`, `profit`, `margin`, `unconverted[]`, `unpricedLabourMinutes`, counts of client and internal projects, all-time client revenue and profit and internal cost, `stillToEarn` (client revenue not recognised yet), and `billing` (the position as of today, whatever the period).
- **Monthly:** each month's revenue, cost by source, profit and `unconverted[]` (costs of that month with no rate).
- **Billing:** `position` (value, invoiced, issued count, received, payment count, on account, outstanding, overdue, overdue count, days overdue of the oldest, not yet invoiced, drafts), `aging[]`, `monthly[]` (invoiced by issue month, collected by the month received), `byClient[]` (clients with invoices or drafts, most outstanding first), `byMethod[]`, `openInvoices[]` (soonest due first) and `unconvertedInvoices[]`.
- **Costs:** labour by person is the 10 people who cost most; the biggest expenses are the 12 largest in USD (unconverted ones last); `unconverted[]` lists the period's costs with no rate, by source and currency.
- **Project list `money`:** revenue, recognised to date, cost, profit, margin, `marginKnown`, invoiced and outstanding, plus `hasUnconverted` (a cost or invoice of the project has no rate, so the USD figures leave it out) and `unpricedLabourMinutes` (time of people with no pay record). Sorting and filtering by money isn't offered on this paged list; the Financials projects table has every project.
- **Client `money`:** projects (active, completed), hours, revenue, recognised, cost, profit, margin, active and completed revenue, the margin of completed work, billing and `unconvertedInvoices[]`. The detail's `projectMoney` lists each project the same way, with the same `hasUnconverted` and `unpricedLabourMinutes` as the project list.
