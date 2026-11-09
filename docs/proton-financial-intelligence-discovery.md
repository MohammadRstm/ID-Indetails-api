# Proton 2.0, Financial Intelligence Engine: architectural discovery

Investigation only, 9 November 2026. Nothing in the application was changed for this report.

- **Code read:** the API at `master` (`4ff4092c`), in `ID-Indetails.api/`; the client (`Ident-Internal-Management-System-client/src/`) for Proton's interface only.
- **Three small Proton fixes** are in PR #193 (`MR-Proton-Audit-Fixes`): see [§2.F](#f-changes-on-the-fixes-branch). Everything else describes `master` as of `4ff4092c`.
- **How to read the labels:**
  - **[Code]** a fact read in the source. Paths are relative to `ID-Indetails.api/` unless they start with `docs/`, `client:` or a test project. A `~` before a line number means approximate.
  - **[Docs]** what the project documentation says.
  - **[Conclusion]** my technical judgement.
  - **[Ask]** something only the business owner can settle (collected in [Questions for the Business Owner](#questions-for-the-business-owner)).
- **Method:** six read-only passes over the code (Proton tools, revenue and billing, costs and pay, calculations, authorization, history and infrastructure). The facts the conclusions rest on were re-checked by hand (listed in [Appendix A](#appendix-a-facts-re-checked-by-hand)). Where code and docs disagree, the code is taken as true and the gap is listed in [Appendix B](#appendix-b-documentation-that-contradicts-the-code).

---

## 1. Executive Summary

**Proton today** [Code]
- An agent in the API, on Anthropic's `claude-haiku-5-5`. It has 77 read tools and 103 "propose" tools.
  - A propose tool only prepares a card. The person's Confirm button runs the change.
  - Proton cannot write while it reads: a database interceptor refuses anything but `SELECT`.
- **Authorization is inherited, never re-implemented.** Each tool names the controller endpoint it stands in for and is offered only if the caller passes that endpoint's permission policy. The service it calls then applies the same domain and money checks as the page.
- **It already does analysis in code:**
  - period comparisons, shares, contributors and baselines (`Business/Proton/ProtonTrends.cs`);
  - rule-based signals ranked by urgency (`Business/Proton/ProtonSignals.cs`);
  - typed datasets that the client draws as charts, tables and KPI tiles.

**The financial data** [Code]
- **Strong:**
  - dated, append-only pay history (`UserPayHistory`) and exchange rates (`ExchangeRate`);
  - an append-only price history (`ProjectPaymentChange`);
  - an activity feed that stores money amounts (`ActivityEvent.Amount`/`PrevAmount`);
  - soft deletes on costs, voids on payments and invoices;
  - one shared, pure, tested project calculator (`Business/Finance/ProjectFinance.cs`). Project pages, lists, clients, Financials, PDFs, notifications and Proton all reuse it through `ProjectFinanceService` and `MoneyRollupService`.

**What a Financial Intelligence Engine is missing** [Code] + [Conclusion]
- **No cost budgets or estimates of any kind.**
  - Design departments have "budgets", but they are the price split by share: revenue, not planned cost.
  - Execution's internal BOQ drafts are not linked to cost.
  - Neither domain has planned hours.
- **No committed or future costs:** no purchase orders, supplier invoices, payables or accruals.
- **Progress is only the share of requirements ticked done**, in both domains (`Business/Projects/ProgressCalculator.cs`). Nothing records construction progress against BOQ quantities. The existing "projected cost" (cost ÷ progress) rests on this.
- **Some labour is costed nowhere:**
  - Execution employees' salaries reach no site and no company figure.
  - Design employees' paid but unlogged time (excused hours, leave, shortfall) is in no profit figure.
  - Admins' and super admins' logged hours cost projects 0.
  - The threshold overtime premium, which `docs/company.md` calls a company cost, appears in no profit figure.
- **History can't be fully replayed:**
  - re-submitting a labour week replaces its day rows;
  - site expenses are edited in place;
  - deleting a project removes everything on it;
  - "expected revenue" applies today's supervision rate to past events;
  - there are no financial snapshots.
- **Proactive monitoring has building blocks but no agent path.**
  - Building blocks: background jobs with `PeriodicTimer`, a notification fan-out to permission holders, de-duplication keys, push and email.
  - Missing: Proton can't run without a signed-in person, and there is no system identity for an analysis.

**Bottom line** [Conclusion]
- **Can be built now on today's data:**
  - project and company profitability "as the app defines it";
  - period comparisons;
  - Design labour-cost analysis (as the cost of logged hours);
  - utilization of logged against expected hours (Design only);
  - risk signals.
- **Need new data and business rules first:**
  - budget variance;
  - reliable cost and profit forecasts, above all for Execution;
  - cash-flow forecasting (payables and payroll timing);
  - comparisons with similar projects;
  - scenario simulation.
- **How to build it:** add new GET endpoints, services and pure rules on top of the existing calculator, each mirrored by a Proton read tool that inherits its permission. A second calculator would contradict the first.

---

## 2. Existing Proton Architecture

### A. Core architecture

| Question | Answer | Where |
|---|---|---|
| Initialization | `AddProton` registers an `IAnthropicClient` singleton (API key from `Proton:ApiKey`, 40 s timeout, one retry), every tool as a scoped `IProtonTool`, the toolbox, the chat agent, the conversation store, the usage service, voice, history and the background jobs. Proton answers only when `Proton:Enabled` is true **and** a key is set; with Enabled on, the API won't start without the key | [Code] `Extensions/ProtonServiceCollectionExtensions.cs:~38-70`; `Proton/Usage/ProtonUsageService.cs:29` |
| Request path | `POST /api/proton/messages` (JSON, 55 s) or `/messages/stream` (Server-Sent Events, 120 s) → `ProtonController` (`proton:use`, rate policy `proton`) → `ProtonChatAgent.PrepareAsync` (switched on, allowance, budget hold, conversation, turn origin) → `ProtonToolbox.OpenAsync` (the tools this person may use) → `RunTurnAsync` (the model loop) → usage row and saved chat | [Code] `Controllers/ProtonController.cs:48-66`; `Proton/Agent/ProtonChatAgent.cs:54-225` |
| Provider and model | Anthropic Messages API through the official SDK. `claude-haiku-5-5` by default (`Proton:Model`), effort `medium`, adaptive thinking, 8,000 output tokens. Voice adds OpenAI `gpt-live-1`, which only listens and speaks and hands every question to this agent | [Code] `Configuration/Options/ProtonOptions.cs:18-25, 200`; `ProtonChatAgent.cs:394-405` |
| System instructions | 12 Markdown files in `Proton/Prompts/` (persona, company, data rules, money meanings, analysis, navigation, files, actions, messages, presentation, chat; voice has its own), compiled into the assembly and joined into one system prompt. It is the same for everyone and carries a cache breakpoint. `{product}` is replaced with the product name. The prompt version on each usage row is a SHA-256 of the text | [Code] `Proton/Prompts/ProtonPrompts.cs:15-37`; `ProtonChatAgent.cs:372-375` |
| Per-question context | A context note written by the app goes with each question: today in the company time zone, who is asking (role, visible domains), the conversation focus, company name, the page and record the person is on (only if they may see it), their saved preferences, the recap after a rollover, whether it was tapped or spoken, and what happened to earlier cards | [Code] `ProtonChatAgent.cs:641-686` |
| Conversation context | In memory (`ProtonConversationStore`), per user and conversation id, released after 30 idle minutes, 4 hours, or a restart. The message list is exactly what was sent to the model. At 20 questions or 80,000 input tokens it **rolls over**: the messages are dropped and the next question gets an app-written recap of the last three exchanges (400 characters each). A **focus** (project, client, people, period, last comparison) is taken from tool results, never guessed from text | [Code] `Proton/Conversation/*`; `ProtonChatAgent.cs:84-91` |
| Conversation history | Every answered question is saved as the person saw it (`proton_conversations`, `proton_conversation_messages`), 90 days (`ProtonHistoryCleanupJob`). A chat stores a hash of the access it was made under: if the owner lost a permission or domain, its messages are hidden; if their access changed at all, it can't be carried on. Carrying on after the server let it go restores a recap | [Code] `Proton/History/ProtonHistoryService.cs`; [Docs] `docs/proton.md` "Saved chats" |
| How tools reach the model | Each tool's input is a C# record; its JSON schema is generated from the record and its `[Description]`s. Definitions are sent on every model call. Past 55 tools for a person (`DeferToolsAbove`), all but a core set of 28 are sent with `defer_loading` and found through the provider's BM25 tool search, so the definition list stays cacheable | [Code] `ProtonChatAgent.cs:367-369, 619-634`; `Proton/Agent/ProtonToolSearch.cs:10-19` |
| Sequential or parallel | The model may return several tool calls in one response. **The server runs them one at a time**, in order: "the tools share the request's database context" | [Code] `ProtonChatAgent.cs:497-533` (comment at :518) |
| Several calls in one investigation | Yes. The loop allows up to 8 model calls (`MaxRoundTrips`) and 12 tool calls (`MaxToolCallsPerQuestion`) per question. On the last round `tool_choice` is `none`, so the model must answer with what it has. At most 4 "heavy" tools (those loading every project's money) per question | [Code] `ProtonChatAgent.cs:391-441`; `Proton/Tools/ProtonToolbox.cs:146-150` |
| Errors and failed tools | A failing tool returns a JSON `{status, message}` with `is_error: true` to the model, and the loop goes on. Status values: `invalid` (input or validator), `not_found` (missing or not visible, the same message), `forbidden`, `refused` (a business rule, the heavy limit, a family switched off), `timeout` (20 s per tool, 45 s for reading a file), `failed` (unexpected; logged). Results over 40,000 characters are refused. A failed model call or a timeout ends the question as `failed` (`503 proton_unavailable`); its tokens are still recorded and any card it proposed is closed as superseded | [Code] `ProtonToolbox.cs:122-219, 324-339`; `ProtonChatAgent.cs:146-163, 479-494` |
| Planning | **No explicit planner.** Multi-step reasoning is the model's own (adaptive thinking) inside the tool loop, guided by the prompts. The code adds two nudges: a "write your answer first" refusal when the model offers suggested replies with no text, and one reminder when the person asked for a chart or table in their own words and none came | [Code] `ProtonChatAgent.cs:414-459` |
| Token usage and cost | Every model call's tokens (input, output, cache read, cache write) are counted, including a streamed call cut off part way. One `proton_usage` row per question: user, conversation, model, prompt version, outcome, model and tool call counts, tokens, estimated cost (from `Proton:Prices`, with a long-prompt rate), the question, up to 8,000 characters of the answer, and the tool calls with free text replaced by its length | [Code] `ProtonChatAgent.cs:700-822`; `Business/Proton/ProtonUsageRules.cs` |
| Limits | A monthly company budget (50 USD plus top-ups); each question holds 0.25 USD while it runs; 300 questions per person per month (only questions that used a tool count); 10 questions a minute per person; at most 3 pending cards per conversation, 30 confirmed changes an hour, 20 messages and 20 outside emails a day | [Code] `ProtonOptions.cs`; `Proton/Usage/*`; `Repositories/Proton/Implementation/ProtonUsageRepository.cs:24-25` |

### B. Read tools and write tools: how they differ

- **Read tools** (77) can only stand in for a GET endpoint (`ProtonEndpoint.Of` refuses anything else) and run inside `ProtonReadOnly`. That is an EF Core command interceptor refusing anything but `SELECT`/`WITH`. Any change a tool staged without saving is dropped and the call refused. Email, file storage, the content store, notifications and live updates also refuse while a tool runs. [Code] `Proton/Safety/ProtonReadOnly.cs:45-56, 105`; `ProtonToolbox.cs:225-244`; `Proton/Safety/ProtonSideEffectGuards.cs:17-23`
- **Propose tools** (103) stand in for a write endpoint but only store a pending card (`proton_actions`), which expires after 10 minutes.
  - On Confirm, `ProtonActionService.ConfirmAsync` checks the card is the person's own, still pending and not expired; that its family is switched on; the hourly and daily caps; the typed phrase the server stored; and that the person's permissions are unchanged since the card. It then claims the card with one conditional `UPDATE` and calls the same service as the endpoint, after checking the endpoint's permission again.
  - The model has no path to run anything. [Code] `Proton/Actions/ProtonActionService.cs:36-122`
- **Families:** Core is on by default; Lifecycle, Deletes, Access, CompanyRules, PayRules, Money, Messaging and Website are off until switched on in `Proton:Actions`. Every family except Core and Lifecycle may only be proposed on a turn the person **typed**, not from a tapped button or a voice question. [Code] `ProtonToolbox.cs:131-145`; `Proton/Actions/ProtonCoverage.cs:91-92`

### C. Frontend interaction

| Question | Answer |
|---|---|
| Transport | JSON (`ProtonReplyResponse`) or Server-Sent Events with `status` (a fixed phrase per tool, never its data), `delta` (the answer as it is written, per model call), `done` (the same reply object), `error` (the uniform error contract), and a `: ping` every 10 s. The client reads it with fetch and shows a status line, the streaming text and a Stop button [Code] `Proton/Agent/ProtonEventStream.cs`; `client:domains/shared/proton/protonStream.ts` |
| Reply fields | `conversationId, reply (text), refs (records named in the answer, as links), limited, usage (the person's allowance), sources ("Based on …", from analysis bases), uiCommands, choices, quickReplies, suggestions, pendingActions (cards), blocks (charts and tables), rolledOver` [Code] `DTO/Response/Proton/ProtonResponses.cs`; `ProtonChatAgent.cs:208-222` |
| Navigation | A typed command, never an address: `{type: open_project / open_client / open_person / open_page / open_surface / fill_form / set_preference / refresh, id?, page?, tab?, part?, surface?, …}`, all enums and positive ids. The API checks the record or page against the person's access before emitting it; the client maps it to its own routes and checks access again. One navigation per reply (`ProtonUiRules.Pick`) [Code] `Proton/Ui/ProtonUiCommand.cs:208, 299`; `client:…/proton/uiCommands.ts` |
| Charts and tables | A tool returns typed **datasets** (`ProtonDataset`: columns with units, rows, period, notes, whether the rows are parts of a whole, at most 100 rows). The conversation holds the last 12 for 15 minutes; the model sees only each one's id, title, columns and the forms it fits, never its values. `present_data` turns one into a **block**: `bar`, `columns`, `line`, `composition`, `kpis` or `table`, under rules enforced in code (`Proton/Ui/ProtonPresentationRules.cs`). For example, a chart never mixes units, a trend needs time points, and a composition needs every part. The model picks only the form, columns, sort, count and title [Code] `ProtonToolbox.cs:165-170`; `Proton/Tools/PresentTool.cs`; `Proton/Ui/ProtonPresentation.cs:109` |
| Structured output | Yes, as datasets, blocks, cards and commands. The answer text itself is Markdown, rendered as elements, never as HTML; links come only from `refs` [Code] `client:…/proton/protonText.ts`, `ProtonMarkdown.tsx` |
| Several visualizations | Up to 3 blocks per answer (`Visuals:MaxBlocksPerAnswer`), 64 KB each. Blocks are saved with the chat [Code] `ProtonOptions.cs:171`; `PresentTool.cs` |
| Datasets in tool results | Yes. Tools producing them today include `list_projects`, `get_project_team`, `get_project_financials`, `get_time_summary`, `get_person_work`, `get_client`, `get_domain_dashboard`, `get_money_by_month`, `list_invoices`, `list_payments`, `compare_work` and `compare_money`. `add_up` totals a dataset column in code (refusing a partial dataset or percentages) [Code] `Proton/Tools/AddUpTool.cs` |
| Loading, errors, long work | Everything happens inside one HTTP request: there is no background investigation and no job id. The blob shows "thinking" or "working" moods and "Still looking…" after 8 s. Errors use the API's uniform `{type, code, message, errors, traceId}`. A stopped or dropped question is recorded as `failed` and adds nothing to the conversation [Code] `client:…/proton/blob/protonMood.ts`; `ProtonChatAgent.cs:146-163` |

[Conclusion] A financial report, forecast or dashboard can be delivered with today's pieces: datasets, up to 3 blocks, a `sources` basis, and `open_page` to Financials. Anything longer than about 2 minutes (a full portfolio forecast) would need a new asynchronous pattern, which does not exist.

### D. Analysis tools (already financial-intelligence-like)

[Code] `Proton/Tools/AnalysisTools.cs`, `InsightTools.cs`, `Proton/Analysis/ProtonAnalysisService.cs`, `Business/Proton/ProtonTrends.cs`, `ProtonSignals.cs`

- **Comparisons** (`ProtonPeriods.Compare`):
  - a running period against the same number of days at the start of the previous one;
  - a finished period against the whole previous one;
  - a day, last-N-days or custom range against as many days just before;
  - also "same period last year" or custom.
- **Figure arithmetic** (`ProtonTrends`):
  - `{current, previous, diff, pct}`, where `pct` is null when there was nothing before;
  - shares, and contributors' shares of the change;
  - baseline = the average of the 4 earlier periods, null unless all of them are above 0.
- **Rules** (`Proton:Insights`):
  - *notable*: ±30% and at least 10 h or 1,000 USD;
  - *unusual weekly hours*: at least 1.5× or at most 0.5× the baseline, and at least 10 h away from it;
  - *target soon*: within 14 days;
  - from existing business rules: idle (7 days without work), close-out (90% progress), cost past 85% of revenue (Design), overdue invoice, finished but owed, no payment.
- **`get_attention`**: one row per project with all its signals, ranked overdue invoice → finished but owed → overdue project → overdue requirements → cost high → no payment → target soon → unusual or notable change → idle → close-out → on hold → not logging.
- **`review_project`** and **`get_company_summary`**: each block only if the person may call its own endpoint. Results carry a `basis` (the exact days, what they were compared with, the rules, and what was left out and why).
- **Per-question cache:** `ProtonAnalysisService` is scoped, so one request and one user. Nothing is cached across requests.

### E. Other Proton features (for completeness)

- Reading project files (PDF, images, video frames, Office text), off until the company allows it.
- Reading the client discussion; reply suggestions there.
- Filling 30 forms for the person to save, and message drafts.
- Memories of the person's own preferences (300 characters, 20 at most).
- Voice.
- A no-model **briefing**: `get_attention`'s top 3, cached 3 minutes per user and permission set.

[Code] `Proton/Files`, `Proton/Suggestions`, `Proton/Ui/ProtonForms.cs`, `Proton/Memory`, `Proton/Analysis/ProtonBriefingService.cs`

### F. Changes on the fixes branch

PR #193, `MR-Proton-Audit-Fixes` (3 commits; built and tested, CI green):

1. **`get_project_departments`** no longer returns each person's labour cost, only their minutes. Their cost beside their minutes was their hourly cost.
2. **The domain-settings card** now says what "hold logging" really does: projects on hold still take work.
3. **`propose_project_lifecycle`** shows what the client owes or paid ahead, and can excuse or write off what is owed, or keep the overpayment. That is held to the Money family, with the amount typed. `get_project_financials` now returns `settled_owed` and how it was settled.

---

## 3. Complete Proton Tool Inventory

**Columns:**
- *Permission* is the endpoint's own `[HasPermission]`; "rule-based" means the endpoint has none and its service limits the data to the caller. Every tool also needs `proton:use`.
- *Default holders*: **S** super admin, **A** admin (own business), **E** employee, **W** Website staff (`Constants/PermissionCatalog.cs:100-155`).
- **None of the read tools has a side effect.** The one exception: `get_website_analytics` fills a shared, user-neutral cache of Google Analytics totals.
- ★ = always sent to the model (the core set).

**Period inputs** (`Business/Proton/ProtonPeriods.cs`):
- The words are `today, yesterday, this_/last_ week|month|quarter|year, last7_days, last30_days, last90_days, custom`, plus `from`/`to`.
- They become company-time-zone days with the company's week start, 366 days at most.
- Pitfall [Code]: `from`/`to` are ignored unless `period` is `custom`.

### 3.1 Money, billing and company

| Tool | Parameters | Implementation (service → calculation) | Permission (holders) | Output (and what the tool computes itself) |
|---|---|---|---|---|
| `get_project_financials` ★ | `project_id` | `IProjectService.GetAsync` → `MoneyOfAsync` → `MoneyRollupService` / `ProjectFinanceService.Calculate` / `ProjectFinance.Summarise`, `ProjectBilling.Summarise` | `GET /projects/{id}` (`project:view`); refuses when money is hidden (needs `financial:view`: S) | agreed revenue (net), lock flag, agreed with TVA, TVA %, fee, supervision % and amount, permits, expenses per currency, billing block (invoiced, outstanding, received, on account, not yet invoiced, left to invoice, owed, paid ahead, overdue, drafts), cost by source and unconverted, agreed profit and margin, recognised to date, projection, labour hours, Execution cash profit, pocket moves. Two datasets. Leaves out cost per hour on purpose. Tool's own maths: percentages only |
| `get_domain_finance` | `domain`, `period` (default this quarter), `from`, `to` (≤3×366 days) | `IDomainFinanceService.SummaryAsync` → `PeriodMoney`, `ExpectedRevenue`, `MoneyRollup` | `GET /finance/{domain}/summary`, `financial:view` (S) | `period` (cash net of TVA: received, TVA collected, expected revenue, cost by source, cash profit, margin), `as_of_today` (billing, gross), `all_time` (agreed revenue and profit). Adds `so_far` |
| `get_money_by_month` ★ heavy | `domain`, `months` 2–36 | `IDomainFinanceService.MonthlyAsync` | `financial:view` (S) | per month: received, expected, cost by source, cash profit; datasets |
| `get_finance_projects` | `domain`, `sort` (revenue, cost, profit, margin, hours), `ascending`, `project_ids`, `limit` ≤25 | `IDomainFinanceService.ProjectsAsync` | `financial:view` (S) | projects ranked in the tool (unknown margins last) |
| `get_money_attention` | `domain` | `IDomainFinanceService.AttentionAsync` → `Business/Finance/MoneyAttention.Of` | `financial:view` (S) | up to 6 follow-ups: overdue invoices, finished but owed, no payment, cost >85% (Design) |
| `list_invoices` ★ | `project_id, client_id, status, overdue, domain, issued_in`+dates, `limit` | `IInvoiceService.ListAsync` (100 rows) | `financial:view` (S) | rows, totals in the invoice currency, a USD dataset of up to 100 rows |
| `list_payments` ★ | `project_id, client_id, invoice_id, include_voided, domain, received_in`+dates, `limit` | `IPaymentService.ListAsync` | `financial:view` (S) | rows in USD, a dataset of live payments |
| `get_invoice` | `invoice_id` | `IInvoiceService.GetAsync` | `financial:view` (S) | one invoice: lines, paid, balance, terms, deliveries, void reason |
| `get_company_overview` | `period` (≤366 days), `from`, `to` | `ICompanyOverviewService.GetAsync` (its own payment and invoice queries, see §9) | `company:view` (S) | per domain received, project cost, cash profit, margin; overhead; invoiced, collected, outstanding. **Admin salaries and net profit are deliberately not passed on** |
| `list_overhead` | `include_ended` | `IOverheadService.GetAsync`, `GroupsAsync` | `company:view` or `overhead:manage` (S) | recurring company expenses, this month's amount, amount changes, categories |
| `list_expenses` | `project_id`, `limit` ≤50 | `IExpenseService.ListAsync` | `expense:manage` or `financial:view` (S) | a Design project's expenses, a total per currency |
| `compare_money` ★ heavy | `domain`, `period`+compare options, `group_by` (source, project, total), `sort`, `limit` | `ProtonAnalysisService.MoneyAsync` → `IDomainFinanceService.ProjectPeriodsAsync` | `financial:view` (S) | two periods side by side; diff, %, shares, notable flags and top movers computed **in the tool** |

### 3.2 Execution sites (Execution only)

| Tool | Parameters | Implementation | Permission (holders) | Output |
|---|---|---|---|---|
| `get_site_summary` | `project_id` | `ISiteRecordsService.SummariesAsync` | `project:view` (S A E); costs only with `financial:view` | counts of expenses and labour weeks, their costs |
| `list_site_expenses` | `project_id, spent_in`+dates, `category, search, limit` | `ISiteExpenseService.ListAsync` | `project:view` (S A E) | expenses with costs, supplier, account label; totals matching and all. **Costs shown to anyone who sees the site** (as the page does) |
| `get_labour` | `project_id, week` | `ILabourService.ListWeeksAsync`, `GetDefaultsAsync`, `GetWeekAsync` | `project:view` (S A E) | up to 20 weeks, totals, crew defaults with day rate (a site cost, decision 13) |
| `list_boqs`, `get_boq` | `project_id` / `boq_id` | `IBoqService.ListAsync`/`GetAsync` | `boq:create`, `financial:view` or `project:pricing_manage` (S) | BOQs and VOs, work and permit lines, adjustment, supervision, TVA, what the client pays |
| `get_site_account` | `project_id` | `IAccountFiguresService.GetAsync` + `ISupervisionWithdrawalService.ListAsync` | `financial:view` (S) | work, permits, supervision, TVA, received, owed, costs, profit, supervision due, taken and left, pocket moves, cash in account; by label, category and 15 largest payees |
| `get_pocket_money` | `project_id, moved_in`+dates, `limit` | `IPocketService.GetAsync`, `EntriesAsync` | `financial:view` (S) | balance, pending requests, movements |
| `list_site_logs` / `list_my_site_logs` / `list_my_sites` | site, person, period, `search`, `limit` | `ISiteLogService` | `site_log:view_all` (S A) / `site_log:create` (E) | daily-log text (the staff's own words, data), sites |

### 3.3 Projects, work, people, clients

| Tool | Parameters | Implementation | Permission (holders) | Output |
|---|---|---|---|---|
| `find` ★ | `text`, `types` | project, client, employee and website-project lists | each type by its own list permission | ids by name (ranked in the tool); archived projects too |
| `list_projects` ★ | `status, domain, client_id, owner_id, search, overdue_only, year, quarter, sort, limit` | `IProjectService.ListAsync` | `project:view` (S A E); work columns `work_entry:view_all`, money `financial:view` | rows with overdue computed in the tool (`Business/Projects/ProjectOverdue.cs`); dataset. Leaves `ExcludeArchived` false, so "all" includes archived projects, unlike the app's "All" |
| `get_project` ★ | `project_id` | `IProjectService.GetAsync` | `project:view` (S A E) | details, site staff, department names; no money |
| `get_project_requirements` | `project_id, open_only` | `IRequirementService.ListAsync` | `requirement:view` (S A E) | up to 40 requirements |
| `get_project_team` | `project_id`, period | `IWorkInsightsService.ProjectWorkAsync` (Design; also needs `work_entry:view_all`) | `project:view` | Design: hours per contributor, weekly dataset (the service's per-person cost is dropped); site: staff |
| `get_project_departments` | `project_id` | `IProjectDepartmentService.GetAsync`, `HistoryAsync` | `project:view`; money and history with `financial:view` | shares, budget, cost, left, minutes; people's minutes (cost removed on the fixes branch) |
| `list_design_vos` | `project_id` | `IDesignVoService.ListAsync`, `OutsideScopeAsync` | `financial:view` (S) | VOs, lines, status, split, TVA amounts; applied and pending totals summed in the tool; hours outside scope. Doesn't say whether a VO carries TVA |
| `list_work_entries` ★ | `user_id, project_id, client_id`, period, `search, limit` | `IWorkEntryQueryService.ListAsync` | `work_entry:view_all` (S A) | entries with descriptions, overtime minutes |
| `get_work_entry` | `work_entry_id` | `IWorkEntryQueryService.GetAsync` | `view_all` or own | one entry and comments |
| `list_my_work`, `list_my_deleted_work` | period, `project_id, limit` | `ListMineAsync`, `ListMyDeletedAsync` | `work_entry:view_own` (E) | own entries |
| `get_time_summary` ★ | period, `project_id, user_id, client_id, internal_only` | `IReportService.WorkReportAsync` | `report:view` (S A) | hours by project, person, client, week; top lists; datasets (the service's money block is ignored) |
| `get_work_overview` | `period` (month, quarter, last 30 days) | `IReportService.OverviewAsync` | `report:view` (S A) | the Design overview, attention list |
| `get_person` ★ | `user_id` | `IEmployeeService.GetAsync` | `employee:view` (S A) | profile, **no pay** |
| `get_person_work` ★ | `user_id`, period | `IMyWorkService.SummaryForPersonAsync` | `work_entry:view_all` (S A) | hours today, this and last week, month, quarter, all time; datasets |
| `list_people_activity` | `team_id, search, inactive_for_days, sort, limit` | `IWorkInsightsService.PeopleAsync` | `work_entry:view_all` (S A) | who logs what recently (rates and costs in the response are not copied) |
| `list_teams` | — | `ITeamService.ListAsync` | rule-based | departments |
| `get_permissions` | `user_id, search` | `IPermissionManagementService` | `permission:manage` (S) | permissions and overrides |
| `list_hour_excuses` | `user_id, month` | `ISalaryService.GetAsync` (runs the whole salary calculation, returns only excuses) | `salary:view` (S) | excuses, paid or not, **never an amount** |
| `list_clients` ★, `get_client` ★ | filters, sorts / `client_id` | `IClientService` → `MoneyRollup.ClientMoney` | `client:view` (S A); money `financial:view` | clients, contacts, hours, money rollups |
| `get_recent_activity` ★ | `domain, project_id, client_id, user_id, group`, period, `limit` | `IActivityFeedService.ListAsync` | `activity:view` (S A); amounts only with `financial:view` | the feed, with units added in the tool |
| `summarize_changes` ★ | period+compare, `domain, project_id` | `IActivityFeedService.CountsAsync` + `ListAsync` (≤3 pages) | `activity:view` (S A) | counts by kind, compared; pay events left out |
| `list_meetings` | period ≤92 days, `project_id, domain` | `IMeetingService.ListAsync` | `project:view` | meetings |
| `get_domain_dashboard` ★ | `domain` | `IDashboardService` | `report:view` (S A) | counts and deadlines, no money |
| `compare_work` ★ | period+compare, `group_by, project_id, person_id, client_id, limit` | `IReportService.WorkTotalsAsync` | `report:view` (S A) | hours compared; diff, shares, notable computed in the tool |
| `review_project` ★ heavy | `project_id`, period+compare | project, hours, requirements, money, signals, each by its own endpoint's permission | `project:view` | one project's state; omissions listed |
| `get_company_summary` heavy | period+compare | projects, hours, money, activity, overhead, attention, each gated | `project:view` | the company at a glance |
| `get_attention` ★ heavy | `domain, limit` ≤10 | `ProtonAnalysisService.SignalsAsync` + `ProtonSignals.Rank` | `project:view`; each source gated | ranked signals (§2.D) |

### 3.4 Messaging, files, portal, website, settings, UI

| Tool | Purpose | Permission |
|---|---|---|
| `find_recipient`, `get_my_inbox`, `get_my_notifications` | contacts and chats; unread counts (**never message bodies**); notifications | rule-based |
| `get_project_files`, `get_file_comments`, `read_project_file` (heavy, off until the company allows it), `get_portal_status`, `read_discussion` (heavy) | a Design project's files, comments, a file's content for the model, the client portal, the discussion with the client | files: `project:view`; portal: `client_portal:manage` or `client_access:manage`; discussion: participants only |
| `get_website_projects`, `get_website_content`, `get_website_analytics` (heavy) | the public website and its Google Analytics totals | `website:view`/`manage`, `website_analytics:view` (S W) |
| `get_company_settings`, `get_domain_settings`, `get_my_board_layout`, `list_my_devices`, `get_my_notification_settings` | settings everyone may read; the person's own layout, phones, push settings | rule-based |
| `open_record` ★, `open_page` ★, `open_surface` ★, `fill_form`, `offer_choices` ★, `suggest_replies` ★, `set_preference`, `refresh_page` ★ | navigation and form filling (see §2.C); each checks the target's own permission | gated by `project:view`, plus the target's own |
| `present_data` ★, `add_up` ★ | show a dataset; total a column in code | `project:view`; read nothing |

### 3.5 Propose (mutation) tools

Each one stores a card; the change happens only on Confirm (see [§2.B](#b-read-tools-and-write-tools-how-they-differ)).

- **Confirm strength:**
  - *Standard*: one click (Core).
  - *Strong*: Review then Confirm; often with a typed phrase (the record's name, the amount, an invoice number or an email address).
- **Money:** amounts must be ones the person typed in the conversation (`MoneyText.RequireTyped`).
- **Coverage:** `Proton/Actions/ProtonCoverage.cs` classifies all 244 write endpoints:
  - 168 built actions;
  - 14 planned: site staff, daily logs, pocket money, supervision taken, invoice email, plans and schedules, a payment's label, a Design VO's TVA;
  - 18 only opened for the person: uploads, passwords, pay, deleting a project;
  - 31 not the signed-in person's to do; 2 read-only POSTs; 11 Proton's own controls.

| Area | Tools (family, confirm) | Financially relevant notes |
|---|---|---|
| Projects | `propose_create_project`, `propose_update_project`, `propose_change_project_status` (Core, 1 click); `propose_project_lifecycle` (Lifecycle, typed name); `propose_project_billing`, `propose_project_money` (Money, typed name or amount) | creating sets no money; correcting a Design price needs a reason; completing locks revenue |
| Design | `propose_project_departments` (Core); `propose_department_shares`, `propose_design_vo`, `propose_send_design_vo` (Money, typed); `propose_blocked_change` (Money) | a VO the profit floor refuses stays pending as a blocked change |
| Execution | `propose_boq`, `propose_edit_boq`, `propose_boq_agreement`, `propose_send_boq`, `propose_site_pricing`, `propose_add_site_expenses`, `propose_site_expense_step`, `propose_submit_labour_week`, `propose_labour_defaults` (Money) | agreeing a BOQ sets the site's price; site expenses need an exchange rate on their day |
| Billing and company money | `propose_create_invoice`, `propose_update_invoice`, `propose_invoice_step`, `propose_record_invoice_delivery`, `propose_record_payment`, `propose_payment_step`, `propose_add_expense`, `propose_delete_expense`, `propose_billing_settings`, `propose_exchange_rate`, `propose_overhead`, `propose_proton_top_up` (Money) | payments at the dated rate in force, never one Proton sets; bank details never in a card |
| Pay rules and settings | `propose_working_time`, `propose_schedule`, `propose_hour_excuse` (PayRules, never an amount); `propose_company_settings`, `propose_domain_settings`, `propose_company_proton`, `propose_company_contact_email`, `propose_remove_company_logo`, `propose_account_label` (CompanyRules) | |
| Requirements, meetings, clients | `propose_add_requirement`, `propose_update_requirement`, `propose_set_requirement_done`, `propose_schedule_meeting`, `propose_update_meeting`, `propose_create_general_meeting`, `propose_create_client`, `propose_update_client`, `propose_set_client_archived` (Core); `propose_delete` (Deletes: a requirement, meeting, unused client or file) | archiving a client ends its contacts' portal access |
| Own work (employees) | `propose_log_work`, `propose_update_my_work`, `propose_delete_my_work`, `propose_restore_my_work`, `propose_set_work_entry_scope`, `propose_comment_work_entry` (Core) | logged hours are the input of Design labour cost |
| People and access | `propose_update_employee`, `propose_employee_access`, `propose_deactivate_employee`, `propose_reactivate_employee`, `propose_end_sessions`, `propose_permissions`, `propose_invitation` (Access); `propose_create_team`, `propose_rename_team` (Core), `propose_delete_team` (Deletes) | never pay |
| Messages and notifications | `propose_send_message`, `propose_send_announcement`, `propose_report_bug` (Messaging); `propose_mark_chat_read`, `propose_mark_notifications_read` (Core) | |
| Files, portal, discussion | `propose_rename_file`, `propose_restore_file`, `propose_reorder_files`, `propose_file_for_client`, `propose_comment_on_file`, `propose_edit_file_comment`, `propose_set_comment_fixed`, `propose_mark_discussion_read` (Core); `propose_delete_file_comment` (Deletes); `propose_portal_invitation`, `propose_portal_end_access`, `propose_portal_heroes`, `propose_portal_summary`, `propose_portal_step` (Access); `propose_discussion_post`, `propose_discussion_change` (Messaging) | |
| Public website | `propose_website_project`, `propose_website_service`, `propose_website_location`, `propose_website_social_link`, `propose_website_order`, `propose_website_picture`, `propose_website_conveyor`, `propose_remove_website_favicon`, `propose_website_under_construction` (Website) | |
| Own settings | `propose_update_my_profile`, `propose_arrange_board`, `propose_my_proton_suggestions`, `propose_notification_settings` (Core); `propose_remove_my_photo` (Deletes), `propose_remove_device` (Access) | |

Each tool's endpoint, permission and confirm phrase are tabled in [proton-audit.md](proton-audit.md), §2.

---

## 4. Financial Data Model

### 4.1 Entities that carry money

[Code] `Models/*.cs`, `Configuration/ModelConfig/*`

| Entity | Key money fields | Changed through | History kept |
|---|---|---|---|
| `Project` | `BillingType` (Client / Internal), `Revenue` (Design: the agreed price net of TVA, with applied VOs added; Execution: 0 while open, the frozen agreed amount once finalised), `ProjectFee` (Execution work price), `PermitsPrice`, `SupervisionRate` (default 0.10), `TvaRate` (default 0.11 client, 0 internal), `SettledOwedUsd` / `SettledOwedAs`, `RevenueLockedAt`, `FinalizedAt`, `CompletedAt`, `ProgressPct`, `StartDate`, `TargetDate`, `Status` | `ProjectService.UpdatePaymentAsync` (Design price, `financial:write`), `UpdatePricingAsync` (rates, `project:pricing_manage`), `UpdateBillingAsync`; `BoqService.SetPrice` (fee, permits); `ProjectStatusService` (lock, freeze, settle) | current values only, edited in place; history in `ProjectPaymentChange` and `ActivityEvent` |
| `ProjectPaymentChange` | `Amount`/`PreviousAmount`, `TvaRate`/`PreviousTvaRate`, `SupervisionRate`/`Previous…`, `Permits`/`Previous…`, `Description`, `ChangedAt`, `ChangedById` (legacy `MaterialMarkup*` unused) | appended on create, price corrections, rate changes, VO applied, BOQ agreed or withdrawn, made internal, owed settled | **yes**, append-only |
| `DesignVo` (+ `DesignVoLine`, `DesignVoAllocation`) | `Status` (Pending, Applied, Cancelled), `TotalUsd` (net, may be negative), `TvaRate` (0 if no TVA), `Split` | `DesignVoService` create, confirm, cancel, `SetTvaAsync` | status timestamps; **`SetTva` overwrites `TvaRate` in place with an event but no previous value** |
| `ProjectDepartment` (+ `ProjectDepartmentChange`) | `Share` (fractions adding up to 1) | `ProjectDepartmentService` | JSON before and after, reason, who, when |
| `Boq` (+ `BoqLine`, `BoqDelivery`) | `Kind` (client, VO, internal draft), `Status`, `SubtotalUsd`, `TotalUsd`, `PermitsUsd`, adjustment; lines: quantity, waste, rate, `Nature` (work or permit) | `BoqService` | drafts edited in place (`UpdatedAt`); agreeing and withdrawing in events and payment changes; **no snapshot of an agreed BOQ** |
| `Invoice` (+ `InvoiceLine`, `InvoiceDelivery`, `InvoiceSequence`) | `Number`, `Currency` (the project's, USD in practice), `Total` (no TVA split), `TermsDays`, `IssuedAt`, `DueAt`, void fields, `SendOn` (scheduled) | `InvoiceService` | drafts edited in place; issued invoices are immutable (void and reissue) |
| `Payment` | `Amount`, `Currency`, `FxRate`, `AmountUsd`, `TvaUsd` (frozen when recorded), `ReceivedOn`, `InvoiceId` (null = on account), `AccountLabelId`, `IsInitial`, void fields | `PaymentService` | never edited; only its invoice link and label can change (with events) |
| `ExchangeRate` | `Currency`, `RatePerUsd`, `EffectiveFrom` | `BillingSettingsService` | append-only; a new rate must be later than the last, never in the future |
| `ProjectExpense` (Design) | `Label`, `Amount`, `Currency`, `Category` (a fixed list of strings), `SpentOn`, `Note`, `DeletedAt` | `ExpenseService` create and delete only (**no edit**) | soft delete; events |
| `SiteExpense` (Execution) | `Name`, `Quantity`, `Unit`, `UnitCost`, `Amount`, `Total` (generated), `Currency`, `Supplier`, `Category` (enum), `AccountLabelId`, `SpentOn`, `DeletedAt` | `SiteExpenseService` add (1–100), update, delete | edited in place (`UpdatedAt`); events carry the previous cost |
| `LabourWeek` + `LabourWeekDay` + `ProjectLabourDefault` | days: `Workers`, `DailyPay`, `Cost` (generated); defaults dated `EffectiveFrom` | `LabourService` | **re-submitting a week replaces its days** (`LabourService.cs:309`); the event has only the old and new total |
| `OverheadItem` + `OverheadAmount` + `OverheadGroup` | dated monthly amounts, currency, `StartedOn`, `EndedOn` | `OverheadService` | append-only amounts; changes only from the current month on |
| `UserPayHistory` | `EffectiveFrom`, `Basis` (Hourly, Monthly), `Amount`, `Currency`, `OvertimeRate` | `EmployeeService` pay change | **append-only**; back-dated changes re-price work since then |
| `WorkingTimePolicy`, `UserSchedule` | dated company week, break, overtime multiplier, overtime threshold; personal weeks | `WorkingTimeService`, `ScheduleService` | dated rows |
| `WorkEntry` | `UserId`, `ProjectId`, start, end, `DurationMinutes`, `OutsideScope`, `TeamId`, `DeletedAt` | `WorkEntryService` | soft delete; closed months can't change |
| `HourExcuse` (+ `HourExcuseDay`) | `MonthStart`, `Kind`, `Minutes`, `IsPaid` | `HourExcuseService` | soft delete |
| `SupervisionWithdrawal` | `AmountUsd`, `TakenOn` | `SupervisionWithdrawalService` | soft delete |
| `PocketEntry`, `PocketRequest` | signed `Amount` from the pocket's side; request status and amounts | `PocketService` | never edited |
| `AccountLabel` | name, position, `ArchivedAt` | `AccountLabelService` | archive only |
| `BlockedChange` | `Kind`, JSON `Payload` (the refused request), status, who and when | `BlockedChangeRecorder`, `BlockedChangeService` | kept |
| `ActivityEvent` | `Kind`, `Amount`, `PrevAmount`, `Payload`, `ActorId`, `OccurredAt`, `IsDemo` | everywhere | append-only; serves as the audit log |

### 4.2 What exists, and what does not

| Information | Status | Detail |
|---|---|---|
| Client contract value | **exists** | Design: typed in plus applied VOs. Execution: agreed client BOQ plus agreed VOs (work), plus supervision, plus permits. Editable only while unlocked [Code] `Business/Projects/ProjectRules.cs:117` |
| Contract amendments | **exists** | Design VOs; Execution VOs; price corrections with a reason; all in `ProjectPaymentChange` |
| Financial completion | **exists** | completing (or archiving an unfinished project) "finalises": revenue locked, Execution amount frozen [Code] `ProjectStatusRules.cs:47`, `ProjectStatusService.FinaliseAsync` |
| Internal projects | **exists** | `BillingType.Internal`: no client, revenue 0, TVA 0, no invoices or payments; Execution can't be internal (database check) |
| Actual costs | **exists** | labour (logged hours), Design expenses, site expenses, ground labour, each dated and converted at its day's rate |
| Expense categories | **partly** | three unrelated taxonomies: Design `ExpenseRules.Categories` (assets, services, contractor, software, travel, hardware, other); Execution `SiteExpenseCategory` (materials, contractor, equipment, fees, other); overhead groups; plus account labels on site costs and payments |
| Project-level summaries | **exists** | `ProjectFinancials` / `ProjectMoneyResponse` |
| Expected costs or budgets | **missing** | no cost budget, estimate, planned hours or target margin per project or department. Design department "budget" = price × share (revenue) |
| Project-specific recurring expenses | **missing** | only company overhead recurs |
| Committed or future costs | **missing** | no purchase order, supplier invoice, payable, subcontract or accrual entity |
| Construction progress | **missing** | `BoqLine` has no executed quantity or percentage; `BoqDelivery` is a send to the client |
| Project type or size attributes | **missing** | nothing to group "similar" projects beyond domain, client, price, dates and location text |

---

## 5. Revenue and Payment Workflows

### 5.1 The five concepts are distinct in code

| Concept | Formula | Net or gross | Where |
|---|---|---|---|
| **Contract value** (agreed revenue) | Design: `Project.Revenue`. Execution open: `PriceOf(work, permits, rate, tva).Net` = work + round(work × supervision rate) + permits. Execution locked: frozen `Project.Revenue` | net of TVA | [Code] `Services/Finance/Implementation/ProjectFinanceService.cs:~263-325`; `Business/Projects/ExecutionPricing.cs:28` |
| Agreed with TVA | Design: `Tva.Gross(revenue, rate, untaxedVOs)`; Execution: `PriceOf().Gross` = net + round(work × TVA) (TVA on work only) | gross | `Business/Billing/Tva.cs`, `ExecutionPricing.cs` |
| **Recognised revenue** | `round(revenue × progress)`, progress = 1 when done, else `ProgressPct/100` (requirements done ÷ total) | net | `Business/Finance/ProjectFinance.cs:~278-282` |
| **Invoiced** | Σ totals of issued, unvoided invoices (at the issue-day rate) | gross | `Business/Billing/ProjectBilling.cs:66` |
| **Collected cash** | project billing: Σ `Payment.AmountUsd` (gross); period revenue: Σ (`AmountUsd − TvaUsd`) by `ReceivedOn` | gross for billing, **net** for period revenue | `ProjectBilling.Summarise`; `Business/Finance/PeriodMoney.cs:22` |
| **Outstanding receivables** | Σ invoice balances (max(0, total − paid on it)) | gross | `Business/Billing/InvoiceRules.cs` |
| Owed | max(0, agreed with TVA − received), whether invoiced or not | gross | `ProjectBilling.Summarise` |
| Overdue, aging | open invoices past `DueOn`, in buckets not due / 1-30 / 31-60 / 61-90 / 91+ | gross | `InvoiceRules.IsOverdue`, `ProjectBilling.Aging` |
| Left to invoice, not yet invoiced, on account, paid ahead | agreed with TVA − invoiced (−on account); payments without an invoice; received − agreed with TVA | gross | `ProjectBilling.Summarise` |
| **Expected revenue** in a period | Σ (amount − previous) of `revenue_set/changed`, `project_fee_set` (× (1 + **today's** supervision rate)) and `permits_set` events in the period | net | `Business/Finance/ExpectedRevenue.cs`; `Services/MoneySummaries/Implementation/DomainFinanceService.cs:137-142` |

Watch the name: `ProjectBilling.Summary.Revenue` (and `BillingPositionResponse.Revenue`) is **gross**, while `ProjectFinance.Summary.Revenue` is **net**. [Code] `MoneyRollup.cs:49`

### 5.2 Workflows

- **Partial payments:** many payments per invoice; status `Partial` is derived. An invoice payment can't exceed its balance; payments without an invoice are "on account" and are applied automatically when an invoice is issued (each whole, oldest first, only if it fits). [Code] `Business/Billing/PaymentRules.cs:57`, `InvoiceRules.Status`
- **Dates:** payments have `ReceivedOn`; invoices `IssuedAt`, `DueAt` (issue + `TermsDays`), `SendOn` (scheduled). Payment plans split what is left into 2–24 scheduled drafts. [Code] `InvoiceService.CreatePlanAsync`
- **Refunds, credit notes, invoice discounts:** **none exist**. [Code]
- **Discounts and write-offs:** only at completion. `settleOwed` (`excused` or `written_off`) lowers the price to what was received: a closing VO with a negative amount on a site, a lower payment on Design. It is recorded in `SettledOwedUsd`/`As`, the payment history and the feed (`owed_excused`, `owed_written_off`). The two kinds are numerically identical. [Code] `ProjectStatusService.cs:123-183`
  - **[Code] It bypasses the profit floor:** `ProjectStatusService` has no profit-floor call. [Ask] Q10.
- **Keeping an overpayment:** `keepPaidAhead` raises the price to what was received. [Code] **Its endpoint checks only `project:change_status`**; it doesn't check the money-writer permission that `settleOwed` checks (`ProjectStatusService.cs:107-117`).
- **History when amounts change:** price and rate changes go to `ProjectPaymentChange` (old and new values, who, when). Payment money is never edited. A Design VO's TVA switch keeps no previous value.
- **Currencies:** a payment stores its own rate (typed, or the dated rate in force, else refused), so payments are never "unconverted". Invoices follow `Project.RevenueCurrency`, which is always USD in practice (nothing writes it after creation).

---

## 6. Expense and Cost Workflows

| Cost | Created, edited | Date, category, description, currency | Several projects? | Deleted, audit |
|---|---|---|---|---|
| Design project expense | `POST`, `DELETE` only; no edit (remove and re-add) | `SpentOn`, category from a fixed list, label and note, any currency; **no exchange-rate check on creation**, so an LBP expense with no rate is saved and then sits in "unconverted", outside cost, profit and the profit floor | no (one `ProjectId`) | soft delete; `expense_added`/`removed` events |
| Execution site expense | add (batch), update, delete; own entries, or anyone's with `material:manage` | `SpentOn`, enum category, item name, quantity, unit, unit cost or lump sum, supplier, account label, USD or LBP (**refused without a rate that day**, `exchange_rate_missing`) | no | soft delete; events carry the previous cost; edited in place |
| Ground labour | weekly report per site, Monday–Saturday, no future days; dated crew defaults | USD only | no | re-submission replaces the days; the event keeps totals only |
| Company overhead | monthly recurring lines, dated amount changes from the current month on, cancel and resume | monthly amounts, currency per line (a change of currency ends the line and starts a new one) | not allocated to projects | append-only amounts |
| Labour (Design) | from work entries | see §7 | one project per entry | soft delete; closed months frozen |

- **Recurring project expenses, future commitments, purchase orders, supplier invoices:** none. `SpentOn` is the only date on any cost. [Code]
- **Materials against labour:**
  - by cost source (`Enums/CostSource.cs`: Labour, Expenses, SiteExpense, GroundLabour);
  - within site expenses by `SiteExpenseCategory.Materials`;
  - no material quantity is tied to a BOQ line.

---

## 7. Employee and Resource Cost Model

### 7.1 Pay

[Code] `Business/Pay/PayRules.cs`, `Business/Pay/PayChangePolicy.cs`, `Business/Pay/RoleHistory.cs`

- **Pay history** (`UserPayHistory`):
  - dated, append-only;
  - hourly or monthly, in USD or LBP;
  - an overtime rate is stored on each row (suggested: salary ÷ threshold, or 184).
- **Fixed salaries:**
  - admins and super admins (`PayRules.HasFixedSalary(role) => role != employee`); a month with any day as admin is a fixed-salary month;
  - Execution employees must be paid monthly.
- **Hourly rate of a salary:** `round(salary × 60 ÷ PaidMinutesInMonth, 4)`, where the paid minutes are the person's own scheduled day spans in that month, the break included (`PayRules.HourlyRate:24`, `PaidMinutesInMonth:48`). The rate varies month to month.

### 7.2 Labour cost of a project

[Code] `ProjectFinanceService.LoadAsync`, `CostItems:~239`; `ProjectFinance.PriceLabour:92`, `RatesOf:142`, `RateOn:190`; `Business/Finance/PaidBreaks.cs`; `Business/Payroll/Overtime.cs`, `SalaryCalculator.cs`

- **Per entry:** `(minutes − paid overtime minutes)/60 × hourly cost on that day + paid overtime minutes/60 × overtime rate × multiplier`.
  - The day is the entry's start day in company time.
  - Time before a person's first pay row uses their earliest rate.
  - A person with no pay row adds "unpriced minutes" and no cost.
- **Paid overtime:**
  - comes from the salary calculation's entry splits;
  - from 1 November 2026 a salary pays overtime only past the month's threshold, max(policy threshold or 184, expected hours);
  - that premium is "a company cost" (`docs/company.md:121`), so projects carry no premium for salaried people (`EntrySplit.NoPremium`).
- **Paid breaks:**
  - each day's paid break minutes are shared across that day's projects by in-hours minutes, at that day's rate;
  - zero from 1 December 2026 (the policy row sets the break to 0).
- **Fixed-salary months** price at 0 (`ProjectFinance.ByRole`): admins' logged hours cost nothing.
- **Conversion:** each cost is converted at its own day's exchange rate; a cost with no rate goes to "unconverted".
- **Shared use:** the same `PriceLabour` serves the project, the per-person views (`Services/WorkEntries/Implementation/LabourPricer.cs`) and the departments (`LabourByDepartmentAsync`).
- **Tests:** `ID-Indetails.UnitTests/Business/ProjectFinanceTests.cs`, `PaidBreaksTests.cs`, `PayrollTests.cs`, `OvertimeThresholdTests.cs`; integration `ProjectFinanceTests`, `ProjectPayTests`, `PersonLabourCostTests`.

### 7.3 Is project labour cost reliable?

[Conclusion]

**Yes for Design, as "the cost of logged hours at the rate in force that day".** It is deterministic, dated and tested, and it agrees with Salaries for the hours it covers.

**No as "what the company really pays for a project".** What stays outside every profit figure [Code]:
1. **Execution employees' salaries** (site staff):
   - they log no hours, so no site carries them;
   - they aren't fixed-salary roles, so the company overview's salary line (`CompanyOverviewService.FixedSalariesAsync`, admins and super admins only) leaves them out too.
2. **Design employees' paid, unlogged time:** paid excuses, leave and unlogged days.
3. **Admins' and super admins' time on projects:** costed 0. Their salaries are a company cost, not spread over projects.
4. **The threshold overtime premium:** "a company cost" in the docs, but I found no figure that includes it.
5. **People with no pay row:** their time is unpriced.
6. **Design expenses in LBP with no rate:** they drop out silently.

**Hours attribution:**
- One project per entry.
- A department via `WorkEntry.TeamId`.
- An "outside the original scope" flag.
- **No activity type and no non-project time.**
- Holidays and leave exist only as hour excuses; there is no holiday calendar.

**Utilization data that exists (Design):**
- logged minutes by project and day;
- expected hours, excused, short, made up, overtime (`Business/Payroll/HoursLedger.cs`, `MyTime.cs`);
- department hours;
- outside-scope hours.

---

## 8. Design vs. Execution Differences

| | Design | Execution |
|---|---|---|
| Price | typed in (net), plus applied Design VOs | agreed client BOQ + agreed VOs (work), + supervision (work × rate) + permits; TVA on work only |
| Revenue while open | `Revenue` field | computed live from fee, permits, rates; frozen at completion |
| Costs | labour (logged hours), project expenses | site expenses, ground labour; labour normally 0 (staff log no hours) |
| People | employees log hours; departments with shares and budgets | site staff (optional) write daily logs (text, no hours, no money) |
| Extra money | — | supervision taken (not a cost), pocket money (moves profit), account labels, an initial payment |
| Profit | agreed: revenue − cost | agreed: revenue − cost + (from pocket − to pocket); cash: received net of TVA − cost − to pocket + from pocket |
| Progress | requirements ticked ÷ total | the same; **no BOQ quantity progress** |
| Baselines | department budget = price × share (+ VO amounts) | internal BOQ drafts (not linked); the agreed BOQ (price, not cost) |
| Existing forecasts | projected cost = cost ÷ progress (client work, ≥5% progress) | the same, plus a labour "runway": working days until cash profit is spent at the crew's daily pay (`Business/Labour/LabourRules.ProjectZero`) |
| Shared entities | `Project`, `ProjectRequirement`, `ProjectMeeting`, `Invoice`, `Payment`, `Client` | the same |

[Conclusion] One forecasting formula cannot serve both domains.
- **Design** costs are almost all labour, and its natural driver is hours against a budget.
- **Execution** costs are materials, subcontractors and crews, driven by quantities against the BOQ, which nobody records.

---

## 9. Existing Financial Calculations and Reporting

### 9.1 Calculations

| Calculation | Formula | Implemented in | Shared / duplicated | Tests | Ambiguities |
|---|---|---|---|---|---|
| Project cost | labour (+ breaks) + expenses + site expenses + ground labour, each converted at its day's rate and rounded per source | `ProjectFinance.ToUsd:193`, `Costs.Total:38` | shared | unit and integration `ProjectFinanceTests` | per-source rounding; period views sum unrounded items, so they can differ by a cent |
| Agreed profit | revenue − cost + pocket net (internal: revenue 0) | `ProjectFinance.Summarise:262-289` | shared | unit | `docs/projects.md` says "revenue − cost" and omits the pocket |
| Margin | profit ÷ revenue (4 dp), shown only if done, ≥5% progress or ≥10 h logged | `Summarise`, `MarginOf:291` | **duplicated** with `PeriodMoney.Margin` (same body) | unit | Execution rarely has 10 h; "done" excludes on hold |
| Recognised to date | revenue × progress | `Summarise:278` | shared | unit | progress is requirement counts |
| Projected cost and profit | cost ÷ progress; revenue − projected cost + pocket net | `Summarise:270-276` | shared | unit | linear; at 5% progress it multiplies cost 20× |
| Cost per hour | all cost ÷ logged hours | `Summarise:288` | shared | — | not an hourly rate (includes expenses) |
| Execution cash profit | received (gross) − its TVA − cost − to pocket + from pocket | `ProjectFinanceService.Calculate:317` | project only | `SiteMoneyTests`, `PocketTests` | the only project-level cash profit; Design has none |
| Cash by week, runway | cumulative received (net) and spent per week; days until cash profit is spent at crew rate | `ProjectFinance.CashByWeek:226`, `LabourRules.ProjectZero` | Execution page only | unit | runway counts ground labour only |
| Department budget and cost | budget = round(price before VOs × share) + VO amounts; cost = labour by department; left = budget − cost | `ProjectDepartmentService.DescribeAsync:~224-277`, `ProjectFinanceService.LabourByDepartmentAsync:65` | **re-prices labour** itself; treats unconverted labour as 0 | integration only | labour only; budgets may not sum to the price after rounding |
| Billing figures | see §5.1 | `ProjectBilling.Summarise` | shared via `MoneyRollup.Billing`; **re-derived** in `CompanyOverviewService` | unit and integration `MoneySummaryTests` | client and domain sums net overpayments against debts before the max |
| Period received, cost, "profit", margin | received net of TVA by `ReceivedOn`; costs by date; profit = received − round(cost) | `PeriodMoney`, `DomainFinanceService.SummaryOf:77-104` | shared with monthly, project periods, Proton; **duplicated** by the company overview | unit and integration | **no pocket money**, so a domain's period "cash profit" differs from a site's cash profit |
| Expected revenue | §5.1 | `ExpectedRevenue`, `DomainFinanceService.AgreedAsync:137` | domain only | `ExpectedRevenueTests` | uses today's supervision rate for past events |
| Money attention | overdue invoices, finished but owed, no payment, Design cost > 85% of revenue; 6 at most | `Business/Finance/MoneyAttention.cs:38` | shared (dashboard, scheduler, Proton) | unit | invoices without a rate are missed |
| Profit floor | applies to client projects with an agreed amount (Design revenue > 0, Execution fee > 0); at zero if profit ≤ 0, close if margin ≤ 10%; a change lowering profit to ≤ 0 is refused and kept | `Business/Projects/ProfitFloorRules.cs:28`, `Services/Projects/Implementation/ProfitFloor.cs:~33-93`; the scheduler warns | shared | `ProfitFloorRulesTests`, `ProfitFloorTests` | not applied to owed settlements |
| Site account | cash in account = payments (gross) − cost − supervision taken − to pocket + from pocket | `Business/Execution/AccountFiguresRules.cs:71` | own payment query | unit and integration | gross by design; differs from cash profit |
| Company overview | per domain: received net − cost; gross = Σ; net = gross − overhead (calendar days, rate on the 1st) − fixed salaries | `Services/Company/Implementation/CompanyOverviewService.cs:~52-260` | **parallel path** with its own queries | `CompanyOverviewTests` | parts rounded before subtracting (a known issue in `docs/status.md`); excludes the pocket, Execution staff salaries and the threshold premium; Proton gets no net profit or salaries |
| Payroll | base pro-rated, minus deduction, plus overtime (threshold) | `Business/Payroll/SalaryCalculator.Calculate:93` | shared with project cost via entry splits | `PayrollTests`, `OvertimeThresholdTests`, `SalaryTests` | totals per currency |
| Design work report money | labour in period + expenses in period; profit all-time | `ReportService.MoneyAsync:~362-399` | mixes period and all-time | `ReportTests` | |
| Proton | comparisons, shares, baselines, signals on top of the above | `ProtonTrends`, `ProtonSignals` | reuses the services; no money calculation of its own | `ProtonTrendsTests`, `ProtonRulesTests`, `ProtonMoneyTests` | rounded shares may not sum to 100 |

### 9.2 Duplications and traps

[Code] + [Conclusion]

1. **The company overview** computes per-domain received, cost and profit, and invoiced, collected and outstanding, with its own queries instead of `DomainFinanceService` and `ProjectBilling`. They agree today; nothing ties them.
2. **Margin** is implemented twice.
3. **Three "profits":**
   - agreed profit (with pocket);
   - Execution cash profit (with pocket, net of TVA);
   - period "cash profit" (without pocket).
   Proton and the docs use "cash profit" for both of the last two.
4. **Gross against net receipts:** account figures and the billing tab's "collected" are gross; period revenue and the company's "collected" are net.
5. **Department labour** prices labour again and counts unconverted labour as 0.
6. **No caching of money**, and no snapshots: every figure is recomputed from all history on every call. `DomainFinanceService` loads every project of a domain with all its history; the company overview loads every domain. [Docs] `docs/status.md` lists the slow loaders (paid breaks, overtime by whole months, one excuses query per month).
7. **[Conclusion]** A Financial Intelligence Engine must not add a fourth calculator. It should call `IProjectFinanceService`, `IDomainFinanceService` and `MoneyRollupService`, or extract the parallel paths into them first.

---

## 10. Authorization and Security Assessment

### 10.1 How it works

[Code]

| Layer | Implementation |
|---|---|
| Authentication | Two JWT bearer schemes behind a policy scheme: staff and portal (different audiences). 15-minute access tokens, refresh cookie for the web (hashed), body tokens for the phone. `OnTokenValidated` refuses tokens issued before a session revocation (`Extensions/AuthServiceCollectionExtensions.cs:45-150`) |
| Fallback | every endpoint requires a signed-in person with a changed password (`:160`) |
| Roles | `super_admin`, `admin`, `employee`. Defaults only; endpoints check permissions (`Constants/PermissionCatalog.cs:144-155`) |
| Domains | `design`, `execution`, `website`. One company, **not multi-tenant** (`Constants/DomainCodes.cs`) |
| Permissions | 64 codes, each with a code-only sensitivity tag (Money, Pay, Access, CompanyRules). `EffectivePermissions.Resolve`: role defaults in the home domain, plus grants, minus revokes. Super admins hold all but own-work permissions and see every domain. Admins and super admins never log hours |
| Domain visibility | `UserAccess.VisibleDomains` = home + any domain one of whose domain-limited permissions the person holds. `CanSee(null)` is true (company-level records) |
| Policy handler | `[HasPermission]` / `[HasAnyPermission]` → `PermissionHandler`, from `PermissionService` (in-memory, 10 minutes, `Forget(userId)` on change). The role in the token is never used |
| Service checks | services re-check with `actor.Require(...)` and refuse other domains as 404 (`SiteMoneyService.cs:29-40`, `AccountFiguresService.cs:36-41`, `DomainFinanceService.cs:446-465`; invoices and payments filter by visible domains). Money is **left out (null)**, not zeroed, without `financial:view` (`ProjectService.cs:91, 642`, `ClientService.cs:139`, `ReportService.cs:129`, `ActivityFeedService.cs:40`) |
| Money permissions | `financial:view` (reads), `financial:write` (Design price), `project:pricing_manage` (Execution rates), invoice, payment, expense, overhead, exchange-rate codes. **All super-admin-only by default** |
| Pay | `salary:view`, `employee:pay_manage`, `working_time:manage`; company-only. Proton never reads pay or salaries (`ProtonToolCatalogTests.No_tool_output_can_carry_the_excluded_fields`) |
| Super admin vs admin vs employee | Super admin: everything, every domain. Admin: own domain's work, no money, pay, hiring or client publishing. Employee: own work; never colleagues' hours or pay |
| Proton | tool calls run in the request's scope with the caller's `ClaimsPrincipal`; each tool's endpoint policy is evaluated with `IAuthorizationService` (cached per question); the service then checks again. Proton keeps no permission list of its own (`Proton/Tools/ProtonToolContext.cs:137-163`, `ProtonEndpoint.cs:103-114`) |

### 10.2 Paths outside the normal checks

[Code]

| Path | Risk |
|---|---|
| `IProjectFinanceService` and the repositories | **no authorization inside**. Callers must check. A new caller that forgets can read any project's money |
| No global EF query filters | domain and soft-delete filters are applied by hand; a missed `Where` returns other domains' or deleted rows |
| `InvoiceService.Sending` | a static super-admin `UserAccess` used to describe invoices for scheduled sending (`InvoiceService.cs:50`) |
| Background jobs | `NotificationScheduler`, `ScheduledNotifications` call services with no user and pick recipients with `HoldersAsync(domain, permission)`; money notifications carry percentages only |
| `ICurrentUser.ActAs` | used to force a blocked hours entry and to add a department as another person |
| `financial:view` is not domain-limited | anyone holding it sees money in every domain they can see; an aggregate "everything I can see" spans both businesses ([Docs] accepted item in `docs/security-audit.md`) |

### 10.3 Risks of multi-step financial investigations

[Conclusion]

1. **Aggregation leakage.**
   - Even when each read is allowed, combining reads can reveal what no single page shows. Today: a person's cost beside their minutes (fixed on the branch), or a one-person project's labour cost ÷ hours (`ProjectTools.cs:484-489`, `FinanceTools.cs:257-266`), both `financial:view` only.
   - Net profit with overhead reveals admin salaries, which is why Proton gets neither.
   - Every new tool output needs a "what can be derived from it" review, plus a catalogue test like the two that exist.
2. **Held datasets outlive permissions.** A dataset from an earlier tool stays usable for 15 minutes in the conversation; `present_data` and `add_up` re-check only `project:view`. A change of permissions ends the conversation, so this is low risk, but a longer-lived analysis store would need the same rule.
3. **Background analysis has no identity.** Proactive monitoring needs a decided actor and a rule for who receives what (§12).
4. **The database user can write.** Proton's read-only guarantee is the EF interceptor, which a raw SQL path or a new `DbContext` would bypass. There is no read replica.
5. **Load.** Heavy money reads load all history. Four per question is the current guard; a portfolio-wide engine would need snapshots or aggregates first.
6. **Saved output.** Answers, charts and tables are kept 90 days in saved chats, and 8,000 characters in `proton_usage`. Financial reports would inherit that retention, hidden when access is lost.
7. **What reaches the model provider.** Every figure a tool returns is sent to Anthropic, and with voice, the answers to OpenAI. Company files are off by default for that reason.

---

## 11. Historical Data and Data Quality Assessment

| Question | Answer |
|---|---|
| Created and updated timestamps | yes on every money entity (`CreatedAt`/`AddedAt`/`RecordedAt` + actor; `UpdatedAt` where editable) [Code] |
| Edits keep previous values | price and rates: yes (`ProjectPaymentChange`); payments: never edited; site expenses: in place (the event keeps the previous cost); labour weeks: days replaced (totals in the event); BOQ drafts: in place; Design VO TVA: in place without the previous rate |
| Deleted records | soft-deleted costs, work entries, BOQ drafts, supervision withdrawals; voided payments and invoices; **hard-deleted projects** (archived mistakes, with no money on them by rule) leave one `project_deleted` event |
| Snapshots | **none** (no monthly close, no project money snapshot); finalising freezes only the Execution amount in `projects.revenue` |
| Past costs reconstructable | mostly: costs are dated (`SpentOn`, entry day, week day), pay and rates are dated, payments have their own rate. But a backdated exchange rate or pay change **restates** past figures on the next read (by design) |
| Salary history | preserved (`UserPayHistory`, append-only) |
| Exchange rates dated | yes (`EffectiveFrom`; never edited) |
| Budget revisions | n/a: no budgets. Department share changes are kept (`ProjectDepartmentChange`) |
| Category consistency | three taxonomies (§4.2); Design categories are strings checked against a list |
| Progress history | `ProgressPct` is current only; the docs say the timeline rebuilds status and progress from activity events [Code] `Models/ActivityEvent.cs` (comment) |
| Demo rows | `ActivityEvent.IsDemo` marks seeded rows; analyses must leave them out |

**Can a project's financial state at a past date be rebuilt?** [Conclusion] Mostly, for cost and cash. Partly, for the agreed price. What blocks an exact replay:
1. labour-week day rows are overwritten;
2. site-expense edits overwrite date, currency and category (not confirmed whether events keep them);
3. expected revenue applies the current supervision rate;
4. a Design VO's TVA history is lost;
5. backdated rates restate history;
6. there are no snapshots to compare against.

**A monthly financial snapshot table would make point-in-time comparisons exact.** It is a schema change, so it needs approval.

---

## 12. Background Processing and Infrastructure

[Code]

| Item | Today |
|---|---|
| Scheduler | **No Hangfire, no Quartz.** `BackgroundService` + `PeriodicTimer`, registered in `Extensions/ApplicationServiceCollectionExtensions.cs` and `ProtonServiceCollectionExtensions.cs` |
| Jobs | `NotificationScheduler` (hourly: scheduled checks, auto-archive, push reminders; de-duplicated by `notification_sent_keys`, safe across instances), `NotificationCleanupJob`, `PushDispatcher` (in-memory queue), `PushReceiptsJob`, `DeviceCleanupJob`, `FileCleanupJob`, `PortalContentCleanupJob`, `ProtonHistoryCleanupJob`, `ProtonVoiceWatch` |
| Money checks that already run | `invoice_overdue`, `invoice_draft_waiting`, `profit_low`, `profit_at_zero`, scheduled invoice sending and reminders, `hours_short` (`Services/Notifications/Implementation/ScheduledNotifications.cs`). **Money notifications carry percentages only, never amounts** |
| Notifications | `INotificationSender` (not the actor, active people who can see the domain), in-app list, Expo push, Web Push, SignalR live signals (no content) |
| Email | SMTP through MailKit |
| Caching | `IMemoryCache` for permissions (10 min), company settings, working time, website analytics, the Proton briefing. **No money cache** |
| Logging | built-in console logger in JSON; categories `ID_Indetails.api.Proton`, `…Security`, `RequestCostMiddleware` (5xx or slower than 1 s). No Serilog |
| Monitoring | `/health`, `/api/health` (database check); no built-in alerting |
| Database | MySQL 8.4 on the same single Docker host as the API (production on a VPS); the API uses a restricted DML user; nightly dumps kept on the same server; no replica |
| Audit | `activity_events` (append-only, amounts in columns) and `proton_actions` (every proposal and its outcome) |
| Single-instance assumptions | permission cache, Proton conversations, voice sessions, budget holds, push queue, presence |

**What proactive financial monitoring could reuse** [Conclusion]
- the `NotificationScheduler` pattern (a scoped check per run, de-duplication keys);
- `HoldersAsync(domain, permission)` to choose recipients;
- the "percentages only" rule for money notifications;
- the pure rule classes (`ProfitFloorRules`, `MoneyAttention`, `ProtonSignals`).

**What is missing:**
- a system identity for an unattended analysis;
- a decision on whether an unattended run may call the model (cost, and data sent to the provider);
- a store for findings (to show trends and avoid repeating them);
- a per-recipient visibility rule for findings that combine domains.

---

## 13. External Integrations

[Code]

| Integration | How | Credentials |
|---|---|---|
| Anthropic (Proton) | `Anthropic` SDK, singleton client | `Proton:ApiKey` (user secrets locally, `PROTON_API_KEY` in production) |
| OpenAI (voice) | named `HttpClient`, WebRTC from the browser; the key stays on the server | `Proton:Voice:ApiKey` |
| Google Analytics 4 Data API | `Google.Apis.AnalyticsData.v1beta`, service account with a read-only scope; results cached and shared (no per-user data) | base64 service-account key in configuration |
| Expo push, Web Push | `AddHttpClient`; Web Push restricted to four push hosts | Expo token (optional), VAPID keys |
| SMTP | MailKit | options and secrets |
| Content store | Cloudflare R2 through the S3 SDK (local store in development, refused in production) | four R2 settings |
| Geocoding | Nominatim (`AddHttpClient`), GeoNames (not examined) | none |
| Meta, Facebook, Instagram, web search, other Google APIs | **none** | — |
| OAuth token storage | **none** (no user OAuth flows) | — |

Credentials are bound to options classes from configuration (user secrets locally, `.env` in Docker); production start-up refuses missing or development values.

**Keeping external data out of the calculation engine** [Conclusion]
- Treat external information (market prices, exchange-rate feeds, analytics) as **inputs stored as dated rows**, the way `ExchangeRate` already is.
- The calculators should read only stored rows. That keeps results reproducible and testable.
- Fetching belongs in a separate integration service or job, and every new source needs its own credentials, rate limits, failure mode and data-to-provider review.

---

## 14. Financial Intelligence Data Readiness Matrix

| Capability | Status | What exists | What is missing (exactly) |
|---|---|---|---|
| Project profitability | **READY** (as the app defines it) | agreed and cash profit, margin, cost by source, per project, client, domain, company | to be "true" profitability: costs for Execution staff, unlogged paid time, admin time and the threshold premium (Q1–Q4) |
| Budget variance | **PARTIALLY READY** (Design labour only) | Design department budget (price × share) against its labour cost | a cost budget per project (and per department or BOQ line), budget revisions (dated, with reason and approver), planned hours; for Execution, a cost budget per BOQ line or category |
| Forecast final cost | **PARTIALLY READY** | projected cost = cost ÷ requirement progress; dated cost history; Execution labour runway | a reliable progress measure (Design: planned hours or deliverables; Execution: executed quantity per BOQ line), planned end dates kept current, committed costs |
| Forecast project profit | **PARTIALLY READY** | agreed revenue and pending VOs; projected profit | the same as above, plus a rule for pending VOs and expected settlements |
| Expense anomaly detection | **PARTIALLY READY** | dated, categorised costs with supplier, unit cost and quantity (site); baselines (`ProtonTrends.Baseline`) | one category taxonomy (or a mapping), an item or material catalogue (item names are free text), thresholds per category (Q16), the reasons for exclusions (voided, deleted) |
| Labour cost analysis | **PARTIALLY READY** | Design labour by project, department, month, person (the person view is internal, never to Proton) | Execution staff attribution, non-project time, admin time policy (Q1–Q3) |
| Employee utilization | **PARTIALLY READY** (Design) | logged against expected hours, overtime, excuses, outside scope, by project and department | non-project or activity categories, capacity or availability, a leave calendar; Execution has no hours at all |
| Project risk detection | **PARTIALLY READY** | profit-floor standing, money attention, signals, overdue, idle, cost growth | budgets and a reliable forecast, to flag "will become unprofitable" rather than "is"; thresholds from the owner |
| Historical comparison | **PARTIALLY READY** | period comparisons (READY); cost history by date | project attributes for "similar" (type, size, scope), snapshots for point-in-time states, the history gaps in §11 |
| Company financial overview | **PARTIALLY READY** | company overview, domain summaries, monthly series | Execution staff salaries and the threshold premium in cost; a decision on what Proton may see (net profit and salaries are barred today, Q14) |
| Cash-flow analysis | **PARTIALLY READY** | past inflows and outflows by date; invoice due dates, payment plans, scheduled sends; dated overhead | payables and commitments (supplier invoices, purchase orders, subcontracts), payroll timing per month (salary-sensitive), expected payment dates for "left to invoice" |
| Scenario simulation | **NOT READY** | pure rules usable for what-ifs (`ExecutionPricing.PriceOf`, `ProjectFinance.Summarise`, `Tva`, `ProfitFloorRules`) | a what-if service that runs them on hypothetical inputs without writing, a list of levers (Q18), and rules for labour and overhead scenarios (salary-sensitive) |
| Design project forecasting | **PARTIALLY READY** | hours by department, budgets (revenue shares), cost history | planned hours or effort per department or deliverable, a progress measure tied to them |
| Execution project forecasting | **NOT READY** | agreed BOQ (quantities and rates), dated site expenses and labour | executed quantities or % complete per BOQ line (dated), a cost estimate per line, a schedule, commitments |

---

## 15. Proposed Financial Intelligence Tools

**Principles for every tool** [Conclusion]
- **Backed by an API endpoint.** Each tool is a GET endpoint with its own service and pure rules in `Business/Finance/…`, mirrored by a Proton read tool that inherits the endpoint's permission. The client can then show the same numbers (CLAUDE.md: "the API does every calculation").
- **Reuse the calculators.** Use `IProjectFinanceService`, `IDomainFinanceService` and `MoneyRollupService`, never a parallel formula.
- **Show the working.** Return a `basis`: method, assumptions, period, what was left out and why. Return datasets for charts.
- **Never per person in money.** No output places a person beside money, and none returns salaries, rates or net profit. A catalogue test should pin each new output.
- **Mark heavy reads.** Anything loading every project is marked heavy.

### A. Profitability and summaries

| Tool | Purpose | Inputs | Output | Uses | New work needed | Authorization |
|---|---|---|---|---|---|---|
| `get_project_financial_summary` | one project's whole financial picture | `project_id` | agreed, recognised, invoiced, collected, outstanding, owed (gross and net, labelled); cost by source and by month; profit, margin, cash profit; profit-floor standing; department budget use; settled amounts; unconverted costs | `get_project_financials` (exists), `CostHistoryAsync`, `ProfitFloorRules.StandingOf`, `ProjectDepartmentService` | profit-floor standing and cost-by-month in one response | `GET /projects/{id}` + `financial:view` |
| `analyze_project_profitability` | why a project's margin is what it is | `project_id`, optional comparison set | margin bridge: base price, VOs, settlements, cost by source, labour share by department (no people), against the domain's median | the above, `DomainFinanceService.ProjectsAsync` | a median or percentile rule; a labour attribution policy (Q1–Q3) | `financial:view` |
| `compare_project_financial_performance` | a project against similar ones | `project_id`, similarity options | ranked peers with margin, cost per unit of price, duration, overrun | `DomainFinanceService.ProjectsAsync` | **project attributes** (type, size, scope; schema change) and a similarity rule (Q15) | `financial:view` |
| `get_company_financial_overview` | company performance by period, compared | period, compare | domains' received, cost, cash profit, overhead, trends, attention | `get_company_overview`, `compare_money`, `get_money_by_month` (exist) | a decision on net profit and salaries (Q14); consolidate the overview's parallel path (§9.2) | `company:view` + `financial:view` |

### B. Forecasting

| Tool | Purpose | Inputs | Output | Uses | New work needed | Authorization |
|---|---|---|---|---|---|---|
| `forecast_project_final_cost` | estimated cost at completion, with a range | `project_id`, method | estimate at completion by method (progress-based, burn-rate to target date, budget-based), each with its basis and a confidence note | `CostHistoryAsync`, `ProjectFinance.Summarise` (projection exists), `LabourRules.ProjectZero` | progress per domain (Design: planned hours; Execution: executed quantities), budgets, commitments | `financial:view` |
| `forecast_project_profit` | profit at completion | `project_id` | agreed revenue + expected VOs − forecast cost; profit-floor outlook | the above | a rule for pending VOs and settlements | `financial:view` |
| `forecast_cash_flow` | expected inflows and outflows by week or month | domain or company, horizon | inflows (invoice balances by due date, scheduled invoices, plans), outflows (overhead, labour run-rate, commitments) | `InvoiceService`, `OverheadService`, `CostHistoryAsync` | payables and commitments; payroll timing (salary-sensitive, Q14); expected dates for amounts not yet invoiced (Q17) | `company:view` + `financial:view` |

### C. Risk and anomalies

| Tool | Purpose | Inputs | Output | Uses | New work needed | Authorization |
|---|---|---|---|---|---|---|
| `detect_project_financial_risks` | projects likely to lose money | domain, `limit` | ranked risks: profit-floor standing, cost growth against progress, department over budget, overdue and owed money, pending blocked changes, unconverted costs, short runway | `get_attention` / `ProtonSignals` (exist), `ProfitFloorRules`, `MoneyAttention`, departments | thresholds (owner), budgets and forecasts for the "likely" part | `project:view`, each signal by its own endpoint (as `get_attention`) |
| `analyze_expense_trends` | spending by category, supplier and month, with unusual items | domain or project, period, category | series per category, baselines, items flagged with their rule (amount against baseline, unit cost against the item's history, same amount twice) | site expenses, Design expenses, overhead, `ProtonTrends` | category mapping across domains, an item catalogue or name normalisation, thresholds (Q16) | Design: `expense:manage`/`financial:view`; site: `project:view` (as the page) |

### D. Labour and utilization

| Tool | Purpose | Inputs | Output | Uses | New work needed | Authorization |
|---|---|---|---|---|---|---|
| `analyze_labor_costs` | labour cost by project, department and month, **never per person** | domain, project, period | totals and series; hours by department; outside-scope share | `LabourPricer`, `LabourByDepartmentAsync` (fix unconverted-as-zero) | attribution of Execution staff and unlogged time (Q2–Q3) | `financial:view` |
| `analyze_employee_utilization` | how logged time is spread | period, department | per person: hours, expected, overtime, project mix (**hours only, no pay**); department totals | `HoursLedger`, `WorkInsightsService`, `ReportService` | non-project categories, capacity, leave calendar | `report:view` / `work_entry:view_all` |

### E. Scenarios

| Tool | Purpose | Inputs | Output | Uses | New work needed | Authorization |
|---|---|---|---|---|---|---|
| `simulate_project_financial_scenario` | what-if on one project, writing nothing | `project_id`, levers (a VO amount, an extra cost, a rate change, a delay with run-rate, a settlement) | before and after: price, TVA, cost, profit, margin, profit-floor outcome | pure rules: `ExecutionPricing`, `ProjectFinance.Summarise`, `Tva`, `ProfitFloorRules` | a service that feeds the pure rules hypothetical inputs; the list of levers (Q18) | `financial:view` |

---

## 16. Critical Missing Data and Business Rules

**Data** (each a schema change, needing approval under CLAUDE.md rule 9) [Conclusion]
1. **Cost budget:** per project, and per department (Design) or BOQ line or category (Execution). Revisions dated with a reason and approver.
2. **Planned effort:** planned hours per department or deliverable (Design).
3. **Execution progress:** executed quantity or % complete per BOQ line, dated.
4. **Committed costs:** purchase orders, subcontracts, supplier invoices and payables, with expected payment dates.
5. **Project attributes for comparison:** type, size, scope.
6. **Monthly financial snapshots** per project and domain, for point-in-time comparisons and to spare heavy reads.
7. **History gaps:** labour-week day history, site-expense field history, a Design VO's previous TVA, the historical supervision rate in expected revenue.
8. **One expense category scheme**, or a mapping between the three.
9. **Non-project time categories and a leave calendar**, if utilization matters.

**Business rules not settled** (Questions below)
- **Labour cost attribution:**
  - Execution staff salaries;
  - unlogged paid time;
  - admins' time;
  - the threshold premium.
- **What profitability means:** agreed or cash, overhead allocation, internal projects.
- **Progress and completed work** in each domain.
- **Settlements and the profit floor.**
- **Currencies:** forecast rates, and LBP Design expenses with no rate.
- **Who may see what:** analysis results, net profit and salaries; proactive alerts with or without amounts.

**Code gaps found during this investigation** (not changed)
- `keepPaidAhead` checks only `project:change_status` (§5.2).
- Design LBP expenses are accepted without a rate (§6).
- Department labour counts unconverted labour as 0 (§9.1).
- The company overview duplicates the domain summary (§9.2).

---

## 17. Recommended Implementation Dependencies

In order [Conclusion]:

1. **Decide the business rules first** (Questions Q1–Q9). Most change formulas, not just presentation.
2. **Consolidate the calculations** that a new engine would otherwise multiply, before adding any new figure:
   - route the company overview's period figures through `DomainFinanceService`, and its billing through `ProjectBilling`;
   - keep one margin function;
   - give the three "profits" distinct names;
   - fix the department labour conversion.

   Each step needs regression tests against today's numbers.
3. **Approve and add the missing data** (§16) in small, separate migrations, each with its own entry screens or endpoints. Without data entry, budgets and progress stay empty.
4. **Add pure rules** in `Business/Finance/Intelligence/*` (forecast methods, variance, anomaly rules), unit-tested at their thresholds, with no service or HTTP dependencies, as the existing `*Rules.cs` are.
5. **Add GET endpoints and services** for each capability:
   - authorization with `[HasPermission]` and service re-checks;
   - money left out, not zeroed;
   - domain filters by hand (there are no global filters).

   Then mirror each with a Proton read tool:
   - `ProtonEndpoint.Of`;
   - a `basis` on every result;
   - datasets for charts;
   - the heavy flag where the read loads every project.

   Extend the catalogue tests so no output can place a person beside money.
6. **Performance before breadth:** address the `docs/status.md` slow loaders, or add snapshots (step 3), before portfolio-wide forecasts.
7. **Proactive monitoring last:**
   - a `BackgroundService` on the `NotificationScheduler` pattern;
   - rules only (no model call) unless the owner accepts the cost and data exposure;
   - recipients chosen by permission;
   - notifications with no amounts;
   - findings stored for trend and de-duplication, which is a schema change.
8. **Evaluation:** add Financial Intelligence questions to `ProtonEvalTests`, which call the real model and cost money, so run them only by hand. Check that every number in an answer appears in a tool result, as the existing eval does.

---

## Questions for the Business Owner

Only what the code and documentation cannot answer, most consequential first.

| # | Question | Why it matters |
|---|---|---|
| Q1 | **What is "project profitability" for decisions:** agreed (contract value − cost so far) or cash (received − cost)? Should company overhead be allocated to projects, and how (by revenue, by hours)? | Every profitability, risk and comparison figure depends on it; today both exist and overhead is never on projects |
| Q2 | **Execution employees' salaries** (site staff) are in no profit figure today. Should they be charged to the sites they staff (by days on the staff list? equally?), or shown as a company cost line? | Site profit and company net profit are overstated by their pay |
| Q3 | **Design employees' paid time that isn't logged** (paid excuses, leave, short months not deducted): a company cost, or spread over projects? | Labour cost per project and utilization |
| Q4 | **Admins' and super admins' work on projects** costs 0 today. Should their time (or part of their salary) count on projects? And where should the threshold overtime premium go, which the docs call a company cost but no figure includes? | True project margins and company cost |
| Q5 | **Internal projects:** count them as cost centres in company profitability, compare them, or leave them out of Financial Intelligence? | Portfolio averages and comparisons |
| Q6 | **Currencies:** costs convert at the rate of their day and payments keep their own rate. For forecasts, which rate: today's, or a planned one? Should a Design LBP expense with no rate for its day be refused, as site expenses are? | Forecast accuracy; costs silently left out |
| Q7 | **Progress and completed work:** what measures how far a Design project is (deliverables, hours against a plan, department budgets) and an Execution site (quantities executed per BOQ line, milestones)? Who records it, and how often? | Every forecast; today progress is the share of requirements ticked |
| Q8 | **Budgets:** should projects have a cost budget (per department, BOQ line or category)? Who sets and approves it and its revisions? Are Design department budgets (price × share) meant as cost budgets? | Budget variance and risk detection |
| Q9 | **Committed and future costs:** does the company sign purchase orders, subcontracts or supplier invoices with payment terms that should be recorded before they are paid? | Cash-flow forecasting and cost at completion |
| Q10 | **Settling what a client owes at completion** (excused or written off) can take a project's profit to zero or below without the profit floor's check. Is that intended? | Profit-floor rule and risk signals |
| Q11 | **Expected revenue** applies today's supervision rate to past fee changes. Should past figures keep the rate in force then? | Historical comparisons of expected revenue |
| Q12 | **Who may see Financial Intelligence?** Super admins only (`financial:view`), or admins too for hours and utilization without money? Employees ever? | Authorization design for every new tool |
| Q13 | **Proactive alerts:** who receives them, by which channel (in-app, push, email), how often, at which thresholds, and may they ever carry amounts (they never do today)? | Background monitoring design |
| Q14 | **Net profit and salaries:** Proton is barred from admin salaries and the company's net profit (which would reveal them). Should a cash-flow or company-health analysis ever include payroll, and for whom? | Cash flow and company overview |
| Q15 | **"Similar projects":** what makes two projects comparable (type, area, scope, client type, location, price band)? | Historical comparison needs these attributes stored |
| Q16 | **Unusual spending:** what counts as unusual (against a category's average, an item's usual unit price, a supplier's usual amounts), and which categories matter? | Anomaly detection rules |
| Q17 | **Cash-flow horizon and expected inflows:** how far ahead, and when is money "left to invoice" expected to arrive? | Cash-flow forecasting |
| Q18 | **Scenario simulation:** which decisions should be simulated (VOs, delays, crew size, rates, hiring, overhead)? | Scope of the what-if service |
| Q19 | **The model without a person:** may an unattended analysis call the AI model (monthly cost, company data sent to the provider), or should proactive checks be rules only? | Proactive design and budget |

---

## Appendix A: facts re-checked by hand

These were read directly, beyond the passes, because the conclusions rest on them:

- the chat loop runs tools one at a time; its limits and timeouts (`ProtonChatAgent.cs`, `ProtonOptions.cs`);
- the toolbox: offering tools by endpoint policy, the typed-turn and family gates, proposals (`ProtonToolbox.cs`);
- the confirmation checks (`ProtonActionService.cs`);
- the 244 coverage entries and their pinned counts (`ProtonCoverage.cs`, `ProtonCoverageTests.cs`);
- the role defaults and the Website staff exclusion (`PermissionCatalog.cs:100-155`, `EffectivePermissions.cs:29-31`);
- `settleOwed` and `keepPaidAhead`: owed = agreed with TVA − received, the money-writer check on settle only, no profit-floor call (`ProjectStatusService.cs:20-183`);
- owed and paid ahead in billing (`ProjectBilling.cs:66-90`);
- fixed salaries only for non-employees, so Execution employees are in no salary line (`PayRules.cs:55`, `RoleHistory.cs:20-31`, `CompanyOverviewService.cs:253-261`);
- labour-week days replaced on re-submission (`LabourService.cs:309`);
- expected revenue with today's supervision rate (`DomainFinanceService.cs:142`);
- no authorization inside `ProjectFinanceService`;
- no `HasQueryFilter` anywhere;
- no Hangfire or Quartz in the project file;
- the side-effect guards wrap five services; the push queue is not wrapped (`ProtonSideEffectGuards.cs:17-23`).

## Appendix B: documentation that contradicts the code

| Doc | Says | Code |
|---|---|---|
| `docs/proton.md:59-68` | 237 write endpoints; 13 planned, 17 opened | 244; 14 planned, 18 opened |
| `docs/proton.md:111` | `get_recent_activity` reads up to 3 pages | one page, filtered by the period (`ActivityTools.cs:112-124`) |
| `docs/projects.md` | profit = revenue − cost | Execution adds the pocket net (`ProjectFinance.Summarise`) |
| `docs/finance.md` | "nothing here calculates cost, profit or margin a second way" | the company overview and department labour do (§9.2) |
| `docs/billing.md:122` | owed = "revenue" − received | the revenue there is gross, TVA included (`MoneyRollup.cs:49`) |
| `docs/company.md:121` | the threshold premium is a company cost | no figure includes it |
| `docs/status.md` | 369 unit and 297 integration tests; no CI | outdated; CI exists (`.github/workflows/ci.yml`); it doesn't mention Proton at all |
| `docs/authentication.md` | no "forgot password" flow; the domain claim is design or execution | the flow exists; `website` is a third domain |
| `Constants/PermissionCatalog.cs:96` (comment) | employees don't get Proton yet | `EmployeeGrants` includes `proton:use` |
