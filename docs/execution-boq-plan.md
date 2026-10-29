# Execution BOQ rework: build plan and handoff

> Written 2026-10-29 by the laptop session that planned this work, and handed to a session on the desktop. The same file is in both repos: `ID-Indetails/docs/execution-boq-plan.md` and `Ident-Internal-Management-System-client/docs/execution-boq-plan.md`. Delete it from both repos once the last phase has merged.

- **Spec (Claude Doc, older draft):** https://claude.ai/artifact/7CDiKWuJiXzJMAG21XquwD. Where this file and the doc differ, **this file wins**.
- **Approval:** the user approved this plan and every DB change listed below on 2026-10-29. Any DB change NOT listed here still needs the user's explicit approval (see CLAUDE.md).

## 1. Decisions (confirmed by the user)

### How the company works
- **Internal drafts.** Two admins each write an internal draft BOQ in the app. Each author sees only their own draft; super admins see all of them.
- **The client BOQ.** A super admin builds one client BOQ from those drafts. It's sent to the client as a PDF, as often as needed, and stays a draft until a super admin presses **Agreed**, with a confirmation.
- **After agreement:** weekly labour and the expenses list are filled in, payments carry on as today, and later price changes are **VOs**.

### Money
- **Work price** = agreed client BOQ total + agreed VO totals. It's stored in `Project.ProjectFee` and set **only** by agreeing or withdrawing; nobody types it in.
- **Supervision** = work × `supervisionRate`. The default is **10%**, set per project and editable.
- **TVA** = work × `tvaRate`. The default is **11% for every client project in every area**, Design included. TVA is never charged on supervision.
- **Net revenue / agreed amount**, used for profit and the profit floor, = work + supervision.
- **Client gross** = work + supervision + TVA.
- **Profit floor:** it applies only once ProjectFee > 0, so it's off until the first agreement. A price drop (withdrawal, negative VO) goes through the existing force / blocked-change flow.

### Removed or not built
- **Quotes:** removed for Execution projects. Design keeps them.
- **Client statements:** removed entirely. Nothing is in production, so drop them with no data migration.
- **Permits:** only BOQ lines; no permits domain.
- **Subcontractors:** ordinary expense lines with a payee; no subcontractors domain.
- **Portal:** BOQs aren't shown there yet; delivery is by PDF only.

### BOQ details
- **Lines** have a code, description, exact quantity, waste %, unit and rate.
- **The client** sees the final quantity, unit, rate and total. Exact quantity and waste are internal.
- **Adjustment:** the whole BOQ can be adjusted by a fixed new total or by a %.
- **Rights:** client BOQs and VOs are super admin only (create, edit, send, agree, withdraw). Internal drafts need `boq:create`, which replaces `statement:create`.

### Costs and accounts
- **Expenses list.** The materials table becomes a cost-only expenses list for Execution: no client price, no markup. It replaces the "Add project expense" window for Execution projects.
- **Account labels:** one company-wide list that super admins edit in Settings (seeded with Concrete, Finishing, Permits). Used on payments and expenses.
- **Supervision taken:** the company's withdrawals of its supervision fee are recorded. They're **not** a cost.
- **Set aside by the user:** committed costs, the Mila 2 finishing price, the Saifi Apartment / marketing rows.

