# Proton 2.0: Financial Intelligence Engine (plan)

**Status:** approved by the owner on 9 November 2026: the decisions in [§3](#3-decisions-that-shape-the-plan), the
schema changes in [§6](#6-phase-2-data-foundations) and phase 5, and the delivery order in [§10](#10-delivery-order-prs).
Built so far: phase 2h (PRs #195 / client #199), 1a-1 to 1a-4 and 1a-6 (#198 / client #201), and 1a-5 with the daily
USD/LBP rate feed (#199 / client #204), a project's profitability and scenarios (PR 3, #202 / client #206), and a
domain's project risks and spending trends (PR 4, #203 / client #208). Each migration still shows its tables and risks in its own PR (CLAUDE.md rule 9).

**Built on** [proton-financial-intelligence-discovery.md](proton-financial-intelligence-discovery.md) (PR #194). File
references point to that report's sections ("D§9" = discovery report, section 9) or to code paths under
`ID-Indetails.api/`.

**Repositories:** the API (this repo) and the client (`Ident-Internal-Management-System-client`). Work happens on
`MR-<Topic>` branches, one PR per step, with migrations stacked when they build on each other.

---

## 1. Goal and what "done" means

Proton should be able to answer, with every number calculated in code and every judgement backed by a stated rule:

- **"How healthy is a project?"** Profit, margin and cash, where the margin comes from, and what threatens it.
- **"How healthy is the business?"** Each domain and the company, compared with earlier periods.
- **"What will it end at?"** A project's forecast cost and profit, as a range, with the method stated.
- **"What looks wrong?"** Unusual spending and projects drifting towards a loss.
- **"Where does time go?"** Labour cost and utilization, never one person's pay.
- **"What if…?"** A change simulated on one project, without writing anything.
- **"Tell me before I ask."** Risks raised proactively, to the right people, without amounts in notifications.

**Done** for each capability means:
- a GET endpoint the client can show;
- a Proton read tool standing in for it;
- unit tests on its rules at their thresholds;
- integration tests for who may and may not see it;
- an eval case;
- the area's doc updated.

## 2. Principles

1. **One calculator.**
   - Every figure comes from `IProjectFinanceService`, `IDomainFinanceService`, `MoneyRollupService` and the pure rules they use (D§9).
   - New logic is *built on* their outputs, never a second formula.
   - Where a parallel path already exists (the company overview, department labour), it is folded in **before** new figures are added on top.
2. **The API does every calculation.**
   - Each capability is an API endpoint first; the Proton tool mirrors it.
   - The client shows what the API returns and computes nothing.
3. **Authorization is the endpoint's.**
   - `[HasPermission]` on the controller, service re-checks (`actor.Require`, `CanSee`, money left out rather than zeroed), and domain filters written by hand (there are no global query filters, D§10.2).
   - The Proton tool inherits the endpoint's policy through `ProtonEndpoint.Of`.
4. **Never a person beside money.**
   - No output places a named person next to a cost, rate, salary or derived figure.
   - Utilization is hours only.
   - A catalogue test pins this for every new output type, extending `No_row_about_one_person_carries_money` (PR #193).
5. **Show the working.** Every result carries a `basis`:
   - the method;
   - the period;
   - what it was compared with;
   - assumptions;
   - what was left out and why (no permission, no data, unconverted currency).
6. **Judgements need a rule.**
   - "Unusual", "at risk" and "over budget" only when a named threshold fired.
   - Thresholds live in configuration (`Proton:Insights`, extended), and the owner approves the defaults.
7. **Read-only and cheap.**
   - No new tool writes anything.
   - Anything that loads every project is `Heavy`.
   - Phase 2 adds snapshots so portfolio views stop recomputing all history.
8. **Domains differ.** Design and Execution get separate forecasting methods (D§8). There is no single formula for both.

---

## 3. Decisions that shape the plan

These are the discovery report's owner questions (D, "Questions for the Business Owner"), the owner's answers (9 Nov) and
what each one blocks.

| # | Question | Answer | Blocks |
|---|---|---|---|
| Q1 | What "project profitability" means; whether overhead is allocated | **Decided (9 Nov):** project profit = agreed revenue − project costs, nothing else. Overhead is counted at company level only, never on projects. A site's pocket money stays in its profit, as today (moved to the pocket lowers it, paid by the pocket raises it) | phases 1–3 wording and ranking |
| Q2 | Execution staff salaries (in no profit figure today) | **Decided (9 Nov), built in PRs #195 / client #199:** site staff are on a fixed salary like admins and super admins. "Fixed salaries" = everyone on a pure monthly salary who logs no hours, taken off the company's profit in the overview, never off projects | done |
| Q3 | Design salaried people's paid, unlogged time | **Decided (9 Nov), built in the same PRs:** paid leave comes off the company's net profit as its own line in the overview | done |
| Q4 | Admins' project time and the threshold overtime premium | **Decided (9 Nov), built in the same PRs:** admin time stays at 0 on projects (it's a fixed salary). The overtime past a salary's threshold that Salaries pays above the hourly rate comes off the company's net profit as its own "overtime premium" line | done |
| Q5 | Internal projects | **Decided (9 Nov):** cost centres only. Their costs count in company and domain totals; they stay out of margin rankings, risk-of-loss signals and comparisons | phase 1 rankings |
| Q6 | Currencies for forecasts; LBP Design expenses with no rate; where rates come from | **Decided (9 Nov):** money not yet spent or received is forecast at **today's rate**, and the answer names it. A Design expense in LBP with no rate for its day is **refused**, as site expenses are (`exchange_rate_missing`). A **public rate feed adds the day's USD/LBP rate automatically** each morning unless a super admin entered one that day (10% guard, 10 Nov; outside Proton: the implementation session builds it) | phase 1a, phase 3 |
| Q7 | How progress is measured | **Decided (9 Nov):** Design: **hours logged ÷ planned hours** per department, where planned hours exist; requirements otherwise. Execution: **value executed ÷ agreed BOQ value**, from executed quantities per BOQ line that site staff record (new data, later phase) | phase 2b, 2c, phase 3 |
| Q8 | Budgets: who sets and approves them | **Decided (9 Nov):** a **cost budget per project**, per department on Design and per category on Execution, set by whoever holds `financial:write` (Design) or `project:pricing_manage` (Execution). A revision needs a reason and is kept | phase 2a, phase 3 variance |
| Q9 | Committed costs | **Decided (9 Nov):** record **commitments** (supplier, amount, currency, expected date, status: open, received, cancelled) on Execution sites first. Open ones count in the forecast cost and the cash-flow | phase 2d, phase 4 |
| Q10 | Settlements that take profit to zero or below | **Decided (9 Nov):** completing with "excused" or "written off" stays as it is (outside the floor). FI lists a settlement that takes profit to 0 or below as a **risk**, and in the monthly review, with how much profit it took. **Read (10 Nov):** the risks list settlements on projects completed in the last 12 months | phase 1b risks |
| Q11 | Expected revenue with today's supervision rate | **Decided (9 Nov):** past snapshots use the **rate in force on that date**, from the payment history (`ProjectPaymentChange`); today's figures use today's rate | phase 2g |
| Q12 | Who sees Financial Intelligence | **Decided (9 Nov):** money insights to **`financial:view`** (super admins by default). Utilization hours to `report:view` (admins too). Employees: none. **No new permission**. **11 Nov:** `report:view` holders see each Design employee's logged, expected and excused hours on utilization, with no pay figure; the hours ledger (balance, shortfall, made-up hours) stays `salary:view` | every endpoint |
| Q13 | Proactive alerts | **Decided (9 Nov):** in-app and push to `financial:view` holders: a **weekly digest every Monday plus immediate alerts** when a project hits the profit floor or a change is blocked. Push text uses percentages, never amounts | phase 5 |
| Q14 | Payroll in company and cash analyses | **Decided (9 Nov):** **never to Proton** (as today). The cash-flow page shows payroll as one monthly total to `salary:view` holders only; Proton's forecasts say "excluding payroll" | phase 4 |
| Q15 | "Similar projects" | **Decided (9 Nov):** projects get two **optional** fields, a **type** (villa, apartment, commercial, interior, ...) and an **area in m²**, set on the project form and editable any time. FI compares a project with past ones of the same type and size (margin, cost per m², duration) and helps price new ones; until they are filled in, it compares by domain, price band and duration | phase 2e, comparisons |
| Q16 | Unusual spending | **Decided (9 Nov):** flag when a category's month is **≥ 1.5× its 6-month average and ≥ 1,000 USD**, or an item's unit cost is **≥ 25% above its last 3 purchases**. **Read (10 Nov):** the month's total must reach 1,000 USD (not the rise); the unit cost is compared with the average of the item's last 3 purchases; a month with nothing counts as 0 in the 6-month average | phase 1b anomalies |
| Q17 | Cash-flow horizon | **Decided (9 Nov):** **13 weeks**, week by week; invoices by due date, payment plans by their dates, "left to invoice" only where a plan exists | phase 4 |
| Q18 | Scenario levers | **Decided (9 Nov):** all four: a VO amount, an extra cost by source, a supervision or TVA rate, and a settlement or a delay at the current weekly cost. Each shows the new revenue, profit, margin and whether the floor would block it; nothing is saved | phase 1b simulator |
| Q19 | The model without a person | **Decided (9 Nov):** **rules only** for scheduled checks, with fixed wording; the model runs only when someone asks | phase 5 |

---

## 4. Architecture

```text
Controllers/FinanceInsightsController.cs        GET /api/finance/insights/...   [HasPermission(financial:view)]
Controllers/ReportsController.cs (extended)     GET /api/reports/utilization     [HasPermission(report:view)]
        │
Services/Insights/{Abstraction,Implementation}  orchestration, authorization re-checks, domain filters
        │  reuses ─► IProjectFinanceService, IDomainFinanceService, MoneyRollupService, IReportService,
        │            IProjectDepartmentService, ProfitFloorRules, MoneyAttention, ProtonTrends, ProtonSignals
        │
Business/Finance/Intelligence/*Rules.cs         pure: forecast methods, variance, anomaly rules, risk rules,
                                                 scenario levers (no EF, no HTTP), unit-tested at thresholds
        │
DTO/Response/Insights/*                         typed responses (no dictionaries), money null without permission
        │
Proton/Tools/FinanceInsightTools.cs             one read tool per endpoint (ProtonEndpoint.Of), basis + datasets
Proton/Prompts/insights.md                      how to present forecasts, ranges, risks and simulations
```

- **Why a separate controller:** `FinanceController` serves the Financials page's established figures. Insights are derived judgements with their own thresholds and bases, so keeping them apart makes the "one calculator" rule easy to audit.
- **Simulations write nothing.** They take their levers as query parameters on a GET, so they need no `ProtonCoverage` entry.
- **Caching:** none across users. Per-request memoisation as `ProtonAnalysisService` does. Phase 2f snapshots replace the need for heavier caching.
- **Client:**
  - an "Insights" tab on Financials (domain level) and on a project's financials tab;
  - charts from the same DTOs;
  - Proton opens them through `open_page` / `open_record`, with new tab enum values.

---

## 5. Phase 1: on today's data (no schema changes)

### 1a. Consolidate the calculations first

Each item ships with regression tests that pin today's numbers before the change and prove they don't move (or prove the intended correction).

| Step | Change | Why | Size |
|---|---|---|---|
| 1a-1 | `CompanyOverviewService` period figures (received, cost, profit, invoiced, collected, outstanding) through `DomainFinanceService` / `PeriodMoney` / `ProjectBilling` | the only parallel calculator (D§9.2) | M |
| 1a-2 | One margin function (`ProjectFinance.MarginOf` used by `PeriodMoney`) | duplicated body | S |
| 1a-3 | Name the three profits apart in DTOs, prompts and docs: `agreed_profit`, `site_cash_profit` (with pocket), `period_cash_profit` (without pocket) | Proton and the docs call two different figures "cash profit" | S |
| 1a-4 | Department labour reports unconverted labour as unconverted, not 0 (`ProjectFinanceService.LabourByDepartmentAsync`) | silent loss | S |
| 1a-5 | Design expense refused without a rate for its day (Q6) | silent loss, matches site expenses | S |
| 1a-6 | The company monthly view subtracts unrounded parts (the known issue in `docs/status.md`) | rounding drift | S |

### 1b. Insights on today's data

| Capability | Endpoint | Proton tool | Rules (new, pure) | Reuses | Status after phase 1 |
|---|---|---|---|---|---|
| Project summary and profitability (PR 3) | `GET /api/finance/insights/projects/{id}` | `get_project_financial_summary` | margin bridge: base price, VOs, settlements, cost by source, labour share by department | `get_project_financials`, `CostHistoryAsync`, `ProfitFloorRules.StandingOf`, departments | READY |
| Project risks (PR 4) | `GET /api/finance/insights/{domain}/risks` | `detect_project_financial_risks` | profit-floor standing, cost growth against progress, department over budget, pending blocked changes, unconverted costs, short runway (10 Nov: a running site's cash profit already below 0, or, on an active site, out within 24 working days at today's crew and day rate on the site money timeline's projection), settlement at or below zero (Q10, completed in the last 12 months); ranked by kind, then size (`ProjectRiskRules.Rank`) | the shared calculator, `ProfitFloorRules`, the site money timeline's `LabourRules.ProjectZero`, departments, blocked changes | PARTIAL (no forecasts yet) |
| Expense trends and anomalies (PR 4) | `GET /api/finance/insights/{domain}/expenses` | `analyze_expense_trends` | category baselines and the Q16 thresholds; same amount twice (10 Nov: same project, item, amount and currency on the same day); unit-cost jumps per item name (normalised: trimmed, case-folded) | site and Design expenses; its own baseline (`ExpenseTrendRules`), as empty months count as 0 and `ProtonTrends.Baseline` needs every month above 0 | PARTIAL (three category schemes, free-text items) |
| Labour cost (Design) (PR 5) | `GET /api/finance/insights/design/labour?months=12` | `analyze_labor_costs` | totals and series by project, department, month; outside-scope cost and share (the worked time; paid breaks stay with the day); internal projects included; **no person money** (`LabourInsightRules`); Execution is `422 not_for_this_domain` | the shared calculator's batch `DepartmentLabourAsync` (one query per source) | READY |
| Utilization (Design, hours only) (PR 5) | `GET /api/reports/utilization?month=` | `analyze_employee_utilization` | logged, expected, excused, available (expected − excused), overtime as employees see it, utilization = logged ÷ available, project mix; by person and department; Design employees only (`UtilizationRules`) | the hours ledger (`SalaryCalculator.LedgerOf`) | PARTIAL (no non-project time) |
| Company performance | existing `get_company_overview`, `compare_money`, `get_money_by_month`, plus a trend block | — | — | after 1a-1 | READY once built (Q2–Q4 are in, PRs #195 / client #199) |
| Scenario on one project (PR 3) | `GET /api/finance/insights/projects/{id}/scenario?...` | `simulate_project_financial_scenario` | levers from Q18 fed to `ExecutionPricing.PriceOf`, `ProjectFinance.Summarise`, `Tva`, `ProfitFloorRules`; before and after | pure rules only | READY for the levers listed |

### 1c. Proton wiring for phase 1

- **Tools:** one per endpoint in `Proton/Tools/FinanceInsightTools.cs`. Each `Heavy` where it loads a domain, with a `basis` and datasets (bar for rankings, columns or line for series, kpis for summaries, table for risk registers).
- **Prompts:** `Proton/Prompts/insights.md`, covering:
  - present forecasts as ranges with their method;
  - never a judgement without its rule;
  - never infer one person's pay;
  - say what was left out.

  The new terms (bridge, standing, baseline) also go into `money.md`.
- **Tool search:** the new tools stay deferred (not in `ProtonToolSearch.Core`) except the summary.
- **Tests:**
  - catalogue tests (endpoint mirrors, no excluded fields, no person beside money);
  - integration tests per tool (a super admin sees it; an admin without `financial:view` is refused; another domain reads as not found);
  - eval cases added to `ProtonEvalTests`, run by hand only, as they cost money.
- **Docs:**
  - `docs/finance.md` (the new endpoints and every formula);
  - `docs/proton.md` (the tools);
  - CLAUDE.md "What to read" table, proposed for your approval.

---

## 6. Phase 2: data foundations

**Every step here is a schema change and needs your explicit approval first.** Each step comes with its migration, an entry screen or endpoint, Proton coverage for any new write endpoint (CLAUDE.md "changes that always come together"), and docs.

| Step | New data (proposed) | Entry | Unlocks | Depends on |
|---|---|---|---|---|
| 2a | **Budgets:** `project_budgets` (project, version, effective date, reason, set by), `project_budget_lines` (department on Design, `SiteExpenseCategory` or ground labour on Execution, amount USD) | budget window on the project's financials tab; `PUT …/budget` (new revision each time) | budget variance; budget-based forecasts | Q8 |
| 2b | **Planned hours** per department on a Design project (`project_department_plans`: department, hours, revision) | the departments window | Design progress by hours; Design forecasting | Q7 |
| 2c | **Execution progress:** `boq_line_progress` (BOQ line, date, executed quantity, by whom) | a "progress" column in the BOQ editor, or a weekly progress report beside labour weeks | Execution progress by value; Execution forecasting | Q7 |
| 2d | **Commitments:** `cost_commitments` (project, supplier, category, amount, currency, expected date, status open, part-paid, closed) linked to site expenses when paid | a commitments list on the site | cost at completion; cash-flow outflows | Q9 |
| 2e | **Project attributes:** `projects.project_type` and `projects.area_m2`, both optional and editable any time | the project form and its edit window | comparisons with similar projects | Q15 |
| 2f | **Monthly snapshots:** `project_money_snapshots` (project, month, revenue, cost by source, received, profit, progress), written by a monthly job and on finalising | none (job) | point-in-time comparisons; fast portfolio views | — |
| 2g | **History gaps:** labour-week day history (event payload with the replaced days), site-expense edit payload (old date, currency, category), a Design VO's previous TVA rate, expected revenue at the historical supervision rate | none | exact replay | Q11 |
| 2h | **Labour attribution:** site staff as fixed salaries, paid leave (Q2, Q3) and the threshold overtime premium (Q4) as company cost lines in the overview, **built in PRs #195 / client #199** | none | complete company cost | done |

**Order:**
1. 2h first: a formula change, but it moves existing numbers, so it gets its own PR and a heads-up.
2. Then 2a, 2b and 2c, the inputs every forecast needs.
3. Then 2d, 2f, 2e and 2g.

Migrations that build on each other are stacked.

---

## 7. Phase 3: forecasting and variance

Rules live in `Business/Finance/Intelligence/Forecast*.cs`, each method returning an estimate with its basis.

| Method | Formula | When it applies |
|---|---|---|
| Progress-based (exists) | cost ÷ progress | progress ≥ 5%; flagged weak when progress is requirement-based |
| Budget-based | cost so far + remaining budget (by line) | a budget exists (2a) |
| Burn-rate | cost so far + recent monthly burn × months to target date | a target date in the future; at least 3 months of history |
| Design by hours | cost so far + (planned − logged hours) × the department's recent cost per hour, as a department total, never a person | planned hours exist (2b) |
| Execution by value | cost so far ÷ (value executed ÷ BOQ value), plus open commitments | progress (2c), commitments (2d) |

**What the forecast returns:**
- each applicable method's estimate;
- a **range** (min and max of the applicable methods);
- a **preferred** estimate, chosen by a stated rule: the method with the strongest data;
- profit at completion = agreed revenue + approved VOs − cost estimate;
- a profit-floor outlook.

| Capability | Endpoint | Tool |
|---|---|---|
| Forecast final cost | `GET /api/finance/insights/projects/{id}/forecast` | `forecast_project_final_cost` |
| Forecast profit | the same response | `forecast_project_profit` (one tool, two views, or one tool) |
| Budget variance | `GET /api/finance/insights/projects/{id}/variance` and `…/{domain}/variance` | `analyze_budget_variance` |
| Risk detection v2 | phase 1b endpoint, now with "forecast below the floor" and "over budget at completion" | `detect_project_financial_risks` |
| Comparison with similar projects | `GET /api/finance/insights/projects/{id}/peers` | `compare_project_financial_performance` |

---

## 8. Phase 4: cash flow

`GET /api/finance/insights/cash-flow?horizon=13w` (`company:view` + `financial:view`):

- **Inflows:**
  - invoice balances by due date (overdue ones as "late", in their own line);
  - payment-plan drafts by their send dates;
  - nothing for amounts not yet invoiced unless a plan exists (Q17).
- **Outflows:**
  - overhead by month (dated amounts, `OverheadRules.AmountFor`);
  - open commitments by expected date (2d);
  - ground labour at the crew defaults for active sites;
  - Design labour at the recent run-rate as one total;
  - payroll as one total line for `salary:view` only, **never through Proton** (Q14).
- **Output:** weekly net and running balance from today. No opening bank balance exists in the app; the response says so, unless the owner wants one entered.

Tool: `forecast_cash_flow`. It returns no payroll line and states that it leaves it out.

---

## 9. Phase 5: proactive monitoring

- **Job:** `Jobs/FinanceWatchJob` on the `NotificationScheduler` pattern: a scoped run, rules only (Q19), no model call.
  - **Daily:** profit-floor breaches, forecast below the floor, overdue money past thresholds.
  - **Weekly digest:** the risk register's top items.
- **Findings:** stored in `finance_findings` (a schema change: project, kind, first seen, last seen, resolved). Used for de-duplication, for trend ("still at risk after 3 weeks") and for Proton (`get_finance_findings`).
- **Recipients:** `HoldersAsync(domain, financial:view)`, through the existing `INotificationSender` (in-app and push). New notification kinds carry **percentages or counts only, never amounts**, as `profit_low` does today.
- **Briefing:** Proton's briefing adds open findings for those who may see them.
- **Identity:** the job reads with an internal actor scoped to what each recipient may see. A finding is shown only to people who can see its domain and hold `financial:view`.

---

## 10. Delivery order (PRs)

| # | Branch | Contents | Needs |
|---|---|---|---|
| 1 | done: `MR-Finance-Consolidate` (#198 / client #201, merged) | 1a-1 to 1a-4, 1a-6, with regression tests | — |
| 2 | done: `MR-Design-Expense-Rate` (#199 / client #204, merged) | 1a-5, and the daily USD/LBP rate feed | Q6 |
| 3 | done: `MR-Insights-Project` (#202 / client #206, merged) | summary, profitability bridge, simulator; client tab; Proton tools | Q1, Q5, Q18 |
| 4 | done: `MR-Insights-Risks-Expenses` (#203 / client #208, merged) | risks v1, expense trends and anomalies | Q10, Q16 |
| 5 | this PR: `MR-Insights-Labour` | labour cost and utilization | Q12 |
| 6 | done: `MR-Company-Salary-Costs` (#195 / client #199, merged) | 2h | — |
| 7 | `MR-Budgets` → `MR-Planned-Hours` → `MR-Boq-Progress` | 2a, 2b, 2c, stacked migrations, entry screens | Q7, Q8, schema approval |
| 8 | `MR-Insights-Forecast` | phase 3 | 7 |
| 9 | `MR-Commitments`, `MR-Money-Snapshots`, `MR-Project-Attributes`, `MR-Finance-History` | 2d, 2f, 2e, 2g | Q9, Q11, Q15, schema approval |
| 10 | `MR-Insights-Cash-Flow` | phase 4 | 9, Q14, Q17 |
| 11 | `MR-Finance-Watch` | phase 5 | Q13, Q19, schema approval |

Each PR: the API and client together where the client shows it, tests, docs, Proton coverage and pinned counts updated where they change, built and tested locally on one core before one push.

---

## 11. Risks

| Risk | Mitigation |
|---|---|
| A second calculator drifts from the first | 1a before anything else; insights only consume calculator outputs; regression tests pin today's figures |
| Aggregation reveals what no page shows (a person's rate, admin salaries) | principle 4; a catalogue test per output type; net profit and salaries stay out of Proton |
| Budgets, planned hours and progress stay empty | entry screens in the same PRs; insights state "no budget" rather than guessing; risk signals for "no budget on an active project" (optional) |
| Portfolio views are slow on all history | heavy flags; snapshots (2f); the status.md loader fixes before portfolio forecasts |
| Forecasts read as promises | ranges, methods and data strength in every answer; prompts forbid presenting a forecast as a fact |
| Model cost and data sent to the provider | rules-only monitoring; insights computed in code, so the model only explains; the monthly budget unchanged |
| Single-instance assumptions (permission cache, holds) | unchanged by this plan; flagged if scaling out |

---

## 12. What I need from you to start

1. **Decisions:** all made on 9 Nov.
2. **The order in §10:** approved on 9 Nov.
3. **Schema approvals:** the phase 2 steps and phase 5's findings table were approved on 9 Nov. Each migration's tables, backfill and risks are still written in its PR, and no migration is applied to a database without being asked.
