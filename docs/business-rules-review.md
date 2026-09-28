# Business rules from the prototype update (28 Sep 2026)

The Hourline prototype changed around 27 September: the Execution pricing model was replaced, and working schedules, an hours ledger and pay views were added. The schema was updated for it in the `ExecutionPricingAndWorkingTime` migration (see [database.md](database.md)).

This file records the business decisions taken at the same time, so they're applied when the API features are built. It also lists what is still open.

## Decided

| # | Rule | Where it is enforced |
|---|---|---|
| 1 | **Working time is dated.** The company working week, the break, the overtime multiplier and personal schedules only apply from their effective date. A change never rewrites past months' overtime, hours or pay. | Schema (dated tables) |
| 2 | **Excused time is paid** by default. Each excuse has an `is_paid` flag for unpaid leave. Leave types and a public-holiday calendar are deferred. | Schema; API / payroll calculation |
| 3 | **A finalised Execution project is locked.** Nobody, admins included, can add, edit or delete its materials or labour until it is reopened. *(The prototype still lets admins edit after finalise; the backend won't.)* | API |
| 4 | **A stated material's price is frozen.** Once a material appears on a statement, neither a markup change nor a manual edit changes its client price. *(The prototype reprices stated materials live; the backend won't.)* | API; statement lines keep the price shown |
| 5 | **Execution has no internal projects.** Every site is for a client. | Schema (CHECK) |
| 6 | **The initial payment is flagged** with `payments.is_initial`, not recognised from its note text. At most one live initial payment per project. | Schema (generated column + unique index) |
| 7 | **Labour defaults are admin-only** (`labour_report:manage`), per the permission catalogue. *(The prototype lets employees change them; the backend won't.)* | API / permissions |
| 8 | **One company-wide working week**, editable by super admins only (`working_time:manage`). People with different hours get a personal schedule (`schedule:manage`, super admins). Excusing hours is also super-admin only (`hours:excuse`). | Permissions |
| 9 | **Working-time and schedule changes start today or later**, never in the past, so closed months never change. A change dated in the future may be replaced before it starts. | API |
| 10 | **A missed working day deducts the full paid day.** In a closed month, each whole working day with no in-hours work and no excuse deducts required hours plus the break (8h on a 9–5 day) from salaried pay. Any other shortfall deducts the missing hours. | Payroll calculation |
| 11 | **Made-up overtime** comes from all of the month's overtime, in date order (as in the prototype). | Payroll calculation |
| 12 | **Salaries are always calculated.** No month-close snapshot for now. | Payroll calculation |
| 13 | **Mid-month joiners are pro-rated on their own working days** (personal schedule, otherwise the company week), not a fixed Monday–Friday. Everyone's hourly equivalent stays salary ÷ 173.33. | Payroll calculation |
| 14 | **The hours ledger** applies to Design employees only, from the month they joined. Admins and Execution staff have none. | Payroll calculation |
| 15 | **The Salaries page lists everyone with a pay record**, admins included. Super admins appear only if they have pay. | API |
| 16 | **Company overhead:** amount changes are dated (past months keep their cost), totals are kept per currency, and "cancel" ends a line at the end of the current month. It can be resumed only before that date passes; after that, a new line is added. | API |
| 17 | **A manual price equal to the automatic one is stored as automatic** (`client_price` NULL), so it keeps following the markup. *(Was open question 5.)* | API |
| 18 | **No separate delivery date** for now: materials are dated by when they were added, labour by its day. *(Was open question 6.)* | Schema unchanged |
| 19 | **Unpaid excuses** are deducted from salaried pay: unpaid minutes at the hourly rate, an unpaid day off as the full paid day, an unpaid whole month as the whole salary. | Payroll calculation |
| 20 | **Hourly staff** are paid for what they log; excuses only clear their shortfall and never add pay. | Payroll calculation |
| 21 | **Admins who log hours** are paid overtime like employees, without an hours ledger. | Payroll calculation |

## Decided for Design work (28 Sep 2026)

Taken when the Design work API was built (see [design.md](design.md)).

| # | Rule |
|---|---|
| D1 | **The timer runs in the client.** Stopping it logs an ordinary entry; nothing is stored while it runs. |
| D2 | **Nobody edits someone else's entries**, admins included. Whoever holds `work_entry:create` logs their own work (Design admins included, by the role defaults). |
| D3 | **Entry rules are enforced on the server.** An entry is locked once past the edit window (counted from when it was logged) or while its project no longer accepts work. 5 minutes to 16 hours, in whole minutes. With future entries allowed, any end time is accepted. |
| D4 | **Entry attachments wait** for a file-upload feature. |
| D5 | **Comments can't be edited or deleted.** Posting a comment to the team group chat is refused until messaging exists. |
| D6 | **Logged-work notifications** go to active people holding `work_entry:view_all` in Design, never the author. |
| D7 | **Domain settings aren't seeded.** A domain uses the defaults until its admins save. Each domain's admins (`domain_settings:manage`) edit their own row. Of the notification settings, only `notifyEntry` and `notifyIdle` (the dashboard's idle projects) are used for now; the others need scheduled jobs. |
| D8 | **Money in the activity feed** shows without its amount to people without `financial:view`. Company-level events need `company:view`. |
| D9 | **Logging doesn't add project members.** |
| D10 | **Employees set meetings.** Design's `meeting:create` (every role, `MeetingCreatePermission` migration) sets meetings and moves or removes your own; `meeting:manage` covers anyone's. Meetings stay Design-only, and nobody moves someone else's meeting without `meeting:manage` (the prototype let anyone drag any meeting). |

## Carried over from the prototype as-is

- **Execution money:**
  - The project fee plus material client prices is the agreed amount.
  - Ground labour is never billed.
  - Invoicing is open from project creation.
  - Payments on account are normal.
  - Cash profit = received − spent.
- **Statements:**
  - Numbering is one sequence per year, `ST-YYYY-NNN`, across the company.
  - A statement is not an invoice and changes nothing that is owed.
- **Labour weeks** run Monday to Saturday.
- **Materials** are grouped by their type name (trimmed, lower-case); the category field was removed.
- **The break** is paid, reduces only the required hours, and applies on days longer than 6 hours. The 6-hour threshold is a code constant, not a setting.
- **Overtime** is:
  - time outside the person's hours;
  - all time on a day off;
  - time within hours beyond the day's required hours.

  Overtime first makes up any shortfall in the month, and the remainder is paid at the multiplier.
- **Shortfall** (unexcused missing hours in a closed month) is deducted from salaried pay at the plain hourly rate. Hourly staff are simply paid for what they log. Nothing carries over between months.

## Open questions

These have no decision yet. The schema doesn't depend on them.

1. **Public holidays:** a company holiday calendar, or keep excusing per person?
2. **Break edge cases:** is the whole break paid after any in-hours minute? Should the 6-hour threshold be configurable?
3. **Project pay and breaks:** should the project pay report include hourly staff's paid breaks? The prototype leaves them out, so it differs from Salaries.
7. **Month-end "short hours" alert:** should it be a scheduled job that notifies super admins?