### Mila 2 import
- **Source:** `#171-Residence Mila  2.xlsx` (it was on the laptop's Downloads folder).
- **Timing:** after phase 3.
- **Rules:** **insert nothing** until the user approves the mapping and names the environment (local dev or production). Enter data only through the API, never SQL, and keep a log.
- **Mila 2 specifics:** TVA is 0% and supervision is 8%.
- **Check figures:** BOQ 744,646.20 + VO "Mila one" 36,500 + VO "Fence" 6,500 = 787,646.20; + 8% = 850,657.90; received 810,000; rest 40,657.90.

## 2. How to build
- **Branches and PRs:**
  - Stacked branches, one API PR + one client PR per phase, each branch off the previous phase's branch so migrations stay ordered.
  - Open the PRs when a phase is finished and checked.
  - Track CI (standing permission to bind PRs and turn on auto-fix). **Merging needs the user**, in phase order.
  - Push each branch once it's finished and checked locally. CI runs only on PRs, so pushing a branch without a PR doesn't use CI.
- **Checks:**
  - **API:** build Release with `-warnaserror` like CI, and run the unit tests you touched. Run integration tests for the classes you touched if the machine allows; CI runs the full suite.
  - **Never run the Proton evals:** add `&FullyQualifiedName!~ProtonEvalTests` to any filter.
  - **Client:** `tsc`, `eslint`, and the vitest files you touched.
  - **One heavy run at a time.** On the desktop, normal cores are fine; ask the user if unsure.
- **Code:** follow each repo's CLAUDE.md: simple reviewable code, thin controllers, FluentValidation, the uniform error contract, live inline form errors, docs updated in the same branch, plenty of tests.
- **Database:** never run `dotnet ef database update` without asking. Port 3307 is another project.
- **Commit trailer:** `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. PR bodies end with the Claude Code line.

## 3. Phases

### Phase 1: settings, supervision, account labels, no Execution quotes (branch `MR-Exec-Settings`)
**Status:** finished on the desktop on 2026-10-29: migration `AccountLabelsAndSupervision`, Release build clean, schema regenerated. PRs: API MohammadRstm/ID-Indetails-api#162, client MohammadRstm/ID-Indetails-client#168. Tests run once on the whole stack after phase 4 (the user's call). The handoff notes below are kept for the record.

**Status at handoff:** 

**API: code, tests and docs are written and committed; the migration isn't made and nothing has compiled since the last edits.**

Commits on `MR-Exec-Settings`: `72080f8` (models and seed), `ff416f0` (account labels API), `bf8ba71` (pricing with supervision, TVA 11%, no Execution quotes).

The work trailer says "Claude Sonnet 5.5". That's fine to leave as is.

What's left on the API side, in order:
1. **Generate the migration.** Run `dotnet ef migrations add AccountLabelsAndSupervision --project ID-Indetails.api --output-dir Data/Migrations`. After the AddColumn in `Up()`, add:

   ```csharp
   migrationBuilder.Sql("UPDATE projects SET supervision_rate = 0.10 WHERE domain_code = 'execution' AND billing_type = 'client';");
   ```

   Check that the migration creates:
   - the column `projects.supervision_rate` decimal(5,4) NULL;
   - the check constraint `ck_projects_supervision_rate_range`;
   - the table `account_labels`, with name varchar(40) unique, and three seed rows: Concrete 1, Finishing 2, Permits 3.
2. **Build and test.** Do a full solution Release build with `-warnaserror` and run the unit tests. One early Debug build of the API project failed only on a missing `using`, which has since been fixed. The test projects have never been compiled. The pinned counts should be: tables 96, Proton Never 91.
3. **Integration tests.** Run the touched classes if the machine allows:
   - AccountLabel, Tva, Quote, Project, ProjectPaymentChange, ProjectStatus, Payment, ProtonMoneyActions, SchemaConstraint;
   - plus the Labour, Material, Search, ActivityFeed and PrivilegeAndPrivacy edits.
4. **CLAUDE.md edits (proposed, not yet applied).**
   - In the §0 Money bullet, Execution revenue becomes work price (from agreed BOQs) + supervision (work × rate, default 10%). For now it also includes the client prices of materials sent on statements; phase 2 removes that. TVA is 11% by default, charged on the work and never on supervision.
   - Add account labels to the Company doc row.
   - The pinned counts move (see step 2).
5. **Open choice for the user.** The payment-change history records a supervision change as a row with a reason, plus an event `supervision_rate_set` that carries the rates. The history response doesn't show the rate itself. Showing it like TVA would need `supervision_rate` and `previous_supervision_rate` on `project_payment_changes`. That's a DB change that hasn't been approved yet: ask the user.

Contract as built (it differs from the plan in places):
- **Account label permissions.** Writes and `?includeArchived=true` need `company_settings:manage`, which super admins hold by default.
  - Errors: `409 account_label_taken`, `422 account_label_order_stale`.
  - The `order` endpoint must list exactly the non-archived labels, each once.
  - Restoring a label puts it last.
  - Event: `account_label_changed`.
- **Create.** `projectFee` is now refused on every create (400, key `projectFee`). A new Execution site starts with `projectFee` 0.
  - `supervisionRate` needs `project:pricing_manage` and is Execution-only.
  - `tvaRate` defaults to 0.11.
- **Pricing.** `PUT /api/projects/{id}/pricing` takes `{ materialMarkup (required), supervisionRate?, tvaRate?, description (required), projectFee must be absent }`. A no-op change returns error key `materialMarkup`.
- **Money response.** `ProjectResponse.money` gains `supervisionRate` and `supervisionUsd` (Execution only).
  - `revenue` = work + supervision (+ client prices of sent materials, until phase 2).
  - `revenueWithTva` = revenue + TVA, so the TVA amount is the difference between the two.
  - `paymentWithTva` = gross of work + supervision.
  - The list and summary rows carry only `revenue`.
- **Quotes.** Any quote endpoint on an Execution project returns `422 quote_not_for_execution`. Creating a project no longer sends `quote_due` for Execution.
- **Payment TVA share** = payment × project TVA ÷ project gross.
- **Test helper.** `ProjectTestSteps.SetWorkPriceAsync(projectId, fee)` sets the work price by SQL and inserts a `project_fee_set` event. Phase 2's agree step should do the real equivalent.
- **Dead code.** The Execution-only "materials billed separately" text in QuotePdf/QuoteEmail is now dead. Remove it in phase 2.


**Client: built and committed.**

Commits on `MR-Exec-Settings`:
- `8b9fa18` account labels Settings + API module/hook
- `7b6fbce` TVA 11% and supervision on create
- `1a30f89` work price read-only, supervision line, pricing dialog
- `f5a2deb` no quotes on Execution, notes in `docs/build-plan.md`

Last checks:
- tsc and eslint were clean.
- vitest ran 34 files and 437 tests. One failure in the new labels test was fixed afterwards; that file was re-run alone (8/8).
- tsc, eslint and the affected vitest set were **not re-run** after the last small edits. Run them once.

What's left on the client side:
1. Regenerate `src/api/schema.d.ts` with `npm run api:types` against the built phase 1 API. It was hand-edited to match. The account-label paths aren't in the schema; the hook uses plain `http` calls.
2. Re-check names against the API commits above. The client reads `project.money.supervisionRate` and `project.money.supervisionUsd`. The client total is `money.revenueWithTva`.
3. Left for phase 2:
   - the statement help articles;
   - the old fee/agreed wording in `explainedMoney.ts` and `CompleteProjectModal`;
   - the `SiteMoneyPanel` material-profit rows;
   - the `?quote=new` link in `notificationWords.ts`.
4. Not done yet: a quick browser smoke check of create, pricing, Settings → Account labels, and no quote UI on Execution.

**Phase 1 is finished when:** the migration exists, the API Release build passes with `-warnaserror`, the unit tests pass, the client checks pass and the schema is regenerated. Then push both branches and open PRs `MR-Exec-Settings` → master/main.


**Approved DB changes:**
- `projects.supervision_rate` (nullable, backfilled to 0.10 for Execution client projects).
- New table `account_labels` (id, name unique, position, archived_at, created_at), seeded with Concrete, Finishing, Permits.

**Contract:**
- `Tva.DefaultRate` is 0.11.
- **Create:** an Execution client project takes `supervisionRate` (default 0.10). `projectFee` is refused for Execution.
- **Pricing edit:** for Execution it takes `tvaRate`, `supervisionRate` and `materialMarkup` (markup until phase 3), and refuses `projectFee`. Supervision changes go into the payment-change history like TVA.
- **Responses:** projects expose `supervisionRate`, and the Execution money figures expose `supervisionUsd`.
- **Account labels endpoints:**
  - `GET /api/account-labels` (`?includeArchived=true` for super admins) → `[{ id, name, position, archived }]`;
  - `POST /api/account-labels` `{ name }`;
  - `PUT /api/account-labels/{id}` `{ name }`;
  - `POST /api/account-labels/{id}/archive` and `.../restore`;
  - `PUT /api/account-labels/order` `{ ids }`.

  Writes are super admin only. Error `account_label_taken`.
- **Quotes:** a quote on an Execution project is refused with `quote_not_for_execution`, and no "send quote?" prompts or notifications appear for Execution.
- **Client:** the Execution create form has no agreed amount, plus a supervision % field (10) and TVA 11. The pricing modal handles TVA + supervision, with the work price read-only ("Set when a BOQ is agreed"). Financials show work price, supervision, TVA and client total. Settings get an Account labels section for super admins, plus a reusable labels hook/select. No quote UI on Execution.

### Phase 2: BOQs and VOs replace statements (branch `MR-Boqs`, off `MR-Exec-Settings`)
**Status:** API built (see "Phase 2 as built" at the end); the client is being built on the desktop. The full contract is in section 4, with the desktop's decisions at its end.

### Phase 3: cost-only expenses list (branch `MR-Site-Expenses`, off `MR-Boqs`)
**Status:** API built (see "Phase 3 as built" at the end); the decisions below the outline win. The client is built on the desktop. Outline:
- **Data:**
  - Rename `project_materials` → `site_expenses`; the model and API routes say "expenses", and the client's Execution "Materials" panel becomes "Expenses".
  - Fields: item, quantity, unit, unit cost or amount, currency, supplier/payee, `spentOn` (date, required, default today), `category` (materials | contractor | equipment | fees | other) and optional `accountLabelId`.
  - Drop `client_price` and `projects.material_markup`, plus the markup/client-price logic, events, validators and UI. Check what `client_ref` is first: if it's an idempotency key, keep it.
- **Execution expenses:** the separate `ProjectExpense` "Add project expense" window is no longer offered for Execution projects. A migration moves any existing Execution `project_expenses` rows into `site_expenses`.
- **Profit floor:** the guard keeps covering expense writes.
- **Approved DB changes:** the rename, the added and dropped columns listed above, and that row move.

#### Phase 3 decisions made on the desktop (2026-10-29)
Settled with the user or from the code. **Where they differ from the outline above, these win.**

**Routes (the user's choice).** Site expenses live at `/api/projects/{projectId}/site-expenses` (`GET` list, `POST` a batch, `PUT` / `DELETE` `/{expenseId}`). The same controller also answers the old `/api/projects/{projectId}/materials` paths with the same shapes, so the phone app keeps recording until it's updated in its own PR; docs say the alias is temporary. `/api/projects/{id}/expenses` stays the project-expenses route. `PUT .../{id}/client-price` goes.

**JSON names.** `name` (shown as "Item") and `supplier` (shown as "Supplier / payee") keep their names so the phone's requests still work. New optional request fields: `currency` (default USD, the existing currency validator), `spentOn` (default today in the company time zone), `category` (default `materials`) and `accountLabelId` (an existing label; archived ones can't be newly picked, but an expense keeps one it already has). The response gains `spentOn`, `category` and `accountLabel: { id, name } | null`, and loses `clientPrice`, `priceSource` and `markup`. The list loses `charge` and `defaultMarkup`, gains `category` and `accountLabelId` filters, and its `from` / `to` filter on `spentOn`. Record names: `SiteExpense…` (`SiteExpenseResponse`, `SiteExpenseListResponse`, `AddSiteExpensesRequest`, `NewSiteExpenseRequest`, …).

**Data (approved).**
- `project_materials` → `site_expenses`, model `SiteExpense`. `client_ref` stays: it's the phone's idempotency key.
- Added: `spent_on` (date, required; backfilled from the day each row was added, in the company time zone), `category` (enum `materials | contractor | equipment | fees | other`, check constraint) and `account_label_id` (optional FK to `account_labels`).
- Dropped: `site_expenses.client_price` and `projects.material_markup`. `ck_projects_execution_pricing` is rewritten without the markup.
- `project_payment_changes.material_markup` / `previous_material_markup` stay as history: no longer written, still shown for old rows.
- Execution `project_expenses` rows move into `site_expenses`: `label` → `name`, `amount`, `currency`, `spent_on`, `note`, `created_at` → `added_at`, `created_by_id` → `added_by_id` (the project's owner when null), `deleted_at` kept. Categories: contractor → contractor; assets, hardware → equipment; services, software → fees; travel, other → other.

**Rules.**
- Creating a project expense on an Execution project is refused (`422 expense_execution_uses_site_expenses`); the client offers no such window there.
- Markup and client-price logic, events, validators, Proton actions and UI all go. The pricing edit becomes `{ supervisionRate?, tvaRate?, description }`; create loses `materialMarkup`; `ProjectMoneyResponse` loses `materialMarkup`.
- Cost: `ProjectCostResponse.materials` → `siteExpenses`, and `CostSource.Material` → `SiteExpense` (`site_expense`). Costs are dated by `spentOn` for exchange rates.
- The profit floor keeps guarding adds and edits.
- Stored kinds don't change, so no event data migration: the activity and notification kinds (`material_*`) and the blocked-change kinds `material_add` / `material_update` keep their values. The words on screen say "expense".
- Permissions keep their codes (`material:create`, `material:manage`). `PermissionExplanations` says "site expenses"; the seeded catalog descriptions are unchanged.

### Phase 4: payment labels, figures, supervision taken (branch `MR-Account-Figures`, off `MR-Site-Expenses`)
**Status:** API built (see "Phase 4 as built" at the end); the decisions below the outline win. The client payment report PDF is not built. Outline:
- **Data:**
  - `payments.account_label_id` (optional).
  - New table `supervision_withdrawals` (id, project_id, amount_usd, taken_on, note, created_by_id, created_at, deleted_at), recorded by super admins. It isn't a cost.
- **Figures** on the Execution project:
  - work price, supervision, TVA and client total;
  - received, rest to be paid;
  - costs (expenses + labour) and profit;
  - supervision due vs taken;
  - cash in account = received − costs − supervision taken;
  - rest in account per label (received − spent per label, plus an "unlabelled" row);
  - a breakdown by category, payee and label.
- **Client payment report PDF:** waits until the user agrees its layout.
- **Approved DB changes:** the column and the table above.

#### Phase 4 decisions made on the desktop (2026-10-29)
Settled with the user or from the code. **Where they differ from the outline above, these win.**

**Payment labels.** `payments.account_label_id` is optional. Recording and editing a payment accept `accountLabelId`, which must be an existing, non-archived label when newly set. Payment responses carry `accountLabel: { id, name } | null`.

**Supervision taken.**
- Endpoints: `GET /api/projects/{id}/supervision-withdrawals`, `POST` the same path with `{ amountUsd, takenOn, note? }`, and `DELETE /api/supervision-withdrawals/{id}` (soft delete via `deleted_at`).
- Who: writes need `payment:record`, reads need `financial:view`.
- Only on Execution client projects. Amount > 0; `takenOn` not in the future.
- It isn't a cost. Taking more than is due is allowed and shown as "over".
- Activity events record it and its removal.

**Figures.** `GET /api/projects/{id}/account-figures` (`financial:view`, Execution client projects only) returns:
- work price, supervision, TVA and client total;
- received, and rest to pay (client total − received);
- costs: site expenses, labour and total;
- profit (the project's existing profit figure);
- supervision due (work × rate), taken and left;
- pocket money moved to the pocket and paid by it;
- cash in account = received − costs − supervision taken − moved to pocket + paid by pocket. The user chose to count pocket moves.
- rows by account label, each with received (payments with that label), spent (site expenses with it) and rest. An "Unlabelled" row also carries labour, supervision taken and pocket moves, so the rows add up to cash in account.
- breakdowns of spending by category and by payee (`supplier`; "No payee" when empty).

Amounts are in USD at dated rates; costs with no rate on their day are listed as unconverted, like the cost response.

**Not built:** the client payment report PDF waits for the user to agree its layout.

## 4. Phase 2 contract (shared by API and client)

### Branches
- Branch `MR-Boqs`, created off `MR-Exec-Settings` (phase 1), in both worktrees.

### Who can do what (confirmed by the user)
- **Internal draft BOQs:**
  - Written by anyone with the `boq:create` permission who can see the project. This replaces `statement:create`; rename the permission key in the catalog, the seed and existing role grants.
  - Each author sees only their own internal drafts; super admins see all of them.
  - Internal drafts are never sent.
- **Client BOQs and VOs:** super admins only, for everything: create, edit, copy lines, send, agree and withdraw. `statement:send` is removed.
- **Read access:** anyone with `financial:view` on the project can read client BOQs, VOs and the price summary (read only).
- **Portal:** none. Delivery is by PDF only (email, or a recorded send).

### Rules
- **A BOQ:**
  - has a kind (`internal` | `client` | `vo`), a status (`draft` | `agreed`), a title, an author, lines and an optional whole-BOQ adjustment;
  - a VO also has a required reason.
- **Numbers:** client BOQs get `BQ-YYYY-NNN` and VOs get `VO-YYYY-NNN`, assigned on creation. Rename `statement_sequences` to `boq_sequences`, keyed by prefix + year, with the same row-lock approach. Internal drafts have no number.
- **A line** has a code (optional, e.g. "2.1.1"), a description (required), a quantity (exact), a waste rate (fraction ≥ 0, default 0), a unit (optional) and a rate.
  - finalQuantity = round(quantity × (1 + wasteRate), 2).
  - line total = round(finalQuantity × rate, 2).
  - Negative rates are allowed only on VOs, so a VO can lower the price.
- **Adjustment:** `none` | `percent` (value is a fraction, e.g. -0.05 or 0.10) | `total` (value is the new total).
  - adjustmentUsd = subtotal × pct, or value − subtotal.
  - totalUsd = subtotal + adjustmentUsd.
- **Totals shown** with each BOQ:
  - supervisionUsd = total × project.supervisionRate;
  - tvaUsd = total × project.tvaRate;
  - clientTotalUsd = total + supervision + TVA.
- **Drafts stay editable after being sent.** Each delivery stores the BOQ's totalUsd at send time.
- **Agreed BOQs are locked** (`422 boq_locked`) and can't be deleted.
- **Only one agreed client BOQ per project** (`422 boq_already_agreed`). Other client drafts stay as drafts; the UI shows them as "not chosen" once one is agreed.
- **VOs** can be created only once the project has an agreed client BOQ (`422 boq_not_agreed_yet`).
- **Agreeing or withdrawing** an agreement recomputes `Project.ProjectFee` = agreed client BOQ total + sum of agreed VO totals (0 when nothing is agreed — phase 1 starts new sites at 0). This is the ONLY way the work price changes.
  - Withdrawing needs a reason, recorded in the activity event, and returns the BOQ to draft.
  - Withdrawing the client BOQ while agreed VOs exist is refused (`422 boq_has_agreed_vos`).
- **Profit floor:** a recompute that LOWERS the price goes through the existing profit-floor guard and its force flow (`force` flag / blocked change), exactly like lowering the fee did. A rise never needs force.
- **Sending/agreeing a BOQ with no lines** is refused (`422 boq_empty`). Sending an internal draft is refused (`422 boq_internal`).
- **Activity events:** `boq_created`, `boq_sent`, `boq_agreed`, `boq_agreement_withdrawn`, the same for VOs (use one event family with the kind in the payload if that's simpler and consistent).
- **Statements are removed completely:**
  - Drop tables `client_statements`, `client_statement_lines` and `client_statement_deliveries`, plus their models, rules, services, controller, PDF, emails, tests and docs.
  - Materials lose everything that depended on statements:
    - "stated" price source, `material_stated`, `sentOn`, "sent" revenue;
    - the agreed amount no longer includes unsent materials;
    - the profit floor's "agreed incl. unsent materials" becomes just net (work + supervision);
    - the "can't complete a site while a material is unsent" rule.

    Drop statement-only columns on `project_materials` if any. Material client price/markup columns stay until phase 3.

### Endpoints
| Method & path | Who | Body / result |
|---|---|---|
| `GET /api/projects/{id}/boqs` | boq:create (own internal), super admin, financial:view (client+vo only) | `{ internal: BoqSummary[], client: BoqSummary[], vos: BoqSummary[], price: { agreedBoqUsd, agreedVosUsd, workUsd, supervisionRate, supervisionUsd, tvaRate, tvaUsd, clientTotalUsd } }` |
| `POST /api/projects/{id}/boqs` | internal: boq:create; client/vo: super admin | `{ kind, title?, reason?, fromBoqId? }`; `fromBoqId` copies all lines from a BOQ the caller can see → `BoqDetail` |
| `GET /api/boqs/{boqId}` | as above | `BoqDetail` |
| `PUT /api/boqs/{boqId}` | author of internal / super admin | `{ title, reason?, adjustmentType, adjustmentValue?, lines: [{ id?, code?, description, quantity, wasteRate, unit?, rate }] }`. Saves the whole document; lines are replaced in the given order → `BoqDetail` |
| `POST /api/boqs/{boqId}/copy-lines` | super admin (or internal author into own draft) | `{ fromBoqId, lineIds?: [] }`; omitting lineIds copies all; appended at the end → `BoqDetail` |
| `POST /api/boqs/{boqId}/duplicate` | super admin (client/vo) or author (internal) | → new draft `BoqDetail` of the same kind |
| `DELETE /api/boqs/{boqId}` | author / super admin | drafts only |
| `POST /api/boqs/{boqId}/agree` | super admin | `{ force?: bool }` (client or vo) → `BoqDetail` |
| `POST /api/boqs/{boqId}/withdraw-agreement` | super admin | `{ reason, force?: bool }` → `BoqDetail` |
| `GET /api/boqs/{boqId}/pdf` | super admin / financial:view (client/vo) | `bq-2026-001.pdf` / `vo-2026-001.pdf` |
| `POST /api/boqs/{boqId}/email` | super admin | `{ to, note? }`; emails the PDF and records an `email` delivery (`503 email_delivery_failed` records nothing) |
| `POST /api/boqs/{boqId}/deliveries` | super admin | `{ channel: email \| whatsapp \| download, recipient?, note? }` |

**BoqSummary:** `{ id, kind, number, title, authorId, authorName, status, totalUsd, lineCount, reason, createdAt, updatedAt, agreedAt, agreedByName, lastDelivery: { channel, sentAt } | null }`

**BoqDetail:** BoqSummary + `{ adjustmentType, adjustmentValue, lines: [{ id, position, code, description, quantity, wasteRate, finalQuantity, unit, rate, totalUsd }], subtotalUsd, adjustmentUsd, supervisionRate, supervisionUsd, tvaRate, tvaUsd, clientTotalUsd, deliveries: [{ id, channel, recipient, note, totalUsd, sentAt, sentByName }] }`

### PDF (client BOQ and VO)
- **Header:** company header like the other documents, client and project, number, date and title. A VO also shows its reason.
- **Lines:** code, description, quantity (finalQuantity), unit, rate and total. Never show the exact quantity or waste.
- **Totals:** subtotal, adjustment (if any), total, supervision (x%), TVA (y%), total to pay.
- **Approach:** reuse the existing document/PDF pattern (see how the statement and quote PDFs are built) and the document email pattern.

### Approved DB changes (phase 2)
- New tables `boqs`, `boq_lines` and `boq_deliveries`.
- Rename `statement_sequences` → `boq_sequences`, keyed by prefix + year.
- Drop `client_statements`, `client_statement_lines` and `client_statement_deliveries`, plus statement-only columns on `project_materials`.
- Permission data: rename `statement:create` → `boq:create` (keeping grants) and remove `statement:send`.

### Notes from phase 1 for phase 2
- **Test helper:** replace the test helper `ProjectTestSteps.SetWorkPriceAsync` (SQL) with real BOQ agreement where tests need a priced site, or keep it as a fast path. Either way, agreeing must write the same `project_fee_set` event.
- **Dead quote text:** remove the Execution "materials billed separately" text in QuotePdf/QuoteEmail.
- **TVA base:** once statements go, the TVA base is work only, with no sent materials. `revenue` = work + supervision, and `revenueWithTva` = revenue + work × tvaRate.
- **Client cleanup:**
  - remove the statements UI (`src/domains/execution/statements`);
  - remove the material "stated"/sent badges;
  - remove the old agreed-amount wording in `explainedMoney.ts` and `CompleteProjectModal`;
  - remove the statement help articles;
  - remove the `?quote=new` link in `notificationWords.ts` for Execution.

### Phase 2 decisions made on the desktop (2026-10-29)
These settle what the contract above left open. **Where they differ from the table or shapes above, these win.**

**Permissions (permissions, never roles: CLAUDE.md §0).** "Super admin only" means a permission super admins hold by default, as phase 1 did with `company_settings:manage` for account labels.
- **Client BOQs and VOs**, every write (create, edit, copy lines into, duplicate, delete, email, record a delivery, agree, withdraw): `project:pricing_manage`.
- **Internal drafts:** `boq:create`. Its author sees and edits their own; holders of `project:pricing_manage` see every internal draft (read, copy from, use as `fromBoqId`) and may edit or delete any.
- **Reading** client BOQs, VOs, the price summary and the PDF: `financial:view` or `project:pricing_manage`.
- `boq:create` keeps `statement:create`'s grants. It stays a super-admin-only default for now; a super admin can grant it to an admin per person. (Open question for the user: should admins hold it by default?)
- BOQs exist only on Execution client projects the caller can see. A project the caller can't see is `404`; any other project is `422 boq_execution_only`.
- BOQ writes need the project open (not completed or archived), with the error code the API already uses for writes to a closed site.

**List response** (`GET /api/projects/{id}/boqs`): `internal` holds the caller's own drafts (all of them for `project:pricing_manage`). `client`, `vos` and `price` are empty / `null` without `financial:view` or `project:pricing_manage`. Newest first in each list. Needs any of `boq:create`, `financial:view`, `project:pricing_manage` (`403` otherwise).

**Profit floor: no `force` field.** Agree and withdraw go through `IProfitFloor` (`BeforeAsync` before, `CheckAsync` after) like every other guarded change. New `BlockedChangeKind` values `BoqAgree` and `BoqWithdraw` (`boq_agree`, `boq_withdraw`), replayed by `BlockedChangeService`, with a description line like the others. The migration updates `ck_blocked_changes_kind` to match (implied by "exactly like lowering the fee did"). A refusal is the usual `422 profit_floor` with `blockedChangeId`; the client shows the existing force / dismiss UI. Agree has no body; withdraw takes `{ reason }`.

**Work price.** Agreeing or withdrawing recomputes `Project.ProjectFee` and writes what a fee change used to write (the `project_fee_set` event, and a payment-change history row if the old fee edit wrote one; look at the fee path in `git show origin/master:ID-Indetails.api/Services/Projects/Implementation/ProjectService.cs`). The BOQ number is the reason on agreement; the given reason on withdrawal.

**Storage.**
- `boqs`: project, kind, status, number (unique, null for internal), title (≤ 120), reason (≤ 500, VOs only), author, adjustment type and value, `subtotal_usd` and `total_usd` (recomputed by `BoqRules` on every save, never taken from the client), created / updated / agreed at, agreed by, `deleted_at` (drafts are soft-deleted so a sent draft's deliveries stay on record).
- `boq_lines`: position, code (≤ 20), description (≤ 500), quantity decimal(14,4) ≥ 0, waste rate decimal(6,4) between 0 and 1, unit (≤ 20), rate decimal(14,4). Final quantity and line total come from `BoqRules`, not stored. At most 1000 lines.
- `boq_deliveries`: channel (the existing `DeliveryChannel`), recipient, note, `total_usd` at send time, sent at, sent by.
- Titles default to "Client BOQ", "Internal draft" or "Variation order" when not given on create; `PUT` requires one.
- Adjustment: `percent` between -0.99 and 1; `total` ≥ 0 except on a VO. A negative rate on a non-VO is `422 boq_negative_rate`.

**Behaviour.**
- Sending (email or a recorded delivery) works on drafts and agreed BOQs, never internal ones (`boq_internal`) or empty ones (`boq_empty`).
- Agreeing a VO needs an agreed client BOQ (`boq_not_agreed_yet`), even for a VO made before a withdrawal.
- Duplicate copies title (+ " (copy)"), reason, adjustment and lines into a new draft of the same kind, with a new number.
- Activity events (`boq_created`, `boq_sent`, `boq_agreed`, `boq_agreement_withdrawn`, the kind and number in the payload, the total in the amount column) only for client BOQs and VOs. Internal drafts are private working papers and raise no events and no notifications.
- No notifications for BOQs (no portal yet).
- Proton: no BOQ actions yet. Every BOQ write endpoint gets a `Never(...)` entry in `ProtonCoverage` ("priced line by line in the BOQ editor"); statement actions, tools and surfaces go.

**Money response after phase 2.** `ProjectMoneyResponse` loses `materialCharge`, `unsentMaterialCharge`, `unsentMaterials` (and any other material-revenue field). `revenue` = work + supervision; `revenueWithTva` = revenue + work × tvaRate. `materialMarkup` stays until phase 3.

**Response shapes (C# record names fix the client's `Schemas[...]` names).**
- `BoqSummaryResponse`: `{ id, kind, number, title, author: PersonRef, status, totalUsd, lineCount, reason, createdAt, updatedAt, agreedAt, agreedBy: PersonRef | null, lastDelivery: BoqLastDeliveryResponse | null }`
- `BoqLastDeliveryResponse`: `{ channel, sentAt }`
- `BoqDetailResponse`: every `BoqSummaryResponse` field + `{ projectId, adjustmentType, adjustmentValue, lines: BoqLineResponse[], subtotalUsd, adjustmentUsd, supervisionRate, supervisionUsd, tvaRate, tvaUsd, clientTotalUsd, deliveries: BoqDeliveryResponse[] }`
- `BoqLineResponse`: `{ id, position, code, description, quantity, wasteRate, finalQuantity, unit, rate, totalUsd }`
- `BoqDeliveryResponse`: `{ id, channel, recipient, note, totalUsd, sentAt, sentBy: PersonRef | null }`
- `BoqListResponse`: `{ internal, client, vos: BoqSummaryResponse[], price: BoqPriceResponse | null }`
- `BoqPriceResponse`: `{ agreedBoqUsd, agreedVosUsd, workUsd, supervisionRate, supervisionUsd, tvaRate, tvaUsd, clientTotalUsd }`
- Requests: `CreateBoqRequest { kind, title?, reason?, fromBoqId? }`, `SaveBoqRequest { title, reason?, adjustmentType, adjustmentValue?, lines: SaveBoqLineRequest[] }`, `SaveBoqLineRequest { id?, code?, description, quantity, wasteRate, unit?, rate }`, `CopyBoqLinesRequest { fromBoqId, lineIds? }`, `WithdrawBoqAgreementRequest { reason }`, `EmailBoqRequest { to, note? }`, `RecordBoqDeliveryRequest { channel, recipient?, note? }`.
- Enums in JSON: kind `internal | client | vo`, status `draft | agreed`, adjustmentType `none | percent | total`, channel `email | whatsapp | download`.

**Client.** The API does every calculation: the editor shows final quantities and totals from the last saved `BoqDetailResponse`, and a line changed since then shows "—" until it's saved.

**Pinned counts after phase 2:** tables stay 96 (three statement tables out, three BOQ tables in, the sequence table renamed); the permission count drops by one (`statement:send`); the Proton coverage counts move with the new `Never` entries and the removed statement actions.

### Phase 2 as built (API, 2026-10-29)
What the API does where the contract above was silent or differs. The client builds against this.

**Permissions per endpoint** (the controller checks one of these, then the service checks the BOQ's kind):
- `GET /api/projects/{id}/boqs`, `GET /api/boqs/{id}`: any of `boq:create`, `financial:view`, `project:pricing_manage`.
- `POST /api/projects/{id}/boqs`, `PUT /api/boqs/{id}`, `POST …/copy-lines`, `POST …/duplicate`, `DELETE /api/boqs/{id}`: `boq:create` or `project:pricing_manage`. Then: an internal draft needs `boq:create` to be created or duplicated, and is written by its author or any `project:pricing_manage` holder; a client BOQ or VO needs `project:pricing_manage`.
- `POST …/agree`, `POST …/withdraw-agreement`, `POST …/deliveries`, `POST …/email`: `project:pricing_manage`.
- `GET …/pdf`: `financial:view` or `project:pricing_manage`.

**Status codes.** Create and duplicate `201`; delete `204`; everything else `200`. Another author's internal draft is `404 not_found`; a client BOQ or VO the caller may not read is `403 forbidden`.

**Errors** (all `422` unless noted): `boq_execution_only`, `boq_locked` (also a second agree of an agreed BOQ), `boq_already_agreed`, `boq_not_agreed_yet`, `boq_has_agreed_vos`, `boq_empty`, `boq_internal`, `boq_negative_rate`, plus two the contract didn't name: **`boq_not_agreed`** (withdrawing a BOQ that is a draft) and **`boq_price_below_zero`** (agreeing or withdrawing a VO that would take the work price below nothing). A closed site is `project_locked`. Validation is `400 validation_failed` with the field, including `reason` (required on a VO, refused on any other kind, also on save), `adjustmentValue`, `lines`, `lines[i].description|quantity|wasteRate|rate`, `lineIds`. A line id that isn't the BOQ's, or a copy of a line the source doesn't have, is `400` too. A source BOQ of another project or one the caller can't read is `404`.

**Behaviour the contract left open**
- Every write needs the project open, including recording a delivery and emailing (the email is refused before anything is sent). Reading and the PDF work on a finished site.
- `BoqDetailResponse.deliveries` and every list are newest first. A VO's `reason` is on the summary too.
- A duplicate of a VO needs the client BOQ agreed (`boq_not_agreed_yet`), like making one. Duplicating or creating a client BOQ never needs one to be agreed.
- The PDF date is the day an agreed BOQ was agreed, or today for a draft; a draft is stamped DRAFT.
- Agreeing sets the payment-history `description` to the BOQ's number, withdrawing to the reason given. The row carries the unchanged markup, TVA and supervision rates.
- The profit floor judges only a lower price (a negative VO, a withdrawal): a rise, the first agreement included, always passes (corrected by the desktop session; the plan says a rise never needs force). Withdrawing the whole client price always needs forcing (the profit is 0 or below once nothing is agreed). `BlockedChangeResponse.summary` reads "Agreeing BQ-2026-001" or "Withdrawing the agreement of BQ-2026-001: <reason>".
- Finalising a site no longer checks anything about materials: no `materials_not_sent`, and no `exchange_rate_missing` (the agreed amount needs no rate).

**Removed from the API:** the statement endpoints; notification kind `statement_sent` (and its push target); activity kinds `statement_created`, `statement_sent`; error codes `statement_empty`, `material_stated`, `materials_not_sent`; permission `statement:send` (and `statement:create` became `boq:create`); Proton's statement tools and the `new_statement` surface. `MaterialResponse` lost `isStated` and `sentOn`, and `priceSource` is `auto` or `manual`. `ProjectMoneyResponse` lost `materialCharge`, `unsentMaterialCharge`, `unsentMaterials`, `materialProfit` and `unconvertedMaterials`.

**Added beside the BOQs:** `ProjectPaymentChangeResponse.supervisionRate` and `previousSupervisionRate` (decimal fractions, null on Design rows and on rows from before supervision), filled like `tvaRate` and `previousTvaRate`.

**Pinned counts after phase 2:** tables 96, permissions 61, Proton coverage Action 87, Open 25, ReadOnly 2, Never 100, notification kinds 46.

### Phase 3 as built (API, 2026-10-29)
What the API does where the decisions above were silent or differ. The client builds against this.

**Routes and permissions** (the controller is `SiteExpensesController`; each route also answers under `/api/projects/{projectId}/materials`, the phone's old path, with the same shapes, until the phone moves):
- `GET /api/projects/{id}/site-expenses`: `project:view`. `POST` (a batch, `201`): `material:create`. `PUT /{expenseId}` and `DELETE /{expenseId}` (`204`): `material:create` (own) or `material:manage` (anyone's). `PUT …/{id}/client-price` is gone (`404`).
- `POST /api/projects/{id}/expenses` on an Execution project: `422 expense_execution_uses_site_expenses`. Design project expenses are unchanged.

**Requests** (`NewSiteExpenseRequest`, `SiteExpenseEntryRequest` for `PUT`; `name` and `supplier` keep their names): `name`, `quantity`, `unit`, `unitCost`, `amount`, `supplier`, `note` as before, plus optional `currency` (`USD` or `LBP`), `spentOn` (date), `category` (`materials | contractor | equipment | fees | other`) and `accountLabelId`; `clientRef` on a batch item only. Left out on a batch item: USD, today (company time zone), `materials`, no label. Left out on a `PUT`: the expense keeps its currency, day and category, but its label is set to what the request says (none when left out). An unknown label id is `400 validation_failed` naming `items[i].accountLabelId` (or `accountLabelId` on a `PUT`); an archived label that isn't the expense's own is `422 account_label_archived`. A stray `clientPrice` (the phone's older builds) is ignored.

**Responses.** `SiteExpenseResponse`: `id, name, quantity, unit, unitCost, cost, currency, supplier, note, spentOn, category, accountLabel { id, name } | null, addedAt, addedOn, addedBy, updatedAt, canEdit` (no `clientPrice`, `priceSource`, `markup`). `SiteExpenseListResponse`: `items, page, pageSize, totalCount, matching, all, types, suppliers, addedBy, canAdd` (no `defaultMarkup`). Totals are `SiteExpenseTotalsResponse { count, cost, byCurrency[] }` (no `charge`): `cost` is the total of the USD rows, `byCurrency` is `[{ currency, total }]` for every currency, since LBP and USD are never added together (the same for `types[].cost`). Filters: `type`, `supplier`, `addedById`, `category`, `accountLabelId`, `from`/`to` (the day **spent**), `search`, `page`, `pageSize`. The list stays ordered by when the expense was recorded, newest first.

**Pricing and money.**
- `PUT /api/projects/{id}/pricing` takes `{ supervisionRate?, tvaRate?, description }` (`projectFee` is still refused). A change that changes neither rate is `400 validation_failed` with the key **`supervisionRate`** (the first of the two). Create no longer takes `materialMarkup` (ignored if sent); `supervisionRate` on create still needs `project:pricing_manage`.
- `ProjectMoneyResponse` loses `materialMarkup`. `ProjectCostResponse.materials` is now `siteExpenses`; `UnconvertedCostResponse.source` is `site_expense` for them. The period and company responses (`PeriodCostResponse`, the monthly rows, the company overview, `site-summary`, `me/site-records`) keep their JSON name `materials` for the same cost. The Financials CSV column is headed "Site expenses".
- `ProjectPaymentChangeResponse.materialMarkup` and `previousMaterialMarkup` stay (null on rows written from now on).
- A site expense's cost is dated by `spentOn` for exchange rates, so the cost-by-week figures and the project cost follow it.

**Stored kinds kept.** Activity kinds `material_added`, `material_updated`, `material_removed` (subject type `material`), notification kind `materials_added` (push target section `materials`), blocked-change kinds `material_add` and `material_update`. `material_markup_set` and `material_priced` are no longer written; older rows stay in the feed (still grouped as money, payloads still hidden without `financial:view`). New texts say "expense": the push ("Expenses added"), the blocked change summaries ("1 expense: Crane hire", "Expense change: Crane hire") and the permission explanations.

**Migration `SiteExpenses`.** `project_materials` is rebuilt as `site_expenses` (rows keep their ids); `spent_on` is the day each was added in the company time zone; Execution `project_expenses` rows with an amount above zero move over (mapping as above; no author becomes the project owner) and are deleted from `project_expenses`; `projects.material_markup` and `ck_projects_material_markup_range` are dropped, and `ck_projects_execution_money_fields` (the plan called it `ck_projects_execution_pricing`) is rewritten without the markup. Table count stays 96.

**Proton.** `propose_add_materials` and `propose_material_step` became `propose_add_site_expenses` and `propose_site_expense_step` (add, update, delete: no price step). `propose_project_money` is Design revenue only; `Projects.UpdatePricing` is a `Never` entry and `SiteExpenses.Add/Update/Delete` replace `Materials.*` in `ProtonCoverage`. `propose_add_expense` is for Design only and refuses a site. The materials sheet form (`ProtonForm.Materials`) keeps its name and fields. **Pinned counts after phase 3:** tables 96, permissions 61, Proton coverage Action 85, Open 25, ReadOnly 2, Never 101, Prefill 7, Own 11, notification kinds 46.

**Load-test tool and k6 scripts** use `/site-expenses`, the new request and cost-source names, and no markup; the site invoices are a quarter of the fee.

### Phase 4 as built (API, 2026-10-29)
What the API does where the decisions above were silent or differ. The client builds against this. Migration `AccountFigures` (additive: `payments.account_label_id` and the `supervision_withdrawals` table; nothing is backfilled).

**Payment labels**
- `POST /api/projects/{id}/payments` takes an optional `accountLabelId`. Every payment response (`PaymentResponse`: record, get, list, apply, void) gains `accountLabel: { id, name } | null`.
- **Changing a label after recording** (the decision text says "editing", but payments are never edited): `PUT /api/payments/{id}/label` `{ accountLabelId }` (`payment:record`) sets, changes or clears (`null` or left out) the label of a live payment and answers the `PaymentResponse` (`200`). Nothing else about a payment can change.
- Errors: a label that doesn't exist or an id of 0 or less is `400 validation_failed` with the key `accountLabelId`; an archived label that the payment doesn't already have is `422 account_label_archived`; a voided payment is `422 payment_already_voided`; a payment of a domain the caller can't see is `404`; no `payment:record` is `403`. Setting the label it already has is a `200` with no event.
- Event `payment_label_set` (billing group): payload `{ label, previousLabel }`, the names or `null`.

**Supervision taken**
- `GET /api/projects/{id}/supervision-withdrawals` (`financial:view`) returns `SupervisionWithdrawalResponse[]`, the latest `takenOn` first (the one recorded last first on the same day); removed ones are left out.
- `POST /api/projects/{id}/supervision-withdrawals` (`payment:record`) `{ amountUsd, takenOn, note? }` returns `201` with a `SupervisionWithdrawalResponse`.
- `DELETE /api/supervision-withdrawals/{id}` (`payment:record`) returns `204`; soft delete, a second call is `404`.
- `SupervisionWithdrawalResponse`: `{ id, amountUsd, takenOn, note, createdBy: { id, name } | null, createdAt }` (`takenOn` is `yyyy-MM-dd`; `createdBy` is always set today).
- Errors: `422 not_for_this_domain` for a project that is not an Execution client site, `404` for a project or withdrawal in a domain the caller can't see, (a completed or archived site still takes and removes them: the user's call, as nothing frozen moves), `400 taken_date_in_future` (key `takenOn`) for a day after today in the company time zone, `400 validation_failed` for an amount of 0 or less, with more than 2 decimals or above 999,999,999,999.99 (key `amountUsd`), a missing or out-of-range day (2000-2100, key `takenOn`) or a note over 500 characters (key `note`). Reads and writes are separate permissions: `payment:record` alone cannot read the list, and `financial:view` alone cannot write.
- Not a cost: no profit floor, no change to `money.profit`, `money.cost` or `money.cashProfit`. More than is due is accepted.
- Events: `supervision_taken` (payload `{ takenOn, note }`) and `supervision_withdrawal_removed` (payload `{ takenOn }`), money group, subject type `supervision_withdrawal`, USD in the amount column. The feed hides the amount and the payload without `financial:view`.

**Account figures** `GET /api/projects/{id}/account-figures` (`financial:view`; `422 not_for_this_domain` on a Design project; `404` for a domain the caller can't see) returns an `AccountFiguresResponse`, all USD:
```
{ workUsd, supervisionRate, supervisionUsd, tvaRate, tvaUsd, clientTotalUsd,
  receivedUsd, restToPayUsd,
  costs: { siteExpensesUsd, groundLabourUsd, staffUsd, otherUsd, totalUsd },
  profitUsd,
  supervision: { dueUsd, takenUsd, leftUsd },
  pocket: { movedToPocketUsd, paidByPocketUsd },
  cashInAccountUsd,
  byLabel: [{ accountLabel: { id, name } | null, receivedUsd, spentUsd, otherUsd, restUsd }],
  byCategory: [{ category, spentUsd }],
  byPayee: [{ payee: string | null, spentUsd }],
  unconverted: [{ source, currency, amount }] }
```
- Records (C# names fix the client's `Schemas[...]`): `AccountFiguresResponse`, `AccountCostsResponse`, `AccountSupervisionResponse`, `AccountPocketResponse`, `AccountLabelRowResponse`, `AccountCategoryRowResponse`, `AccountPayeeRowResponse`, `UnconvertedCostResponse` (already in the project money), `AccountLabelRefResponse` (`{ id, name }`), `SupervisionWithdrawalResponse`. Enums in JSON: `category` is `materials | contractor | equipment | fees | other`; `unconverted[].source` is a cost source such as `site_expense` (the same as `money.cost.unconverted`).
- `workUsd` is the work price; `supervisionUsd` = work x `supervisionRate`; `tvaUsd` = work x `tvaRate`; `clientTotalUsd` = work + supervision + TVA (= `money.revenueWithTva`). `receivedUsd` counts live payments with their TVA; `restToPayUsd` = client total - received and is **negative when the client paid ahead**.
- `costs` are `money.cost`: `staffUsd` is its `labour`, `otherUsd` its `expenses` (0 on a site). `profitUsd` is `money.profit` (pocket money counted). `supervision.dueUsd` = `supervisionUsd`, `leftUsd` = due - taken, **negative when over-taken**.
- `cashInAccountUsd` = received - `costs.totalUsd` - supervision taken - moved to the pocket + paid by it. It can be negative.
- `byLabel`: one row per label with a live payment or a site expense (archived labels included, by name), in the labels' order (the Settings order), then **always** the unlabelled row last (`accountLabel: null`), even when it is all zeros. A label row: `receivedUsd` = its payments, `spentUsd` = its site expenses, `otherUsd` = 0. The unlabelled row: `receivedUsd` = payments with no label, `spentUsd` = every cost no label holds (unlabelled site expenses, ground labour, staff, other), `otherUsd` = supervision taken + moved to the pocket - paid by it. `restUsd` = received - spent - other. **The rows' `restUsd` always add up to `cashInAccountUsd`** (and their `receivedUsd` to `receivedUsd`, their `spentUsd` to `costs.totalUsd`).
- `byCategory` and `byPayee`: site expenses with a rate on their day, largest first (ties: category order above; payee by name, then "no payee"). Only categories and payees with spend appear. A payee groups suppliers by their text trimmed, with single spaces, in lower case, shown as last typed; `payee: null` is expenses with no supplier (the client shows "No payee").
- `unconverted`: costs with no exchange rate on their day, in their own currency, equal to `money.cost.unconverted`. They are left out of every sum, including `byCategory`, `byPayee` and the label rows.

**Decisions where the plan was silent**
- Payments have no edit endpoint, so "editing" a payment's label is `PUT /api/payments/{id}/label`. A relabel writes an event; a no-op writes nothing; a voided payment is refused.
- The withdrawal list is a plain array (no wrapper, no totals); the totals are in the account figures.
- `not_for_this_domain` (not a new code) refuses supervision taken and account figures on non-Execution projects; the one new error code is `taken_date_in_future`.
- `byLabel` always ends with the unlabelled row; received comes from the payments themselves (so the rows add up by construction), and the unlabelled row's cost is the project's total cost less the labelled site expenses.
- Supervision events carry the day and the note in the payload, hidden without `financial:view`.
- The relabel endpoint and the two supervision endpoints are `Never` entries in the Proton coverage; `Payments.Record` (an `Action`) keeps its tool and does not offer a label yet.

**Not built:** the client payment report PDF, which waits for the user to agree its layout.

**Pinned counts after phase 4:** tables 97, permissions 61 (no new permission), Proton coverage Action 85, Open 25, ReadOnly 2, Never 104, Prefill 7, Own 11, notification kinds 46.
