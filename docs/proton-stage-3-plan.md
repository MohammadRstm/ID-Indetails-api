# Proton — Stage 3 plan: company intelligence

Status: **approved and implemented** (see [proton.md](proton.md#analysis-stage-3)). All decisions in section N were approved as proposed.

Stage 1 lets Proton read authorized data, and Stage 2 lets it propose changes the user confirms.
Stage 3 makes it answer *analytical* questions, such as "how is Villa Mounir doing compared with last month?",
"what changed since last week?" or "what should I look at today?". It does this by combining authorized data,
with every number calculated in code and every interpretation labelled as one.

The design adds **six analytical tools** on top of the existing services, plus a small pure calculation module.
It needs **no new table, permission, dependency or infrastructure**. The decisions that do need approval are
listed in [section N](#n-decisions-needing-approval).

---

## A. Available analytical data (what can actually be measured)

| Area | Measurable | Where it comes from | Limits |
|---|---|---|---|
| Design time | Minutes, entries and people per day, project, person and client; overtime; last work per project | `WorkEntry` via `IReportService` and `IWorkInsightsService` | **Design only.** Execution logs no hours. |
| Projects | Status, progress %, start date, target date, completed-at, held-since, owner, client, billing type | `Project`, `ReportRepository.HeldSinceAsync` | No members (the team is the people who logged work); no estimated hours, so **there is no "expected vs actual hours"**. |
| Overdue | Target date before today, status Active or Hold | `Business/Projects/ProjectOverdue` (Stage 1, approved) | — |
| Requirements | Open, done, deadline, overdue, due in 14 days | `RequirementRules`, `IDashboardService.DeadlinesAsync` | — |
| Money (`financial:view`) | Received, expected revenue, cost by source (labour, expenses, materials, ground labour), profit, margin — for any period, per domain; all-time per project; billing, aging, money attention | `PeriodMoney`, `ProjectFinance`, `MoneyRollup`, `IDomainFinanceService` | Per-project figures for a period aren't exposed by any service yet (gap 1). |
| "Expected" money | Agreed payment vs cost (`CostHigh` at 85%), recognised-to-date, projected margin | `MoneyAttention`, `FinanceProjectResponse.Projected` | The only real expected-vs-actual comparison the data supports. |
| Company (`company:view`) | Both domains plus overhead, monthly | `ICompanyOverviewService` | Admin salaries and net profit stay excluded (Stage 1 decision). |
| Execution sites | Materials (count, cost), ground labour per week, statements | `ISiteRecordsService`, `ILabourService` | Weekly money, not hours. |
| Changes | Every write as an `ActivityEvent` (about 85 kinds, grouped by `ActivityGroups`), indexed on `(DomainCode, OccurredAt)` and `(ProjectId, OccurredAt)` | `IActivityFeedService` | The list query has no date filter (gap 2). |
| Meetings | Count, topics, per project, in a range | `IMeetingService` | No attendees. |

**Not measurable, so Proton must say so:** expected hours or budgets in hours, Execution hours, causes of
change, employee performance, client satisfaction, and anything forward-looking beyond the existing projection.

**Gaps found (no database change needed for any of them):**
1. **Per-project money for a period.** `DomainFinanceService.SummaryAsync` computes period money per domain with
   `PeriodMoney` over `MoneyRollup.Costs(projectId)`; the same calculation per project only needs grouping. A new
   method on the existing service does it, reusing those exact functions.
2. **Activity in a date range.** Add optional `From` and `To` to `ActivityListQuery`. The
   `(DomainCode, OccurredAt)` index already exists. This is backwards compatible and makes the Stage 1 workaround
   (paging newest-first until the range starts) unnecessary.
3. **Lighter work totals.** `WorkReportAsync` also computes six quarters of all work, the matrix and money on
   every call. Comparing two periods would do all of that twice. Splitting out the totals part (total, by project,
   by person, by client, overtime) as `IReportService.WorkTotalsAsync`, which `WorkReportAsync` itself then uses,
   gives one calculation with two callers instead of a copy.

## B. Existing reports and analytics to reuse

| Existing | Reused for |
|---|---|
| `ReportService.OverviewAsync`: a period against the previous one, and **Attention** (Idle ≥ `IdleDays` = 7, Closeout ≥ `CloseoutProgress` = 90 %, On hold with days held, Not logging this week) | Company summary, attention list. **These are existing business thresholds, reused as is.** |
| `ReportService.WorkReportAsync` (by project, person, client; weekly; completed in range) | `compare_work`, through the extracted `WorkTotalsAsync` |
| `MoneyAttention` (overdue invoices, finished but owed, no payment agreed, cost > 85 % of payment) | Attention list, project review |
| `PeriodMoney`, `MoneyRollup`, `ProjectFinance`, `Recognition`, `ExchangeRateTable` | All money in Stage 3. Proton calculates no money itself. |
| `IDashboardService` (status counts, longest running, deadlines due in 14 days) | Company summary, attention list |
| `ProjectOverdue`, `RequirementRules.IsOverdue` | Overdue counts and signals |
| `ProtonPeriods.Resolve` (Stage 1) | Every period, extended with comparison periods |
| `ActivityGroups.GroupOf` | Grouping changes in `summarize_changes` |
| `ScheduledNotifications` (idle, deadlines, overdue invoices, cost warnings, all deduplicated) | Future scheduled intelligence (K) |

---

## C. Proposed analytical tools

Six tools, each a reusable primitive with explicit parameters, compact output and the authorization of the
endpoint whose data it uses. This takes the total from 30 to 36. A typical analytical question needs two to
four calls, against five to eight with the Stage 1 tools. There is no "query anything" tool.

Every analytical tool returns:

- **`basis`**: the exact days of each period, what they were compared with, the scope (domains and number of
  projects counted), the rules used (for example `idle: no work for 7+ days`), and `omitted`, meaning what was
  left out because the user can't see it or because the data doesn't exist. Example:
  `"money: needs financial:view"`, `"hours: Execution doesn't log hours"`.
- **Numbers already calculated**: current, previous, difference, percentage, share, rank. The model never adds
  numbers up itself.
- **Refs** for links, as in Stage 1.

| Tool | Parameters | Permission (from the endpoint) | Answers |
|---|---|---|---|
| `compare_work` | `period` (+ `from`/`to`), `compare_with` (`previous` \| `same_period_last_year` \| `custom` + days \| `none`), `group_by` (`total` \| `project` \| `person` \| `client`), optional `project_id`, `person_id`, `client_id`, `limit` (≤ 15) | `report:view` (`ReportsController.Work`); Design only | Hours this week or month vs the previous one; projects with the most hours or the biggest increase or decrease; who worked on a project and how that changed; a person's projects and workload change; "which employees contributed most to the increase" (group by person, filtered to the project). |
| `compare_money` | `domain`, `period`, `compare_with`, `group_by` (`total` \| `source` \| `project`), `sort` (`cost` \| `received` \| `profit` \| `change`), `limit` | `financial:view` (`FinanceController.Summary`), plus the domain visibility the service checks | Revenue and cost comparisons, highest-cost and highest-revenue projects in a period, what made cost rise (by source and by project). |
| `review_project` | `project_id`, `period` (default this month), `compare_with` (default previous) | `project:view` (`ProjectsController.Get`); the hours part needs `report:view`; the money part needs `financial:view`; parts the user can't see are listed in `omitted` | "How is X doing?" in one call: status, progress, target date and overdue, days since last work, hours vs the previous period with the top contributors and their change, open and overdue requirements, money for the period vs the previous one, and the attention signals for this project. |
| `get_company_summary` | `period` (default this week), `compare_with` (default previous) | Per block: projects → `project:view`; work → `report:view`; money → `financial:view` per visible domain; company overhead → `company:view` | Weekly or monthly summary (F), role-aware: each block only for what the user's permissions show; the rest is listed in `omitted`. |
| `summarize_changes` | `period` (default since the start of last week), optional `domain`, `project_id` | `activity:view` (`ActivityController.List`), filtered by the feed service as today | "What changed since last week?" Counts by activity group (projects, requirements, money, people, …), projects created, completed, put on hold or resumed, and up to 15 notable events; `truncated` when there are more. |
| `get_attention` | optional `domain`, `limit` (≤ 10) | Per signal source, as for the summary | "What should I pay attention to?" (G): deterministic, deduplicated, ranked signals, each with its facts and the rule that fired it. |

The Stage 1 tools stay as they are. `get_time_summary` overlaps with `compare_work` when `compare_with=none`;
if evaluations show the model choosing badly between them, its retirement is a separate decision. No Stage 1 or
Stage 2 redesign.

**Where the code lives:**

```text
Business/Proton/
    ProtonPeriods.cs      (+ comparison periods)
    ProtonTrends.cs       new, pure: change, percentage, like-for-like, baseline, contributors
    ProtonSignals.cs      new, pure: the attention rules over facts already loaded
Proton/Analysis/          new folder (same layering as Proton/Usage)
    ProtonAnalysisService.cs   calls the existing services and shapes analytical results
    ProtonQuestionCache.cs     per-question memo (see I)
Proton/Tools/AnalysisTools.cs  the six tools: input → service → bounded output
Proton/Prompts/analysis.md     how to present analysis (no business rules)
```

## D. Deterministic calculations (application code, unit tested)

| Calculation | Rule |
|---|---|
| Period days | `ProtonPeriods.Resolve` as today. The model only names the period. |
| Comparison period | **Like-for-like** *(approval)*: a running period ("this month", 1–7 Oct) is compared with the same number of days at the start of the previous one (1–7 Sep), and `basis` says so. A finished period is compared with the whole previous period. A custom range is compared with the same number of days just before it. `same_period_last_year` moves both ends back a year (29 Feb → 28 Feb). Month ends clamp: 1–31 Mar against 1–28 Feb is "whole month vs whole month", not 31 days. |
| Partial period | `partial: true` and days elapsed out of days total, so the model can say "so far". |
| Change | `diff = current − previous`; `pct = round(diff / previous × 100)`, **null when previous is 0** (reported as "new" or "from none", never ∞ or 100 %). |
| Shares and ranks | Share of total (whole %), rank by value and by change; ties broken by name. |
| Contributors to a change | Each group's difference and its share of the total difference, largest first. This is what answers "why" questions with measurable facts (see Answer rules). |
| Baseline | Average of the previous four equal periods, only when all four have data; otherwise no baseline. |
| Money | Only through `PeriodMoney`, `MoneyRollup` and `ProjectFinance`, rounded with `PeriodMoney.Round`. Unconverted costs are listed apart, never guessed. |
| Hours | Minutes summed in code, shown to one decimal place. |
| Signals | `ProtonSignals` over facts already loaded (G). |

## E. Trend and anomaly approach (simple, deterministic, explainable)

1. **Always report measured changes plainly**: "Hours rose from 110 to 147 (+34 %)." That is a calculation, not
   a judgement.
2. **Words like "notable", "significant" or "unusual" only come from a rule in code**, which sets a flag on the
   result with the rule's text in `basis`. Without the flag, the prompt forbids those words.
3. Proposed rules *(approval: values; all of them configurable in `Proton:Insights`)*:

   | Flag | Proposed rule | Why |
   |---|---|---|
   | `notable_change` (hours) | \|change\| ≥ 30 % **and** ≥ 10 h | The hours floor stops "+100 %, 1 h → 2 h" from being flagged. |
   | `notable_change` (money) | \|change\| ≥ 30 % **and** ≥ 1,000 USD | Same reasoning for money. |
   | `unusual` (vs baseline) | current ≥ 1.5 × or ≤ 0.5 × the 4-period average, with a baseline and the same floors | A rolling average resists one odd month. |
   | `idle` | Existing `ReportService.IdleDays` = 7 | Already a business rule. |
   | `closeout` | Existing `CloseoutProgress` = 90 % | Already a business rule. |
   | `cost_high` | Existing `MoneyAttention.CostWarning` = 85 % | Already a business rule. |
   | `target_soon` | Active project with a target date within 14 days | Matches the existing 14-day deadline window. |
   | `overdue` | `ProjectOverdue.IsOverdue` | Already approved. |

4. **No machine learning and no statistics library.** Period-over-period plus a four-period average is enough
   for this volume of data, and anyone can check it by hand.
5. **People:** only facts ("no work logged since 28 Sep"). Proton never flags a person as low-performing or
   unusual. Recorded hours aren't the same as work done, and the prompt says so.

## F. Company summary design

`get_company_summary` returns only metrics that exist today, in blocks the user's permissions allow:

```text
Period: 5–11 Oct 2026 (this week, to date) vs 28 Sep–4 Oct
Projects (project:view, per visible domain)
  active 12 · on hold 2 · completed in period 1 · started in period 2
  overdue 1 · target date within 14 days 2 · no work for 7+ days 3 (Design)
Work (report:view, Design)
  428.5 h (+12 % vs 382.0 h) · 14 people · 9 projects · overtime 31.0 h
  busiest projects (top 3, with change)
Requirements
  open 41 · overdue 3 · due in 14 days 7
Money (financial:view, per visible domain)
  received · cost by source · profit · margin, each with change · overdue invoices (count, total)
Company (company:view)
  overhead for the period; no admin salaries, no net profit
Attention: the top 5 signals (G)
omitted: [...]
```

**Role-aware:** a super admin gets both domains and the company block. A Design admin gets Design projects, work
and requirements, plus money only if they hold `financial:view`. Execution never gets a work block; `omitted`
explains why. The services' own domain checks decide this; the prompt only presents it.

## G. Proactive insight design (on demand only)

`get_attention` answers "is there anything I should look at?". It never notifies anyone.

- **Sources:**
  - the existing attention rules (idle, closeout, on hold, not logging);
  - `MoneyAttention` (overdue invoice, finished but owed, no payment, cost high);
  - overdue projects and `target_soon`;
  - overdue requirements;
  - `notable_change` and `unusual` on project hours or cost (this week, or this month after the 7th).
- **Each signal** has: kind, the project or domain, its facts (days, %, amounts), the rule text, and a refs link.
- **Deduplicated per project.** One entry per project, carrying all of its signals, so a project that is idle,
  overdue and over cost shows up once.
- **Ranked:**
  1. overdue (money, then project)
  2. cost high
  3. target soon
  4. notable change
  5. idle
  6. closeout
  7. on hold
  8. not logging

  Ties go to the most days or the biggest amount. Ten at most, with `more` counting the rest.
- **Permission-aware:** a signal is only built from data the user can read. A user without `financial:view`
  gets no money signals; `omitted` says money wasn't checked.
- **Wording:** the prompt turns signals into "You may want to review …", never an instruction or a judgement
  about a person.

## Answer rules (prompt `analysis.md`, never a security boundary)

1. **"Why" questions:**
   - state the measured change;
   - name the measurable contributors from the data (by source, project or person);
   - if the data doesn't show a cause, say exactly that ("The data shows cost rose 18 %, mostly labour on Villa
     Mounir (+37 h); it doesn't show why more time was logged.").
2. **Four kinds of statement, kept apart:**
   - facts and calculations are stated plainly;
   - an interpretation starts with "Based on this data, …" or "This suggests …";
   - a recommendation starts with "You may want to …" and is optional;
   - nothing about hiring, firing, pay or anyone's performance.
3. **No invented numbers or dates.** Nothing beyond the tool output. "There is no target date recorded for this
   project."
4. **Always say the period's dates, and whether it is partial.**

---

## H. Cost controls

| Control | Value | Reasoning |
|---|---|---|
| Model calls per question | `MaxRoundTrips` 8 (unchanged) | Analytical tools combine data, so two to four calls are typical. |
| Tool calls per question | `MaxToolCallsPerQuestion` 12 (unchanged) | — |
| **Heavy calls per question** *(new option)* | 4 | `compare_money`, `get_company_summary`, `get_attention` and `review_project` load all-time money; beyond four the tool returns "limit reached". |
| Question time | 55 s (unchanged, under nginx's 60 s) | — |
| Period length | At most 366 days per period (as today), so two years when comparing | Bounds the entries loaded. |
| Output size | Lists ≤ 15 rows, top movers ≤ 5, signals ≤ 10, events ≤ 15; the existing 40,000-character cap stays as a backstop | Analytical outputs target < 4 KB. |
| **Conversation context** *(new option)* | Close the conversation (with a "start a new one" note) once its last request exceeds 80,000 input tokens | Long analytical threads re-send their tool results on every question; this caps the cost per question at roughly 0.20 USD with the configured prices. |
| Tracking | Already recorded per question: model calls (`RoundTrips`), tool calls, input, output and cache tokens, cost, user, date, conversation, and each tool's name and duration (`Tools` JSON) | — |

**Budget view for super admins (no schema change):** extend `GET /api/proton/budget` with this month's usage by
person (with their domain), by tool, by day, and the most expensive conversations. All of it is computed from
`proton_usage`, including tool time from the `Tools` JSON. The domain is the person's *current* one; if a
historical domain is wanted, that is a `Domain` column *(approval, not recommended)*. Normal users keep seeing
only their allowance.

## I. Caching strategy

- **No cross-request cache.** Money depends on payments, costs, rates, labour and pay records, and work on
  entries that can be back-dated. No invalidation hook covers all of that, and a stale summary is worse than a
  slow one. A cached previous period isn't safe either, because a back-dated entry changes it.
- **A per-question memo (`ProtonQuestionCache`)**, scoped to one question's tool session:
  - It keeps each domain's `MoneyRollup` and the work rows of a range loaded **once per question**.
  - For example, `get_company_summary` followed by `review_project` in the same answer doesn't reload all-time
    money.
  - It lives for one question of one user (seconds), so it can't go stale or leak between users.
  - The data is loaded through the same services, so permissions apply first.
- The existing permission cache is unchanged.

## J. Testing strategy

| Kind | What |
|---|---|
| Unit: `ProtonTrends` | Totals, percentages, zero-to-zero and zero-to-something, negatives, rounding; like-for-like periods across 28-, 29-, 30- and 31-day months, the year boundary, 29 Feb, the company's first day of the week; partial flags; baseline with fewer than four periods; contributors that add up to the total change. |
| Unit: `ProtonSignals` | Each rule fires at the threshold and not below it; the floors; dedupe per project; ranking; the limit and `more`. |
| Integration: correctness | Seeded entries, payments and costs → exact hours and money for two periods, per project and person; empty periods; identical results to the existing report endpoints for the same range (the parity check that proves nothing was re-implemented). |
| Integration: authorization | Design admin: no Execution data, and no money without `financial:view`; `report:view` missing → work block omitted and `compare_work` refused; a project in another domain → not found; employees refused (`proton:use`); the excluded-fields guard extended to every new output (no salary, per-person cost, hourly rate, admin salaries or bank data). |
| Integration: multi-tool (scripted model) | A three-tool plan; a tool failing mid-plan → partial answer with the failure reported; the heavy-call limit; round-trip and tool-call limits; timeout and cancellation; a large dataset stays within the output caps. |
| Integration: conversation | "Last month" → "compare with August" → "which project?" → "who worked on it?" keeps period, comparison, metric and entity in the focus. |
| **Evaluations with the real model** *(approval: API key and cost)* | About 40 golden questions (from section 4 of the brief) run against seeded data, checked in code: tool sequence, numbers quoted match the tool output, no unflagged "significant" or "unusual", "why" answers contain no cause the data lacks, interpretations labelled, nothing about personnel decisions. Opt-in (not in CI), about 2–4 USD per run. |

## K. Future scheduled intelligence (design only)

The existing `NotificationScheduler` runs `ScheduledCheck`s with deduplicated `NotificationDraft`s (unique
recipient + key). A future "Monday 9 AM company summary" becomes one more check, with no second scheduler:

```text
NotificationScheduler tick
  → ScheduledCheck "proton_summary" (who asked, at what time)
  → per recipient: an "acting as" scope (ICurrentUser for that user, no HTTP)
  → ProtonToolbox.OpenAsync(origin: Scheduled): the same tools, the same permission checks for that user
  → get_company_summary (deterministic; optionally the model writes the narrative, charged to the budget)
  → NotificationDraft with key "proton_summary:{week}" → existing notification and email delivery
```

It would need:

- a non-HTTP `ICurrentUser` implementation, which doesn't exist yet;
- a subscription preference (**a table, approval at that time**);
- a notification kind.

Because the summary is deterministic, the report still works when the AI budget runs out.

## L. Stage 4 recommendations

1. **Scheduled summaries and opt-in proactive notifications** (K), using `get_attention` with per-user
   subscriptions and quiet rules.
2. **Semantic retrieval over free text**, only if needed: work-entry descriptions, requirement text and meeting
   topics ("what did we do about the façade?"). MySQL full-text search may be enough before any vector store.
   Messages stay excluded.
3. **Forecasting** from what exists: projected margin (`ProjectProjection`), progress pace vs target date.
4. **Voice (GPT-Live-1)** on the same toolbox, as planned since Stage 1.
5. **Persistent conversations** (database-backed), needed for voice and scheduled follow-ups.
6. **More actions** from the Stage 2 risk matrix (complete a project, assign requirements), once Stage 2 has been
   used for a while.

---

## M. Implementation phases (small, reviewable commits; nothing is enabled beyond Stage 2 until P7)

| Phase | Content |
|---|---|
| P1 | `ProtonTrends`, comparison periods, `ProtonSignals` + unit tests |
| P2 | Service additions: `IReportService.WorkTotalsAsync` (used by `WorkReportAsync`), per-project period money on `IDomainFinanceService`, `From`/`To` on `ActivityListQuery` + parity tests |
| P3 | `compare_work`, `compare_money` + `ProtonQuestionCache` + tests |
| P4 | `review_project`, `get_company_summary` + authorization tests |
| P5 | `summarize_changes`, `get_attention` + tests |
| P6 | `analysis.md`, focus (metric and comparison), heavy-call and context limits, `sources` on the reply, budget breakdown |
| P7 | Client: a "Based on" line under analytical answers (periods, scope, omitted) and the budget breakdown for super admins; docs; evaluations run once, with the results reported |

**Traceability in the UI:** the reply gains `sources`: for each analytical tool, its display name, periods, scope
and omissions, taken from `basis` (never raw data or internal names). The panel shows them as a small collapsible
"Based on: 1–7 Oct vs 1–7 Sep · Design · 9 projects". This is an additive API change.

## N. Decisions needing approval

1. **Thresholds** (E.3): notable change at 30 % and 10 h or 1,000 USD; unusual at 1.5× or 0.5× of a
   four-period average; target soon within 14 days. Idle, closeout and cost-high reuse existing rules.
2. **Like-for-like comparisons** for running periods (D). The Design dashboard today compares month to date
   with the *whole* last month; Proton would differ from it, and would say so in `basis`.
3. **Service additions without new endpoints** (A, gaps 1–3). They are reached only through Proton tools, which
   carry the existing endpoints' permissions (`financial:view`, `report:view`, `activity:view`).
4. **Domain admins with `financial:view` get their domain's money in summaries.** Company-wide figures need
   `company:view`. Admin salaries and net profit stay excluded.
5. **Project costs in period comparisons:** shown as the app's Financials pages show them to `financial:view`
   users. Proton never derives per-person or per-hour figures. Note that a project worked on by one person still
   implies their cost, as it already does in the app.
6. **Budget breakdown** from `proton_usage` with no schema change, rather than a new `Domain` column.
7. **New options:** heavy calls per question (4) and the conversation context cap (80,000 input tokens).
8. **`sources` on the reply** and the client's "Based on" line.
9. **Real-model evaluations:** an API key in user secrets and about 2–4 USD per run.

**Not in Stage 3:**
- new tables, permissions, dependencies or external services;
- scheduled reports and proactive notifications;
- vector, embedding or RAG infrastructure;
- machine learning;
- voice;
- changes to Stage 1 or Stage 2 behaviour.

## Implementation notes (where the build differs from this plan)

- **Weekly hours signals:** they compare the last *full* week with the week before, and the baseline is the four
  weeks before that. This comes from the project list's 8-week figures, so no extra queries are needed. Cost signals
  compare the last 30 days with the 30 before.
- **Per-question cache:** it lives inside `ProtonAnalysisService` (scoped to the request) rather than a separate
  `ProtonQuestionCache` class.
- **Company summary:**
  - no overtime figure;
  - company overhead is this month and last, per currency, from the existing overhead endpoint;
  - "completed in the period" comes from the activity history.
- **Evaluation:** an opt-in test suite (`ProtonEvalTests`) that runs when an API key is set. No key was configured
  when Stage 3 was built, so it has not been run yet.
