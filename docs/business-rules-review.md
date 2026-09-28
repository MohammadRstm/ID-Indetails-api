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

1. **Shortfall value:** a missed day deducts 7h (required hours), but a paid day is 8h. Should a missed day also deduct the break?
2. **Public holidays:** a company holiday calendar, or keep excusing per person?
3. **Month close:** freeze a month's salary results (a payroll snapshot), or keep them fully calculated?
4. **Part-time salaried staff:** is their hourly equivalent still salary ÷ 173.33? Should mid-month joiners be pro-rated on Monday–Friday (prototype) or on their own schedule?
5. **Break edge cases:** is the whole break paid after any in-hours minute? Should the 6-hour threshold be configurable?
6. **Made-up overtime:** should it be taken from all of the month's overtime in date order (prototype), or only overtime worked after the shortfall?
7. **Project pay and breaks:** should the project pay report include hourly staff's paid breaks? The prototype leaves them out, so it differs from Salaries.
8. **Hours-ledger start:** from which date does the ledger apply? The prototype uses a demo date. Does it apply to admins who log hours? It never applies to Execution.
9. **Meetings:** the prototype now has meetings in both domains and lets employees create them, and anyone can drag any meeting on their timeline, while delete is limited to the creator and admins. The permission catalogue still has `meeting:manage` as Design- and admin-only. Who may create, move and delete meetings?
10. **Manual price equal to the automatic price:** store it as a manual price, or treat it as "automatic" (NULL)?
11. **Material and labour timing:** costs are dated by when they were recorded (`added_at`, and the labour day). Is a separate delivery date needed?
12. **Month-end "short hours" alert:** should it be a scheduled job that notifies super admins?
