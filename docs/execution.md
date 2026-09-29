# Execution sites

What site staff and admins record on an Execution project, and how it is priced for the client. Execution projects themselves, their fee and markup, and their cost, profit and margin are in [projects.md](projects.md). A project of a domain the caller can't see reads as `404`, and Design projects have none of this (`422 not_for_this_domain`).

**A finalised project is locked.** Once a site is completed or archived, nobody (admins included) adds, changes or removes its materials until it is reopened (`422 project_locked`). Projects on hold stay open. This lock, and the rule that people change only what they recorded unless they hold the manage permission, are in `Business/Execution/SiteRules.cs`.

## Materials

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects/{id}/materials?type=&supplier=&addedById=&from=&to=&search=&page=1&pageSize=30` | `project:view` | Newest first, with totals, types, suppliers and who added them |
| `POST /api/projects/{id}/materials` | `material:create` | `{ items: [{ name, quantity, unit, unitCost, amount, supplier, note, clientPrice }] }`, 1–100 at a time |
| `PUT /api/projects/{id}/materials/{materialId}` | `material:create` (own) or `material:manage` (anyone's) | Replaces name, quantity, unit, unit cost, amount, supplier and note |
| `DELETE /api/projects/{id}/materials/{materialId}` | `material:create` (own) or `material:manage` (anyone's) | Soft delete (`deleted_at`) |
| `PUT /api/projects/{id}/materials/{materialId}/client-price` | `project:pricing_manage` | `{ clientPrice }`; `null` returns it to the project's markup |

Rules (`Business/Materials/MaterialRules.cs`):

- A material needs a **type** (`name`) and a **cost**: quantity × unit cost when both are given, otherwise the total (`amount`). Quantity (3 decimals), unit and unit cost (4 decimals) are optional. A total that doesn't match quantity × unit cost sets the unit cost to total ÷ quantity. Materials are in the project's currency (USD).
- People change and remove only what they recorded; `material:manage` changes anyone's (`403 forbidden`). `canEdit` on each row says whether the caller may.
- **Client price:** a material's own price when set, otherwise cost × (1 + the project's markup). Setting one, on adding or afterwards, needs `project:pricing_manage`. A price equal to the automatic one is stored as automatic, so it keeps following the markup.
- **Stated materials** (on a client statement) keep their type, quantity, unit, cost and price, and can't be removed (`422 material_stated`). Their supplier and note can still change. `priceSource` is `auto`, `manual` or `stated`.
- **Who sees what:** everyone who can see the project sees materials with their costs (site staff type them). Client price, price source, markup, charge totals and the default markup are only returned to callers with `financial:view` or `project:pricing_manage`.
- **Grouping:** types and suppliers are grouped by their text trimmed, with single spaces, in lower case (`type` and `supplier` filters take these keys). A type is labelled as most recently typed. The `from`/`to` filters are days in the company time zone. `matching` totals cover everything the filters match, `all` the whole project.
- Events: `material_added`, `material_updated`, `material_removed` (cost in the `amount` columns) and `material_priced` (new and previous client price), with the name in the payload.
- Adding materials sends one `materials_added` notification (the count in the payload) to every active person who holds `material:manage` and can see the domain (super admins included), except the person who added them.

Material cost and client prices feed the shared finance calculator (`Business/Finance/ProjectFinance.cs`): cost is converted at the rate of the day a material was added.

## Ground labour

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects/{id}/labour-weeks` | `project:view` | Every week from the project's first to the current one (or the one it was finalised in), newest first, reported or not, with the labour total |
| `GET /api/projects/{id}/labour-weeks/{weekStart}` | `project:view` | One week's days. A week not yet reported comes back filled in from the defaults, up to today (`isReported: false`) |
| `PUT /api/projects/{id}/labour-weeks/{weekStart}` | `labour_report:create` (new or own weeks) or `labour_report:manage` (anyone's) | `{ days: [{ date, workers, dailyPay }], note }` submits the week, or replaces the days of one already reported |
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

## Client statements

A statement tells the client which materials were supplied, at the prices they are charged, and where payment stands. It is **not an invoice**: it records no payment and changes nothing that is owed. Once made it is an immutable snapshot of that day's figures; there is no edit, void or delete.

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects/{id}/statements?page=1&pageSize=10` | `statement:create`, `statement:send` or `financial:view` | Newest first, with each one's total, material count and last delivery |
| `POST /api/projects/{id}/statements/preview` | `statement:create` | The statement as it would be made now, with a provisional number; nothing is saved |
| `POST /api/projects/{id}/statements` | `statement:create` | `{ mode: detail \| total, includeEarlier, excludedMaterialIds, unlistedTypeKeys }` makes and numbers it |
| `GET /api/statements/{statementId}` | `statement:create`, `statement:send` or `financial:view` | The statement as the client reads it |
| `POST /api/statements/{statementId}/deliveries` | `statement:send` | `{ channel: email \| whatsapp \| download, recipient, note }` records a send. Repeatable. Saving the PDF counts as a `download` |

Rules (`Business/Statements/StatementRules.cs`):

- **New materials** are those on no earlier statement. A detailed statement takes them all except `excludedMaterialIds`; a total-only statement takes them all.
- **Earlier materials** come back with `includeEarlier`. They always count in the total. In a detailed statement, a type with nothing new can be left unlisted (`unlistedTypeKeys`, the type keys of the materials list); unlisted types are shown as one line (`hiddenTypes`). A type with new materials is always listed (`400`).
- A statement needs at least one material (`422 statement_empty`).
- The client reads **one row per material type** (grouped like the materials list), with what it came to on earlier statements, what is new and the total, plus the quantity added up when every delivery has the same unit. A total-only statement has no rows, just the total. `since` is the day of the previous statement.
- **Prices are frozen.** Each line keeps the client price shown, and every material a statement shows becomes *stated*: its price is written to the material and no markup change or manual price moves it again (see Materials).
- The **payment summary** is snapshotted: what was received and the agreed amount (material client prices plus the fee, from the shared finance calculator), with what remains or was paid ahead. Everyone who may read the statement sees it, `financial:view` or not: it is printed on the client's document (decision M10 in [business-rules-review.md](business-rules-review.md)).
- **Numbers** are `ST-YYYY-NNN`, one sequence per year across the company, taken from `statement_sequences` under a row lock so a number is never handed out twice. A preview doesn't use one up. Statements can be made while a site is on hold or finalised.
- The server records sends but doesn't email the statement; the client produces the PDF.
- Events: `statement_created` and `statement_sent` (the statement's total in the `amount` column; the number, and the mode or channel, in the payload).

## Site staff views

Site staff don't log hours; what they record on site stands in for it.

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects/site-summary?ids=1&ids=2` | `project:view` | One card per site among the ids (1–100, typically a page of the project list): material count and cost, reported weeks, ground labour, and when the caller last recorded something there. Projects the caller can't see and Design projects are left out |
| `GET /api/me/site-records` | `material:create`, `material:manage`, `labour_report:create` or `labour_report:manage` | What the caller recorded |
| `GET /api/employees/{userId}/site-records` | `employee:view` | What someone recorded. People of a domain the caller can't see read as `404` |

- **Cards:** material cost and ground labour come from the shared finance calculator (in USD).
- **Records** (`Business/Execution/SiteRecords.cs`): the materials a person added (dated by when they were added) and the labour weeks they submitted (dated by when they were submitted, valued at the week's total), on sites the caller can see. The response has the totals, this calendar quarter's value, the days with anything recorded, the last 16 weeks (oldest first, ending with the current one), a row per site (most recently recorded first) and the 20 latest records.

## Site money

A site's money (received, cost by source, cash profit, material profit, expected profit as `profit`, owed or paid ahead) is on `GET /api/projects/{id}` for callers with `financial:view`, from the shared finance calculator. Over time:

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects/{id}/site-money/timeline` | `financial:view` | Cash week by week, and when profit would reach zero |

- **Weeks** run Monday to Sunday, from the project's first week to the current one (or the one it was finalised in). Each has what was received, spent and paid in ground labour up to its end, the profit so far (received − spent), and what that week added in payments and ground labour. They come from the finance calculator (`ProjectFinance.CashByWeek`): costs on their day at that day's rate, payments by the day received.
- **Projection** (`LabourRules.ProjectZero`): at today's labour defaults (workers × daily pay a working day), how many working days (Monday to Saturday, from tomorrow) until cash profit reaches zero, and until expected profit would, once the client has paid everything agreed. `state` is `done` for a finalised site, `losing` when cash profit is already below zero, `no_defaults` without a crew and day rate, otherwise `ok`. It assumes the crew, their pay and the money received stay as they are, and no new materials.
