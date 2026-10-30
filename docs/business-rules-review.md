# Business rules from the prototype update (28 Sep 2026)

The Hourline prototype changed around 27 September: the Execution pricing model was replaced, and working schedules, an hours ledger and pay views were added. The schema was updated for it in the `ExecutionPricingAndWorkingTime` migration (see [database.md](database.md)).

This file records the business decisions taken at the same time, so they're applied when the API features are built. It also lists what is still open.

## Decided

| # | Rule | Where it is enforced |
|---|---|---|
| 1 | **Working time is dated.** The company working week, the break, the overtime multiplier and personal schedules only apply from their effective date. A change never rewrites past months' overtime, hours or pay. | Schema (dated tables) |
| 2 | **Excused time is paid** by default. Each excuse has an `is_paid` flag for unpaid leave. Leave types and a public-holiday calendar are deferred. | Schema; API / payroll calculation |
| 3 | **A finalised Execution project is locked.** Nobody, admins included, can add, edit or delete its site expenses or labour until it is reopened. *(The prototype still lets admins edit after finalise; the backend won't.)* | API |
| 4 | ~~**A stated material's price is frozen.**~~ *Superseded (Oct 2026): client statements were removed and a site's price comes from BOQs ([execution.md](execution.md#boqs-and-variation-orders)). A site expense is a plain cost.* | gone |
| 5 | **Execution has no internal projects.** Every site is for a client. | Schema (CHECK) |
| 6 | **The initial payment is flagged** with `payments.is_initial`, not recognised from its note text. At most one live initial payment per project. | Schema (generated column + unique index) |
| 7 | **Labour defaults are admin-only** (`labour_report:manage`), per the permission catalogue. *(The prototype lets employees change them; the backend won't.)* | API / permissions |
| 8 | **One company-wide working week**, editable by super admins only (`working_time:manage`). People with different hours get a personal schedule (`schedule:manage`, super admins). Excusing hours is also super-admin only (`hours:excuse`). | Permissions |
| 9 | **Working-time and schedule changes start today or later**, never in the past, so closed months never change. A change dated in the future may be replaced before it starts. | API |
| 10 | **A missed working day deducts the full paid day.** In a closed month, each whole working day with no in-hours work and no excuse deducts required hours plus the break (8h on a 9–5 day) from salaried pay. Any other shortfall deducts the missing hours. | Payroll calculation |
| 11 | **Made-up overtime** comes from all of the month's overtime, in date order (as in the prototype). | Payroll calculation |
| 12 | **Salaries are always calculated.** No month-close snapshot for now. | Payroll calculation |
| 13 | **Mid-month joiners are pro-rated on their own working days** (personal schedule, otherwise the company week), not a fixed Monday–Friday, by the paid hours of those days. A salary's hourly rate is worked out each month: the salary ÷ that month's paid hours, so the salary stays fixed (6 Oct 2026: no special rule for the 31st). Overtime is paid at the true hourly rate of a standard month (weekly paid hours × 52 ÷ 12), whatever the month. | Payroll calculation |
| 14 | **The hours ledger** applies to Design employees only, from the month they joined. Admins and Execution staff have none. | Payroll calculation |
| 15 | **The Salaries page lists everyone with a pay record**, admins included. Super admins appear only if they have pay. | API |
| 16 | **Company overhead:** amount changes are dated (past months keep their cost), totals are kept per currency, and "cancel" ends a line at the end of the current month. It can be resumed only before that date passes; after that, a new line is added. | API |
| 17 | ~~**A manual price equal to the automatic one is stored as automatic** (`client_price` NULL), so it keeps following the markup.~~ *Superseded (Oct 2026): materials became cost-only site expenses; client prices and markups were removed ([execution.md](execution.md#site-expenses)).* | gone |
| 18 | **No separate delivery date** for now: a site expense is dated by the day it was spent (`spent_on`, which defaults to the day it is recorded), labour by its day. *(Was open question 6.)* | Schema |
| 19 | **Unpaid excuses** are deducted from salaried pay: unpaid minutes at the hourly rate, an unpaid day off as the full paid day, an unpaid whole month as the whole salary. | Payroll calculation |
| 20 | **Hourly staff** are paid for what they log; excuses only clear their shortfall and never add pay. | Payroll calculation |
| 21 | **Admins and super admins are paid a fixed monthly salary** and log no hours (6 Oct 2026): no overtime, no hours ledger; a salary is required when inviting them. Both come off the company's net profit (7 Oct 2026; until then only super admins' did), and any hours they logged cost their projects nothing. Both go by month (23 Oct 2026): a month with any day as an admin or super admin is a fixed salary, and earlier months keep their own role. Until then everything went by the role today, so a promotion wiped the past labour cost of someone's projects and changed their past pay. | Payroll calculation |
| 22 | **Project pay leaves paid breaks out.** A break isn't worked on any project, so project pay covers logged hours and overtime only, and differs from Salaries for hourly staff by exactly their paid breaks. It needs `salary:view`. | Project pay report |
| 23 | **Leavers stay in past months** (29 Sep 2026). `users.deactivated_at` records when an account was deactivated (cleared on reactivation; backfilled from the latest `employee_deactivated` event). Salaries (list, detail, CSV, totals), the month-end alert and project pay include everyone active at any point in the month, with the final month pro-rated to the leaving day like a joining month, and the hours ledger ending there. | Schema (`UserDeactivatedAt` migration); payroll calculation |
| 24 | **Employment is dated periods** (30 Sep 2026, open questions 4 and 5). Leaving ends a period on the last working day the admin gives (today by default, never reaching back into a closed month), which is paid. Being taken back starts a new period, and the days away between two periods are neither paid nor expected. When the time away began earlier in the same month, the admin says whether it is paid: if so, the period simply continues and the days away are excused as paid. | Schema (`EmploymentPeriods` migration); API; payroll calculation |
| 25 | **The break is logged from 1 December 2026** (28 Oct 2026). The company week's break is 0 from that day, so the whole 8-hour day is required and logged: logging all of 9–5 is no longer an hour of overtime, and no paid break is added to hourly pay, missed-day deductions or project cost (M1). The rules are unchanged, since a day with no break already works this way, and November 2026 and earlier keep their paid 60-minute break (decision 1). | Company working-time setting |

## Decided for Design work (28 Sep 2026)

Taken when the Design work API was built (see [design.md](design.md)).

| # | Rule |
|---|---|
| D1 | **The timer runs in the client.** Stopping it logs an ordinary entry; nothing is stored while it runs. |
| D2 | **Nobody edits someone else's entries**, admins included. Whoever holds `work_entry:create` logs their own work: Design employees only (admins and super admins log no hours since 6 Oct 2026). |
| D3 | **Entry rules are enforced on the server.** An entry is locked once past the edit window (counted from when it was logged) or while its project no longer accepts work. 5 minutes to 16 hours, in whole minutes. With future entries allowed, any end time is accepted. |
| D4 | **A work entry can carry one photo or video** (`attachmentFileId`), an upload of its author's, readable by whoever may read the entry (30 Sep 2026, with file uploads; see [files.md](files.md)). |
| D5 | **Comments can't be edited or deleted.** Posting a comment to the team group chat is refused until messaging exists. |
| D6 | **Logged-work notifications** go to active people holding `work_entry:view_all` in Design, never the author. |
| D7 | **Domain settings aren't seeded.** A domain uses the defaults until its admins save. Each domain's admins (`domain_settings:manage`) edit their own row. Of the notification settings, `notifyEntry`, `notifyProject` (N4), `notifyIdle` and `weeklySummary` (N7) are used; `autoArchive` is used by the auto-archive job (A4). |
| D8 | **Money in the activity feed** shows without its amount to people without `financial:view`. Company-level events need `company:view`. |
| D9 | **Logging doesn't add project members.** |
| D10 | **Employees set meetings.** `meeting:create` (every role, `MeetingCreatePermission` migration) sets meetings and moves or removes your own; `meeting:manage` covers anyone's. Nobody moves someone else's meeting without `meeting:manage` (the prototype let anyone drag any meeting). Meetings were Design-only at first; since 29 Sep 2026 Execution sites have them too, as in the prototype, and both permissions apply in both domains (`MeetingsInBothDomains` migration). |
| D11 | **Past months never change** (29 Sep 2026). Work entries that start before the current company month can no longer be logged, edited, moved (the day timeline's swaps and moves included), deleted or restored, by anyone (`422 month_closed`). Excusing hours stays the way to correct a closed month. Known exception: a backdated pay change still changes a closed month's salary; it is out of scope for now. The month just ended stays open through the 1st, so an evening's work can still be logged after midnight (30 Sep 2026). |
| D12 | **Overtime on someone's profile** (29 Sep 2026). `GET /api/employees/{id}/overtime` (`work_entry:view_all`) shows their overtime like their own page does, but which of it made up missing hours is pay information: only callers with `salary:view` get it, as in the prototype where only super admins see it. Their hours ledger (`GET /api/employees/{id}/hours`) needs `salary:view` altogether. |

## Decided for the money summaries (29 Sep 2026)

See [finance.md](finance.md).

| # | Rule |
|---|---|
| M1 | **Everyone's paid break is labour cost**, hourly and salaried alike (7 required hours are 8 paid hours), on the days Salaries pays it, shared across the day's projects by in-hours minutes. Project pay (decision 22) still leaves breaks out. *(Changed on 29 Sep 2026: it was hourly staff only.)* There is no break to share from 1 December 2026, when the break is 0 (decision 25). |
| M2 | **A client's margin follows the project rule** as a whole: shown once all their projects are done, at least 5% of their revenue is recognised, or 10 hours are logged. *(The prototype showed it whenever there was revenue.)* |
| M3 | ~~Revenue in a period follows `Recognition`~~. **Replaced:** revenue in a period is the money received in it, counted on the day it arrived (the summaries, the Financials page, the company overview and its monthly chart). Overhead and admins' and super admins' salaries still count by the share of each month's calendar days the period covers. |
| M4 | **Every cost source is returned** on the dashboards and Financials, so the parts add up to the total. *(The prototype's Execution breakdowns left out materials and ground labour.)* |
| M5 | **The Execution dashboard has no hours cards**: key figures, money cards and projects by status only, as in the prototype. |
| M6 | **Without `financial:view`, invoice write responses hide what was paid**: no paid, balance or overdue figures, and paid or partly paid reads as sent. |
| M7 | **Money on the project list isn't sortable or filterable** on the server (the list is paged in the database); the Financials projects table has every project. |
| M8 | **One billing rule** (`Business/Billing/ProjectBilling`) for the project page, the project and client lists, the client pages and Financials. Invoices are converted at the rate of their issue day everywhere, so the project page and the lists always agree. |
| M9 | **Unconverted money is never dropped.** Costs with no rate are listed for the period they fall in (summary, each month, costs tab); list rows flag them (`hasUnconverted`); the company overview lists invoices and costs with no rate instead of counting them as 0. |
| M10 | ~~**Statement readers see the statement's money.**~~ *Superseded (Oct 2026) with the statements: client BOQs and VOs are read with `financial:view` or `project:pricing_manage`; holders of `boq:create` alone see only their own internal drafts.* |
| M11 | **The markup event keeps the exact markup** in its payload (`markup`, `prevMarkup`, as in the prototype), with no amount: the amount columns keep 2 decimals, so 12.5% was saved as 0.13. The feed shows that payload only with `financial:view`. Existing events were moved by the `MaterialMarkupEventPayload` migration: each project's latest one is exact, older ones keep their rounded value. *Since Oct 2026 markups are gone and nothing writes this event; the supervision rate event (`supervision_rate_set`) keeps its rates in its payload the same way.* |

## Decided for the admin views (29 Sep 2026)

| # | Rule |
|---|---|
| A1 | **Deadlines and money follow-ups cover both domains.** The prototype only shows the follow-ups and deadlines card on the Design dashboard, but its rules don't depend on the domain; the endpoints serve Design and Execution alike, and the client decides what to show. The cost warning (follow-up and notification) is Design only (30 Sep 2026): an Execution site's payment includes its materials at client price and its cost at what was paid, so its cost is past 85% from the first material. |
| A2 | **The project list keeps its newest-start default.** The prototype's default "recent activity" order is `sort=activity`; a project without work counts from when it was added (the prototype used its start date). A `year` alone filters projects open during that year (the prototype only filtered by quarter). |
| A3 | **The client list keeps its A-to-Z default** and sorts each column one way (numbers highest first, activity newest first); the prototype also let a second click reverse it. "Last activity" is the latest work on the client's projects, or when the client was added (the prototype's column sorted by the date added). |
| A4 | **Auto-archive needs 90 quiet days**: a completed project is archived only when its completion and its latest activity event are both 90 days old, so one still being billed stays out. *(The prototype only checked the completion date, and only when the setting was switched on.)* It runs hourly for each domain whose setting is on. |
| A5 | **Money insights are notifications to `financial:view` holders only**, with percentages and never amounts: the profit warnings (profit down to 10% of the agreed amount, and at zero or below, once per project ever; they replaced the 85% cost warning on 29 Oct 2026, see [projects.md](projects.md), "Profit floor"), the closing margin (when a client project is finalised) and the draft reminder (a draft still unissued 3 days after it was created; the prototype had no threshold). The prototype's monthly company-expenses notification is left out. |

## Decided for notifications (29 Sep 2026)

| # | Decision |
|---|---|
| N1 | **Assignees tick their own requirements.** `requirement:complete_own` (every role by default, `RequirementCompleteOwnPermission` migration) ticks a requirement assigned to you and unticks one you ticked yourself. The managers (`requirement:manage`) and the assignees hear about every tick. |
| N2 | **"The team's admin" is the domain's admins** for now: teams have no lead. |
| N3 | **Super admins get admin-level notifications** (work logged, materials, labour, clients, billing, ticked requirements) but not the broadcasts to a domain's people (new projects, status changes, added requirements). |
| N4 | **Added requirements roll into one notification** per person per project per day, and follow `notifyProject` like the other project news. |
| N5 | **No money in notifications.** Billing notifications name the invoice or project, never the amount. |
| N6 | **Live push** came with messaging: a `notifications` signal over the live connection tells the client to reload the bell (see C5). |
| N7 | **Scheduled notifications** run on an hourly timer and are keyed so each goes once: last week's summary, idle Design projects, requirements due tomorrow or overdue (deadlines up to a week old), overdue invoices, and the month-end short-hours alert. `notifyIdle` now also sends `project_idle`, and `weeklySummary` sends the summary. |
| N8 | **The month-end short-hours alert** goes to whoever holds `salary:view` (super admins by default), from the first of the month, naming the five people most short. This settles the old open question. |

## Decided for messaging (29 Sep 2026)

See [messaging.md](messaging.md).

| # | Decision |
|---|---|
| C1 | **Chats follow the organisation, not permissions.** A team chat is the team, its domain's admins and the super admins, worked out live; direct messages reach your own domain and the super admins. Someone granted a permission from the other domain doesn't join its chats. |
| C2 | **Moving someone moves their chats.** They lose the old team's chat, history included, and see the new team's whole history. |
| C3 | **No bell notifications for messages.** The messages badge (unread count and the "@" flag) covers them, as in the prototype. |
| C4 | **A team with chat history can't be deleted**, so history is never lost. |
| C5 | **Live updates** push small "something changed" signals over SignalR for messages and the bell, only once a request has succeeded; the client reloads what changed. One API instance for now: several would need a SignalR backplane. |

## Carried over from the prototype as-is

- **Execution money:**
  - The agreed amount is the work price from the agreed BOQs plus supervision (material client prices are gone).
  - Ground labour is never billed.
  - Invoicing is open from project creation.
  - Payments on account are normal.
  - Cash profit = received − spent.
- **Client BOQs and variation orders** (they replaced the statements in Oct 2026): numbered `BQ-YYYY-NNN` and `VO-YYYY-NNN`, one sequence per prefix and year. They set the work price; they are not invoices and owe nothing by themselves.
- **Labour weeks** run Monday to Saturday.
- **Site expenses** are grouped by their item name (trimmed, lower-case), and also carry a category (materials, contractor, equipment, fees, other) added with the cost-only rework in Oct 2026.
- **The break** is paid, reduces only the required hours, and applies on days longer than 6 hours. The 6-hour threshold is a code constant, not a setting.
- **Overtime** is:
  - time outside the person's hours;
  - all time on a day off;
  - time within hours beyond the day's required hours.

  Overtime first makes up any shortfall in the month, and the remainder is paid at the multiplier.
- **Shortfall** (unexcused missing hours in a closed month) is deducted from salaried pay at the plain hourly rate. Hourly staff are simply paid for what they log. Nothing carries over between months.

## Decided on 30 Sep 2026

Answers to the questions raised after the helper sessions' work was merged.

| # | Rule |
|---|---|
| O3 | **PDFs are made on the server**, so invoices, quotes and BOQs can be emailed with them. The library is PDFsharp with MigraDoc (MIT licence, free for everyone). The layout starts plain and is refined later. The documents have no Arabic ([documents.md](documents.md)). |
| O4, O5 | **Employment is dated periods** (decision 24): leaving takes a last working day, and time away between two periods isn't paid unless it began that month and the admin says so. |
| O6 | **The month just ended stays open through the 1st** (D11). |
| O7 | **The cost warning is Design only** (A1). A site expense's cost is always what was paid for it. An Execution site's profit is watched by the profit floor instead. |
| O8 | **Auto-archive doesn't wait for payment**, but an archived project still owed money stays in the "finished but still owed" follow-up. |
| O9 | **Overtime of work past midnight counts on the day it started**, as pay does. |
| O10 | **Revenue always lands in a month**: with no priced cost it follows the hours logged, then the completion month. |
| O11 | **Every completion and archive finalises the project** (the backend's rule). The close-out's "Notify the contributors" switch is gone: `notifyProject` decides. |
| O12 | **Projects have no members.** Anyone in the domain may work on any project; the unused member tables are dropped. |
| O13 | **Online means an open live connection**; last seen moves with use (at most every 5 minutes). |
| O14 | **No single sign-on in version 1**; the sign-in page no longer mentions it. |
| O15 | **A quiet stretch is 4 days without work**, everywhere. |
| O16 | **An expense's currency changes from a month**: the line ends and a new one takes over, so past months keep theirs. |
| O17 | **The company's address and website are settings**, for the letterhead. |
| O18 | **Money stays exact to the cent**: no tolerance for small balances. |

## Open questions

These have no decision yet. The schema doesn't depend on them.

1. **Public holidays:** a company holiday calendar, or keep excusing per person?
2. **Break edge cases:** is the whole break paid after any in-hours minute? Should the 6-hour threshold be configurable? *(Only for days with a break: none from 1 December 2026, decision 25.)*
