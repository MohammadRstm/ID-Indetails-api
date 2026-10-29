# Version 1 status (30 Sep 2026, updated after the decisions)

This page covers what the API does today, what is in progress, and what the prototype (`hourline`, the source of truth for business rules) still has that the API lacks. Decisions and open questions are in [business-rules-review.md](business-rules-review.md).

## Built

| Area | Docs | PRs |
|---|---|---|
| Sign-in with rotating refresh tokens, invitation-only sign-up, super-admin setup, password reset | [authentication.md](authentication.md) | #4, #25 |
| Permissions: role defaults, personal grants and revokes, delegation, domain separation | [authorization.md](authorization.md) | #6 |
| People, teams, pay records, employment periods (leaving and coming back) | [employees.md](employees.md) | #7, #23 |
| Clients, projects, requirements, expenses, status changes, project lists with filters, sorts and work | [clients.md](clients.md), [projects.md](projects.md) | #8, #16, #22 |
| Invoices, payments, deliveries, billing settings, exchange rates | [billing.md](billing.md) | #8, #9, #21 |
| Project cost, profit and margin; domain money summaries, list money, client rollups, money follow-ups | [finance.md](finance.md) | #10, #18, #21, #22 |
| Execution: pricing, materials, ground labour, client statements, site money | [execution.md](execution.md) | #5, #12 |
| Company: settings, working time, schedules, overhead, salaries, hours ledger, excuses, overview, project pay | [company.md](company.md) | #11, #13, #15, #23 |
| Design work: entries, comments, meetings, settings, reports, activity, timeline, overtime, closed months, dashboards | [design.md](design.md) | #14, #22, #24 |
| Self-service: my pay, hours, overtime, schedule, requirements, board layouts | [me.md](me.md) | #17, #22, #23, #24 |
| Notifications, scheduled checks, money insights, auto-archive | [notifications.md](notifications.md) | #19, #22 |
| Messaging, live updates, online status and last seen | [messaging.md](messaging.md) | #20 |
| Search | [search.md](search.md) | #22 |
| Uploads: message and work-entry attachments, profile photos, the company logo and sign-in branding | [files.md](files.md) | `MR-Files` |
| The prototype gaps: Design cost over time, client overview, list totals, CSV export, wider search, timeline entries, People columns, overtime preview, site teams | [projects.md](projects.md), [clients.md](clients.md), [design.md](design.md), [me.md](me.md) | `MR-Gaps` |
| PDFs of invoices, statements and project reports; emailing invoices and statements | [documents.md](documents.md) | `MR-Gaps` |
| Execution pocket money: contributions from sites, requests approved or denied (replaced the box) | [execution.md](execution.md) | #158 |
| TVA per project (net revenue, gross billing), Execution sites agreed at one amount, quotes | [billing.md](billing.md), [documents.md](documents.md) | #159 |
| Profit floor: changes that take a client project to zero profit are refused, forced or dismissed; profit warnings | [projects.md](projects.md#profit-floor), [notifications.md](notifications.md) | #160 |
| The phone app's API: 30-day mobile sessions, push devices, pushes and reminders, notification settings, duplicate-safe offline materials and labour weeks | [authentication.md](authentication.md#mobile-sessions), [notifications.md](notifications.md#pushes), [me.md](me.md#my-devices), [execution.md](execution.md) | `MR-Mobile-Api` |

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
  - Overtime loads each person's whole span of days, not only the days shown.
  - The money follow-ups, overdue requirements, a client's work and the draft-invoice check each load everything, then filter.
- **Unconverted money still missing in two places:**
  - the company monthly view drops unconverted overhead and costs;
  - the money follow-ups miss overdue invoices that have no exchange rate.
- **Rounding in the company monthly view:** it rounds the parts before subtracting them; the domain charts subtract the rounded total.
- **Viewing a super admin:** `/employees/{id}/work-summary` shows a super admin to anyone, but `/employees/{id}/overtime` only to callers who see every domain.
