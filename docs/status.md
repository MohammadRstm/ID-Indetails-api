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

The tests: 342 unit tests and 272 integration tests against real MySQL. The integration run takes about 7 minutes ([local-development.md](local-development.md#tests)).

## In progress

**File uploads** (branch `MR-Files`, not merged or built yet).
- Written:
  - uploads stored on local disk;
  - type checks from the file's content;
  - photos up to 10 MB and videos up to 100 MB;
  - downloads checked against what the file is attached to;
  - message and work-entry attachments;
  - a cleanup every 6 hours of uploads left unattached for a day.
- Still to do:
  - profile photos;
  - the company logo and the public branding for the sign-in page;
  - photo ids on people's responses;
  - docs and the remaining tests.

## Not built yet

These are compared with the prototype. None needs a schema change unless it says so.

### Projects and money

- **A Design project's cost over time.** Weekly cost is needed for the "payment vs cumulative cost" and "cost composition" charts and for the project report (`56-project-finance.js`, `55-project.js`, `58-report.js`). Today only Execution sites get it (`/site-money/timeline`).
- **The activity feed's Money group** leaves out Execution money: materials, labour weeks, labour defaults and statements (`10-data.js` activity groups).
- **Client list header figures and growth chart:**
  - counts: with active work, without projects, added this month, newest;
  - total revenue, profit and margin, and the largest client's share;
  - the 13-month growth series (`12-clients.js`, `68-clients.js`).
- **Wider search and filters:**
  - projects by description;
  - clients by note, contact email and phone;
  - archived clients listed alongside active ones;
  - an "internal (no client)" filter on activity and reports;
  - a client filter on `/reports/work`.
- **Totals for the filtered project list**: revenue, cost, profit and internal cost (`50-admin.js`). Sorting and filtering by money stay out (M7).
- **Project page details**: when a project went on hold, and who created an invoice.
- **Financials CSV export** (`65-financials.js`).
- **Smaller items:**
  - the overview names people who aren't logging, not only their count;
  - a duplicate-client check before saving.

### People and work

- **An Execution site's "Team"** on its cards: with no project members (O12), it would be the people who recorded materials or labour there.
- **The Timeline tab's lanes and one-day view**: these need individual entries with each person's team, not day totals (`59-timeline-*.js`).
- **People page**:
  - the rate column for money viewers;
  - hours per team over the last 30 days;
  - the "Overtime · this month" column.
- **Project icon and tint** on My work, my projects, meetings and profile tables. Person ids on team activity.
- **The log form's overtime preview**:
  - `/me/schedule?date=` gives the day's totals only;
  - showing the entry being drafted (before hours, after hours, past 7 h) would need a preview endpoint, so the client doesn't copy the overtime rule.
- **The search palette with nothing typed.**

### Documents and delivery

- **PDFs** of invoices, statements and project reports, made on the server (O3). The library is open question 3.
- **Emailing invoices and statements.** Deliveries are recorded, but nothing is sent.

### Outside the API

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
