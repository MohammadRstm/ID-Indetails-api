# Version 1 status (30 Sep 2026, updated after the decisions)

This page covers what the API does today, what is in progress, and what the prototype (`hourline`, the source of truth for business rules) still has that the API lacks. Decisions and open questions are in [business-rules-review.md](business-rules-review.md).

## Built

| Area | Docs | PRs |
|---|---|---|
| Sign-in with rotating refresh tokens, invitation-only sign-up, super-admin setup, password reset | [authentication.md](authentication.md) | #4, #25 |
| Permissions: role defaults, personal grants and revokes, delegation, domain separation | [authorization.md](authorization.md) | #6 |
| People, teams, pay records, employment periods (leaving and coming back) | [employees.md](employees.md) | #7, #23 |
| Clients, projects, requirements, expenses, status changes, project lists with filters, sorts and work; a Design project's departments with their planned hours and progress by hours (`PUT /api/projects/{id}/departments/plan`) | [clients.md](clients.md), [projects.md](projects.md#planned-hours) | #8, #16, #22, `MR-Planned-Hours` |
| Invoices, payments (filed under account labels), deliveries, billing settings, exchange rates and their daily feed | [billing.md](billing.md) | #8, #9, #21, `MR-Design-Expense-Rate` |
| Project cost, profit and margin; domain money summaries, list money, client rollups, money follow-ups; Financial Intelligence: a project's profitability, its bridge and scenarios (`/api/finance/insights/projects/{id}`, with Proton's `get_project_financial_summary` and `simulate_project_financial_scenario`), a domain's project risks and spending trends (`/api/finance/insights/{domain}/risks` and `/expenses`, with Proton's `detect_project_financial_risks` and `analyze_expense_trends`), and Design's labour cost by month, project and department with its outside-scope share, never a person (`/api/finance/insights/design/labour`); a project's cost budget in versions, each line against its cost so far (`/api/projects/{id}/budget`, with Proton's `get_project_budget`) | [finance.md](finance.md#financial-intelligence), [projects.md](projects.md#cost-budget), [proton.md](proton.md) | #10, #18, #21, #22, `MR-Insights-Project`, `MR-Insights-Risks-Expenses`, `MR-Insights-Labour`, `MR-Budgets` |
| Execution: BOQs and variation orders, site expenses (cost-only; the old materials paths stay as a temporary alias for the phone), ground labour, site money, supervision taken and a site's account figures; executed quantities per agreed BOQ line and a site's progress by value (`/api/projects/{id}/boqs/progress`, with Proton's `get_boq_progress`) | [execution.md](execution.md), [execution.md](execution.md#progress-by-value) | #5, #12, `MR-Boq-Progress` |
| Company: settings, working time, schedules, overhead, salaries, hours ledger, excuses, overview, project pay | [company.md](company.md) | #11, #13, #15, #23 |
| Design work: entries, comments, meetings, settings, reports (with utilization, hours only: `/api/reports/utilization`), activity, timeline, overtime, closed months, dashboards | [design.md](design.md) | #14, #22, #24, `MR-Insights-Labour` |
| Self-service: hours, overtime, schedule, requirements, board layouts (employees don't see their pay) | [me.md](me.md) | #17, #22, #23, #24 |
| Time off: requests, approval with or without pay, the hour block, alerts, pay | [time-off.md](time-off.md) | `MR-Time-Off` |
| Notifications, scheduled checks, money insights, auto-archive | [notifications.md](notifications.md) | #19, #22 |
| Messaging, live updates, online status and last seen | [messaging.md](messaging.md) | #20 |
| Search | [search.md](search.md) | #22 |
| Uploads: message and work-entry attachments, profile photos, the company logo and sign-in branding | [files.md](files.md) | `MR-Files` |
| The prototype gaps: Design cost over time, client overview, list totals, CSV export, wider search, timeline entries, People columns, overtime preview, site teams | [projects.md](projects.md), [clients.md](clients.md), [design.md](design.md), [me.md](me.md) | `MR-Gaps` |
| PDFs of invoices, BOQs and project reports; emailing invoices and BOQs | [documents.md](documents.md) | `MR-Gaps` |
| Execution pocket money: contributions from sites, requests approved or denied (replaced the box) | [execution.md](execution.md) | #158 |
| TVA per project (net revenue, gross billing); quotes were removed (`DepartmentsBase`) | [billing.md](billing.md), [documents.md](documents.md) | #159 |
| Profit floor: changes that take a client project to zero profit are refused, forced or dismissed; profit warnings | [projects.md](projects.md#profit-floor), [notifications.md](notifications.md) | #160 |
| The phone app's API: 30-day mobile sessions, push devices, pushes and reminders, notification settings, duplicate-safe offline expenses and labour weeks | [authentication.md](authentication.md#mobile-sessions), [notifications.md](notifications.md#pushes), [me.md](me.md#my-devices), [execution.md](execution.md) | `MR-Mobile-Api` |
| Pushes to browsers and the installed app (Web Push), and the installed app's 30-day sessions | [notifications.md](notifications.md#browsers-web-push), [authentication.md](authentication.md#the-installed-app) | `MR-Web-Push` |

The tests: 369 unit tests and 297 integration tests against real MySQL. The integration run takes about 4 minutes ([local-development.md](local-development.md#tests)).

## Not built yet

Every gap found by comparing the API with the prototype on 30 Sep 2026 is built on `MR-Gaps`: Design cost over time, the client overview, list totals and the financials CSV, wider search and filters, the Timeline tab's entries, People page columns, the overtime preview, site teams, project looks, PDFs and emailing. What is left:

- **PDF layout.** The documents are a plain first version. The project report doesn't have the prototype's "Performance" charts yet ([documents.md](documents.md)).
- **The React client** isn't connected yet.
- **There is no CI pipeline.**

## Technical follow-ups

These are known and harmless at today's size. Revisit them as the data grows.

- **Loading too much:**
  - Paid breaks load every priced contributor's work over the whole period being priced, on project pages, lists and costs.
  - Project cost's overtime loads every priced contributor's whole months of work, with one excuses query per month (28 Oct 2026).
  - Overtime loads each person's whole span of days, not only the days shown.
  - The money follow-ups, overdue requirements, a client's work and the draft-invoice check each load everything, then filter.
- **Unconverted money still missing in two places:**
  - the company monthly view drops unconverted overhead and costs;
  - the money follow-ups miss overdue invoices that have no exchange rate.
- **Viewing a super admin:** `/employees/{id}/work-summary` shows a super admin to anyone, but `/employees/{id}/overtime` only to callers who see every domain.
