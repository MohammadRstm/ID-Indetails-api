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
**Status:** in progress on the desktop. The full contract is in section 4, with the desktop's decisions at its end.

### Phase 3: cost-only expenses list (branch `MR-Site-Expenses`, off `MR-Boqs`)
**Status:** not started. Outline:
- **Data:**
  - Rename `project_materials` → `site_expenses`; the model and API routes say "expenses", and the client's Execution "Materials" panel becomes "Expenses".
  - Fields: item, quantity, unit, unit cost or amount, currency, supplier/payee, `spentOn` (date, required, default today), `category` (materials | contractor | equipment | fees | other) and optional `accountLabelId`.
  - Drop `client_price` and `projects.material_markup`, plus the markup/client-price logic, events, validators and UI. Check what `client_ref` is first: if it's an idempotency key, keep it.
- **Execution expenses:** the separate `ProjectExpense` "Add project expense" window is no longer offered for Execution projects. A migration moves any existing Execution `project_expenses` rows into `site_expenses`.
- **Profit floor:** the guard keeps covering expense writes.
- **Approved DB changes:** the rename, the added and dropped columns listed above, and that row move.

### Phase 4: payment labels, figures, supervision taken (branch `MR-Account-Figures`, off `MR-Site-Expenses`)
**Status:** not started. Outline:
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
