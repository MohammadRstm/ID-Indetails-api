# Proton — the company assistant

Proton answers questions about Hourline's data in plain language: projects, requirements, meetings, clients,
people, logged time, activity and, for people allowed to see it, money (Stage 1). It can also **propose** a few
changes, which happen only when the user confirms them on a card (Stage 2, [Controlled actions](#controlled-actions-stage-2)).
It also **analyses**: compares periods, summarises the company, says what changed and what may need attention, with
every number calculated in code (Stage 3, [Analysis](#analysis-stage-3)), and **opens pages and records** in the app
through typed commands (Stage 4, [Navigation](#navigation-stage-4)). It talks like a colleague, in the first person
and the user's language, and is honest that it is an AI (Stage 5, [How Proton talks](#how-proton-talks-stage-5)).
Proton itself never writes anything. The design and decisions are in [proton-stage-1-plan.md](proton-stage-1-plan.md),
[proton-stage-2-plan.md](proton-stage-2-plan.md), [proton-stage-3-plan.md](proton-stage-3-plan.md),
[proton-stage-4-plan.md](proton-stage-4-plan.md) and [proton-stage-5-plan.md](proton-stage-5-plan.md).

## Who can use it

| Permission | Default | Lets someone |
|---|---|---|
| `proton:use` | super admins and admins | ask Proton, end their conversations, see their own question allowance |
| `proton_budget:manage` | super admins | see Proton's estimated cost and usage per person, and top up the monthly budget |

Employees don't get Proton in Stage 1. Both are ordinary permissions (`Constants/PermissionCatalog.cs`): they can be
granted or revoked per person like any other, and `proton_budget:manage` is a company permission.

## How a question is answered

```text
POST /api/proton/messages
  → ProtonController            proton:use, "proton" rate limit, validation
  → ProtonChatAgent             allowance and budget check, conversation, the Claude loop
  → ProtonToolbox               which tools this user may use; runs each one read-only, with a time limit
  → a tool (Proton/Tools)       calls the same service method as the endpoint it stands in for
  → existing service            domain visibility, money hiding, business rules — unchanged
  → repository → MySQL
```

- **Authorization is the API's own.** Each tool names the GET endpoint it stands in for
  (`ProtonEndpoint.Of<ProjectsController>(nameof(ProjectsController.Get))`). The toolbox evaluates that endpoint's
  `[HasPermission]` policies for the caller with ASP.NET Core's `IAuthorizationService` — the same policies, handler
  and permission cache an HTTP call to that endpoint uses. A tool the user fails is not even offered to the model;
  called anyway, it is refused. Inside, the service still applies its own checks (another domain's record reads as
  not found, money only with `financial:view`). Proton keeps no permission list of its own.
- **No extra hop.** Tools call the services in process, in the request's scope, with the request's `ICurrentUser`:
  no HTTP call back into the API, and each tool is one service call (plus the request validator that endpoint runs).
  Authorization results are cached for the duration of one question.
- **Validation is the API's own.** The FluentValidation validator the endpoint's query goes through runs on the
  tool's query too (`ProtonToolContext.ValidateAsync`), because services don't validate their input themselves.
- **Tools never write, three ways.** Read tools can only mirror GET endpoints (`ProtonEndpoint.Of` refuses anything
  else); action tools mirror a write endpoint but only *propose* (see Controlled actions); and while any tool runs,
  an EF Core command interceptor refuses every statement that isn't a `SELECT` (`Proton/Safety/ProtonReadOnly.cs`).
  A refused write is logged as an error.
  Nor can a tool reach anyone or leave a change behind while it runs:
  - the email sender, file storage, notification sender and live updates are wrapped last in the app's wiring
    (`Proton/Safety/ProtonSideEffectGuards.cs`) and refuse while a tool runs;
  - a change a tool staged without saving is dropped, and the call is refused, so the next save in the request (a
    proposal row) can't write it.
- **Every write endpoint is on a checked list.** `Proton/Actions/ProtonCoverage.cs` says how Proton handles each of the
  118 write endpoints:
  - a built action (8), or one planned at a confirmation tier and family (68);
  - a prefilled form (2), or a place opened for the user (22);
  - a read-only POST (3), never (11), or Proton's own controls (4).

  `ProtonCoverageTests` fails when an endpoint has no entry, when an action and its tool disagree, when an action
  needing a money, pay, access or company-rules permission isn't confirmed strongly in the matching family, when a
  delete without a restore isn't strong, or when an endpoint that emails someone isn't flagged. The sensitivity tags
  are a code-only property of `PermissionCatalog.Definition` (not seeded).
- **The model is replaceable, the tools aren't.** `ProtonToolbox` and the tools are provider-neutral. The chat
  (`Proton/Agent/ProtonChatAgent.cs`) is the only code that knows Claude. A future live-voice interface would be another
  agent over the same toolbox.

## The tools

| Tool | Stands in for | Notes |
|---|---|---|
| `find` | project, client and employee lists | name → ids; drops filler words, tries single words if the phrase matches nothing |
| `list_projects` | `GET /api/projects` | filters, sorting, `overdue_only` |
| `get_project` | `GET /api/projects/{id}` | details and the overdue flag; no money |
| `get_project_requirements` | `GET /api/projects/{id}/requirements` | |
| `get_project_team` | `GET /api/projects/{id}` + `/work` | Design: who logged hours (optionally in a period); Execution: who recorded site data |
| `get_project_financials` | `GET /api/projects/{id}` (money part) | all time; no cost per hour |
| `get_site_summary` | `GET /api/projects/site-summary` | Execution only |
| `list_work_entries` | `GET /api/work-entries` | with descriptions; Design only |
| `get_time_summary` | `GET /api/reports/work` | by project, person, client; no labour cost per person |
| `get_work_overview` | `GET /api/reports/overview` | Design dashboard and attention items |
| `get_person` | `GET /api/employees/{id}` | no pay |
| `get_person_work` | `GET /api/employees/{id}/work-summary` | Design only |
| `list_people_activity` | `GET /api/work/people` | recent logging and inactivity; no rates or labour cost |
| `get_recent_activity` | `GET /api/activity` | reads pages newest first until the period starts (3 pages at most) |
| `list_meetings` | `GET /api/meetings` | at most 92 days |
| `get_domain_dashboard` | `GET /api/dashboard/{domain}` + deadlines | |
| `list_clients` / `get_client` | `GET /api/clients`, `/{id}` | |
| `get_domain_finance` | `GET /api/finance/{domain}/summary` | |
| `get_finance_projects` | `GET /api/finance/{domain}/projects` | ranked by revenue, cost, profit, margin or hours (all time) |
| `get_money_attention` | `GET /api/finance/{domain}/attention` | |
| `list_invoices` / `list_payments` | `GET /api/invoices`, `/api/payments` | paid in USD, balance in the invoice's currency |
| `get_company_overview` | `GET /api/company/overview` | without admin salaries and net profit |

**Never available to Proton**, for anyone: salaries and pay, hourly rates and per-person labour cost, admin salaries
(and net profit, which would reveal them), bank details, passwords and other credentials. A unit test
(`ProtonToolCatalogTests`) fails if any tool output gains such a field. Everything else follows the user's normal
permissions — including work-entry descriptions and contact details.

**Overdue** (`Business/Projects/ProjectOverdue.cs`): a project is overdue when it has a target date, that date is
before today (company time zone), and it is active or on hold. A project without a target date is never overdue.

**Periods** (`Business/Proton/ProtonPeriods.cs`): tools take `today`, `yesterday`, `this_week`, `last_week`,
`this_month`, `last_month`, `this_quarter`, `last_quarter`, `this_year`, `last_year`, `last7_days`, `last30_days`,
`last90_days` or `custom` with from/to. They become company-time-zone days in code, with the company's first day of
the week; running periods end today. Every result says which days it covers.

## Analysis (Stage 3)

Six analytical tools answer comparative and broad questions ("how is Villa Mounir doing compared with last month?",
"what changed this week?", "what should I look at?"). Every number in their results is calculated in code from the
existing services; the model only reads and explains them. Design and decisions:
[proton-stage-3-plan.md](proton-stage-3-plan.md).

| Tool | Reads through | Permission (from the endpoint) | Heavy |
|---|---|---|---|
| `compare_work` | `IReportService.WorkTotalsAsync` | `report:view` (`GET /api/reports/work`); Design only | |
| `compare_money` | `IDomainFinanceService.ProjectPeriodsAsync` | `financial:view` (`GET /api/finance/{domain}/summary`) | yes |
| `review_project` | the project, its work totals, requirements, period and all-time money, attention rules | `project:view`; each part needs its own endpoint's permission | yes |
| `get_company_summary` | projects, work totals, deadlines, period money, activity counts, overhead, attention | `project:view`; each block needs its own endpoint's permission | yes |
| `summarize_changes` | `IActivityFeedService.CountsAsync` and the feed, with `from`/`to` | `activity:view` (`GET /api/activity`) | |
| `get_attention` | projects, money attention, deadlines, the Design overview | `project:view`; each signal source needs its own | yes |

**What every analytical result carries**
- `basis`:
  - the exact days, and the days they were compared with;
  - whether the period is still running (`partial`);
  - how the comparison was chosen, and the scope;
  - the rules behind any judgement;
  - `omitted`: what was left out because the user may not see it, or because the data doesn't exist (Execution logs
    no hours).

  The chat reply turns each basis into a **source** (`sources` on `POST /api/proton/messages`), shown under the
  answer as "Based on …".
- Figures as `{current, previous, diff, pct}`. `pct` is null when there was nothing before, never ∞ or 100%.
- Shares of the total and of the change; the biggest increases and decreases.

**Comparisons** (`ProtonPeriods.Compare`):
- **Running period** (this week, month, quarter, year): compared with the same number of days at the start of the
  previous one (1–7 Oct against 1–7 Sep), never past that period's end. The Design dashboard compares month to date
  with the whole last month instead.
- **Finished week, month, quarter or year**: compared with the whole one before.
- **A day, last-N-days or custom days**: compared with as many days just before.
- Also available: `same_period_last_year`, `custom` and `none`.

**Judgements need a rule** (`Business/Proton/ProtonSignals.cs`, thresholds in `Proton:Insights`):

| Flag / signal | Rule |
|---|---|
| notable change (hours) | ±30% or more **and** at least 10 h; from nothing, the 10 h alone |
| notable change (money) | ±30% or more **and** at least 1,000 USD |
| unusual (weekly hours) | last full week 1.5× or more, or 0.5× or less, of the 4 weeks before (each with work), and 10 h from it |
| target soon | active project, target date within 14 days |
| overdue | `ProjectOverdue` (target date passed, active or on hold) |
| idle, close-out | the Design report's own rules: no work for 7 days; 90% progress |
| cost high, overdue invoice, finished but owed, no payment | `MoneyAttention` (cost past 85% of the payment, …) |
| overdue requirements, not logging | the dashboard's deadlines; Design employees with no work this week |

**Ranking.** `get_attention` lists each project once with all its signals, ranked by its most pressing signal:

1. overdue invoice
2. finished but owed
3. overdue project
4. overdue requirements
5. cost high
6. no payment
7. target soon
8. unusual or notable change
9. idle
10. close-out
11. on hold
12. not logging

It is on demand only: nothing is sent to anyone.

**Never per person in money.** Period money is per domain, per cost source and per project. Pay events are left out
of change counts.

**Per-question cache.** `ProtonAnalysisService` keeps each domain's projects, period money, money attention and work
totals for the rest of one question (one request, one user). Nothing is cached across requests, because back-dated
entries and new payments would make it stale.

**Limits.**
- At most `MaxHeavyToolCallsPerQuestion` (4) heavy analyses per question; the next is refused with a note to answer
  with what Proton has.
- A conversation whose last model request passed `MaxConversationInputTokens` (80,000) input tokens rolls over (see
  [Conversations](#conversations-and-follow-ups)).

**Answer rules** (`analysis.md`):
- Facts, calculations, interpretation ("Based on this data, …") and recommendation ("You may want to …") are kept
  apart.
- No arithmetic by the model.
- "Notable" or "unusual" only when a rule fired.
- "Why" answers name measurable contributors and say what the data can't establish.
- Recorded hours aren't a measure of anyone's work: no judgement of people, and no employment advice.

## Navigation (Stage 4)

Proton can move the user around the app: "take me to Villa Mounir", "open its requirements", "go to financials". A
confirmed "create project" opens the new project. It does this through a small typed contract, **never an address**.
- The API states the intent, for example "open project 123 on its requirements tab".
- The client maps the intent to its own routes, checks access again, and navigates.

Proton has no browser, DOM, script or screenshot control. Design and decisions:
[proton-stage-4-plan.md](proton-stage-4-plan.md).

| Tool | What it does | Permission |
|---|---|---|
| `open_record` | Opens a project (optionally on `requirements`, `timeline` or `meetings`), a client or a person | The record's own GET endpoint. The record is read first through its service, so an id the user can't see is `not_found` and nothing opens. A tab the user can't open falls back to the overview, and Proton says so. |
| `open_page` | Opens one page from a fixed list: dashboard, projects (optionally one client's), clients, people, financials (optionally a tab), meetings, activity, reports, company overview, company expenses | Each page needs its endpoint's permission, e.g. financials needs `financial:view`. Settings, salaries and pay are not on the list. |
| `offer_choices` | Shows 2–6 matches as buttons when a name is ambiguous, or 2–6 fixed answers to Proton's question (`replies`: a period, `design` / `execution`, `yes` / `no`) | Only records a tool returned in the conversation, and only businesses the user can see; labels come from the records or the app, not from the model. No yes/no while a card is pending |
| `suggest_replies` | Offers up to 3 short next things the user might say (60 characters each, one line) | None while a card is pending; a tap sends only its place in the list |
| `open_surface` | Opens a form, dialog or tab on its own page (Stage 5, Phase 4): new project, new client, invite, a project's or client's details, the project report, a project's payment, expense or site pricing, a new invoice, record payment, a new statement, a new meeting, a company expense, a chat with someone, log work, report a bug, a settings section, a company overview tab | The endpoint it saves through (`Proton/Ui/ProtonSurfaces.cs`); a record it is for must be visible. Opening fills and saves nothing. The invite and the bug report email someone, so they open only on a typed turn. One-click confirm dialogs (complete, void, delete, deactivate, top-up) aren't on the list |
| `set_preference` | Light or dark theme; message sounds on or off | Sounds only on a typed turn |
| `refresh_page` | Reloads the current page's data | — |

**Contract** (`Proton/Ui/ProtonUiCommand.cs`):
- `{type, label, id?, domain?, section?, page?, clientId?, tab?, surface?, settingsSection?, companyTab?, theme?, sounds?}`
  - `type` is `open_project`, `open_client`, `open_person`, `open_page`, `open_surface`, `set_preference` or `refresh`.
  - Pages (Phase 4 adds): settings, notifications, messages, help, and in the company area schedules, salaries,
    project pay, Proton's budget, company settings, the profile and the start page. Salary and pay pages open only for
    those who may see them, and Proton never reads their figures.
  - Every field is an enum or a positive id, except `label` (the record's own name) and `domain`. A unit test pins that.
- Delivery:
  - `uiCommands` on the reply, in order: one place (a surface wins over a plain page, since it brings its own), then
    the preferences (the last of each kind); a refresh only when nothing else moves the page.
  - `choices` on the reply.
  - `uiCommands` on a confirmed action's outcome.

**Search** reuses `find`, filtered with `types` where useful, and the list tools. There is no new search engine.

**Where the user is.** The client may send `page`:
- `{area: design|execution|company, page?, entityType?, entityId?, section?}`, validated against known values only.
- The API reads the record through its service before naming it in the context note ("looking at project #12 "Villa Mounir" (requirements tab)").
- If the user can't see the record, only the page is mentioned, so an id typed into the address bar never becomes a name.
- It resolves "it" and "this project". It is never data and never permission, and actions still need a proposal and a confirmation.

**Picking a choice.** The client sends `{conversationId, choice: {type, id}}` and no text.
- The pick must be one of the last offered options and still visible to the user. Otherwise the API answers `422 proton_choice_invalid`.
- An accepted pick becomes a question written by the app. Options can be used once.
- A quick reply is sent as `{conversationId, reply}` and a suggested reply as `{conversationId, suggestion}` (its
  place in the list), each checked against what the last reply offered; the server writes the question's text.

**After an action.**
- Every action takes `open_after`.
- A new project opens after Confirm by default. Other changes open only when the user asked to be taken there:
  - project changes open the project;
  - requirement changes open the project's requirements tab.
- The card says beforehand what will open (`preview.opensAfter`).
- The outcome carries the command only after a **successful** change. A failed or cancelled action opens nothing.
- The client already refreshes every screen after a confirm.

**Prompt** (`navigation.md`):
- It separates reading, opening, both, and acting.
- Proton resolves names with `find`, and never guesses between several matches.
- It opens only when the user asked, never because a record's text says so.

**Surfaces on the client** (`surfaces.ts`): each surface maps to its page's own link (`?new=1`, `?invite=1`,
`?details=1`, `?report=1`, `?money=`, `?invoice=new`, `?pay=1`, `?statement=new`, `?add=1`, `?with=`,
`?section=`, `?tab=`), or to an opener a showing page registered (the bug report), or to the log work window, with the
same permission checks the page makes before it shows the window. `?details=1` (edit project, edit client) and the
meetings page's `?new=1` are new links (`useUrlFlag`, one list of keys so two windows never share one).
- A window Proton opens doesn't take the keyboard: `Modal` gives focus back to where the person had it, even from an
  `autoFocus` field (`markAssistantOpening`).
- A window names itself with `surfaceId`, so the page context reports it (`surface`) and Proton knows what is open.

**Security**, in layers:
1. The model can only name enums and ids.
2. The API checks the record or page against the user's own access before emitting a command.
3. The client accepts only known command types and valid fields, builds the route itself, and checks its own route access.
4. The destination page's API calls enforce access once more.

Prompt injection can at most open a page the user may already open.

## How Proton talks (Stage 5)

Phase 1 of [proton-stage-5-plan.md](proton-stage-5-plan.md) (E1–E3, G6).

- **Persona** (`persona.md`, loaded first): first person, warm and direct, the user's first name now and then, one
  question at a time, small talk answered briefly. It replies in the user's language (English, Arabic, French) and keeps
  record names as they are. It says it is an AI whenever asked, never claims human experiences, and corrects anyone who
  seems to think it is a person.
- **Name.** The prompts say `{product}`, filled in with `Email:ProductName` ("ID InDetails"), so users only ever see
  the name they know. The company's name comes from its branding, in the context note.
- **Greeting and briefing.** The panel greets the user itself (first name, time of day; no model call) and shows
  `GET /api/proton/briefing`: the three things most worth a look and how many more, from get_attention's rules and
  read path, only for users who could ask get_attention. No model call and no question used.
- **Small talk is free.** A question Proton answered without any tool doesn't count toward the monthly allowance; it
  still costs the budget.
- **Turn origin** (decision 33). The server sets each turn's origin from the request's shape: `typed`, `choice` (a
  pick or a quick reply) or `suggestion`. On a turn the user didn't type, the toolbox refuses any proposal in a gated
  family (Deletes, Access, CompanyRules, PayRules, Money, Messaging, or a tool with no coverage entry), with the tool
  list unchanged. Today's actions are all Core.
- **Nothing beside a card passes for a Confirm.** While a proposal is pending, Proton offers no yes/no buttons and no
  suggested replies, and a yes/no click is refused.

## Forms and message drafts (Stage 5, Phase 5)

Proton can fill a form for the person to check and save, and draft a message for them to send. It never saves,
submits or sends anything itself.

- **`fill_form {form, id?, <form>: values}`** (`Proton/Ui/ProtonForms.cs`): one typed record per form (decision 11b,
  no loose dictionary). The forms are `client`, `meeting`, `new_project` (no money: revenue, fee, markup and pay don't
  exist in it), `edit_project`, `report_bug`, `invite` (address, position and team, never role or pay) and `message`.
  - Shape problems go back to the model field by field (lengths, email format, times).
  - It needs the endpoint the form saves through, and any record it names must be visible to the user.
  - `report_bug`, `invite` and `message` fill only on a typed turn. An invitation's email only when the user typed that
    address in this conversation (the conversation keeps the typed questions).
  - A draft goes only to one of the user's contacts or a team chat they are in.
  - The command `fill_form` runs after the surface that opens the form (`Pick`: place, fill, preferences).
- **Client** (`formFill.ts`): a form that can be filled registers an adapter while it is open (`useProtonForm`);
  a fill that arrives before the form opens waits up to 10 s. Adapters fill **empty** fields only (`fillEmpty`); the edit
  form replaces values the user asked to change and shows what they were (`fillChange`). A note lists what was filled.
  In a filled form, **Enter does nothing until the person touches it**; the form's own Save is how it is saved.
  Pilots: `ClientModal`, `MeetingModal`, `CreateProjectModal`, `EditProjectModal`, `ReportBugModal`, `InviteModal`.
- **Drafts**: a message fill opens the chat and puts the draft in its composer (a store keyed by chat, never the URL).
  Enter doesn't send a Proton draft until the person changes it; they press Send.
- **Messaging reads** (D1), as the user, through an explicit allow-list of endpoints without a permission of their own
  (`ProtonEndpoint.OfRuleBased`: `Messages.Contacts`, `Messages.Conversations`, `Notifications.List`, each needing
  `proton:use`): `find_recipient` (contacts and the user's team chats, with role, position, team and business),
  `get_my_inbox` (who, unread, mention, when; **never message bodies**), `get_my_notifications`. A new `team` ref type
  names a team chat the user is in; `open_surface message_team` opens it. The page context reports an open chat by id.
- Later forms in the plan (company details, report options, overhead line, material rows) and sending (Phase 7) build
  on the same pieces.

## Live answers (Stage 5, Phase 3)

`POST /api/proton/messages/stream` takes the same request as `/api/proton/messages`, with `proton:use` and the
`proton` rate policy, and answers as Server-Sent Events. The JSON endpoint is unchanged.

- **Before the first byte**, every check runs: switched on, allowance and budget, the conversation (ended, someone
  else's, access changed, busy), validation, and a button that wasn't offered. A refusal is an ordinary error response
  with its own status code.
- **Then** `200 text/event-stream` with `Cache-Control: no-cache` and `X-Accel-Buffering: no`:
  - `status` `{text}`: a fixed phrase per tool (`Proton/Agent/IProtonListener.cs`), never the tool's input or data.
  - `delta` `{trip, text}`: the answer as the model writes it. `trip` is the model call: a later call's text replaces
    an earlier one's (text written before a tool call is often superseded).
  - `done`: the same `ProtonReplyResponse` the JSON endpoint gives.
  - `error`: the uniform `{type, code, message, errors, traceId}`, from the same mapping as the exception handler
    (`Middlewares/ApiErrorMapping.cs`).
  - `: ping` every 10 s, so no proxy read timeout is reached while tools run.
- **Time**: `StreamQuestionTimeoutSeconds` (120) for the whole question; the JSON endpoint keeps 55 s.
- **Stop or a dropped connection**: the question is still recorded with its tokens (outcome `failed`), adds nothing to
  the conversation, and any change it proposed is closed as `superseded`, so no card waits for a Confirm that can't
  come. The same applies to any question that fails.
- **How**: the agent prepares a question (all checks), then answers it. With a listener, each model call is streamed
  (`Messages.CreateStreaming`) and put back together (`ProtonStreamedMessage`) for the same tool loop and echo.
- **Caching**: the system prompt carries an explicit cache breakpoint (it caches the tools and the prompt for
  everyone), and the request's automatic breakpoint caches the conversation so far.
- **Proxies**: nginx passes events on at once because of `X-Accel-Buffering: no` (and `gzip` is already off on
  `/api/`). Caddy's `encode` is left as it is until the staging stack shows whether it buffers `text/event-stream`
  (decision 17c).
- **Client**: `protonStream.ts` reads the stream with fetch (the same token, one refresh on a 401). The chat shows the
  status line and the text as it arrives, and a Stop button while an answer is running.

## Conversations and follow-ups

- Kept **in server memory** (`ProtonConversationStore`), keyed by user and conversation id: nobody else can continue
  someone's conversation. They end after 30 idle minutes, after 4 hours, or on restart (`404
  proton_conversation_ended`; the client starts a new one and says so). The client never sends history back.
- **Rollover.** At 20 questions, or once the last model request passed `MaxConversationInputTokens`, the chat carries
  on under the same id instead of ending: the messages are dropped, and the next question gets a recap written by the
  app from the last three questions and answers (400 characters each). The focus, the known records, offered options
  and pending proposals stay. The reply says `rolledOver: true`.
- The message list is exactly what was sent to the model, append-only. A failed question adds nothing to it.
- A **focus** (`ConversationFocus`) is updated from tool results, not guessed from text: the current project, client,
  people and period, and from analytical results what the period was compared with and the last analysis. It is sent with each question in a short context note, with today's date and who is asking, so
  "it", "they" and "the month before" resolve reliably.
- If the user's role, permissions or domains change, the conversation is closed with no recap (answers given under
  the old access must not carry over).

## Controlled actions (Stage 2)

| Tool | Risk | Stands in for (its policies are required to propose *and* to confirm) | Notes |
|---|---|---|---|
| `propose_add_requirement` | low | `POST /api/projects/{id}/requirements` | open projects only; assignees are employees of the domain |
| `propose_update_requirement` | low | `PUT …/requirements/{id}` | text, deadline, assignees; refused if the requirement changed since |
| `propose_set_requirement_done` | low | `POST …/requirements/{id}/done` (`/undone`) | |
| `propose_change_project_status` | medium | `POST /api/projects/{id}/hold` (`/resume`) | hold and resume only: completing, archiving and reopening lock or unlock revenue |
| `propose_update_project` | medium | `PUT /api/projects/{id}` | title, description, location, target date, owner; refused if the project changed since |
| `propose_create_project` | medium | `POST /api/projects` | Design only, client or internal, no money fields |
| `propose_create_client` | low | `POST /api/clients` | the contacts the user gave; a shared name is a warning on the card, another client's contact is refused; opens the new client |
| `propose_update_client` | low | `PUT /api/clients/{id}` | details, plus contacts added or removed (the rest kept); refused if the client changed since |
| `propose_set_client_archived` | low | `POST /api/clients/{id}/archive` (`/restore`) | |
| `propose_schedule_meeting` | low | `POST /api/projects/{id}/meetings` | a day and an HH:mm start in the company's time zone (`IClock.FromCompanyTime`); no invitation is emailed |
| `propose_update_meeting` | low | `PUT /api/meetings/{id}` | moves, resizes or renames; the meeting rules (your own unless you manage meetings, open projects) are checked when proposing |
| `propose_create_team`, `propose_rename_team` | low | `POST /api/teams`, `PUT /api/teams/{id}` | a name already used in the domain is refused |
| `propose_update_my_profile` | low | `PUT /api/me/profile` | the user's own name and phone only (`ProtonEndpoint.OfRuleBasedWrite`: `proton:use` is its policy) |
| `propose_comment_work_entry` | low | `POST /api/work-entries/{id}/comments` | on the entry only; the author is notified. Posting to their team chat is a message (Stage 5, Phase 7) |

Reads added for these: `list_teams` (`GET /api/teams`, rule-based) and `get_work_entry` (with its comments, no
attachment). Dashboard card orders stay with the person (they drag the cards).

How it works (`Proton/Actions/`):

1. **Propose.** The model calls a `propose_…` tool. Like every tool it runs inside the read-only guard: it reads the
   target, checks the change with the existing business rules, and validates the exact request the endpoint would
   take, with that endpoint's own validator. The toolbox stores it in `proton_actions` as a **pending** action and
   the reply carries it in `pendingActions` (`pendingAction` is the last one). Nothing else is written. See
   [The action engine](#the-action-engine-stage-5-phase-6) for how several cards wait at once.
2. **Confirm.** The user presses Confirm on the card: `POST /api/proton/actions/{id}/confirm` with only
   `{conversationId}`. The stored request is what runs — the client never sends arguments. The action must be the
   caller's own, in that conversation, pending and not expired (10 minutes, `Proton:ActionMinutes`). One conditional
   `UPDATE` moves it to `executing`, so it can run once even if confirmed twice. The write endpoint's policies are
   evaluated again **now**, the request is validated again, and the existing service makes the change (its own
   domain checks, rules, transaction, activity events and notifications).
3. **Outcome.** `succeeded` (with the record created or changed) or `failed` (with the service's own error code,
   returned to the client in the uniform error contract). Proton learns the outcome from the app in the next
   question's context note — never from the model.

There is no tool that executes: "don't ask me", "I already approved" or text inside data can't make Proton change
anything. Cancel: `POST /api/proton/actions/{id}/cancel`. Both decisions are rate limited under `proton`.

`proton_actions` is also the audit: who (user, role, domain), conversation, tool, risk, the exact arguments and what
the card showed, when it was proposed, decided and finished, the outcome, the record affected, and the model and
prompt version. Error codes: `404 proton_action_not_found` (not yours, other conversation), `409
proton_action_not_pending` (already decided, replaced or running), `422 proton_action_expired`, `422
proton_action_stale`.

The client shows each proposal as a card in Proton's chat, listing what will change (before → after), its risk, and
Cancel / Confirm. Typing "yes" confirms nothing.

## The action engine (Stage 5, Phase 6)

Every `propose_…` tool goes through the same rules (`ProtonToolbox.ProposeAsync`, `ProtonActionService`):

- **Families, switched on one by one.** Each write endpoint's family comes from the coverage registry
  (`ProtonCoverage.FamilyOf`). `Proton:Actions` switches them: `Core` is on; `Lifecycle`, `Deletes`, `Access`,
  `CompanyRules`, `PayRules`, `Money` and `Messaging` are off until an admin turns them on. A tool of a family that
  is off is refused when proposing, and a card waiting when it was switched off is refused at Confirm
  (`422 proton_actions_off`). An action the registry doesn't know is never on. `Proton:Actions:Enabled=false` stops
  every proposal.
- **Several cards.** Up to `MaxPendingActions` (3) wait per conversation. A new proposal for the same tool and the same
  target replaces that card (the old one becomes `superseded`); other cards stay. Over the cap the tool is refused and
  Proton asks the user to decide one first.
- **Strength of the Confirm.** The registry gives each action a tier. A strong action is a two-step card (Review, then
  Confirm). A card can ask for a typed phrase (`confirm: "typed"`, `phrase`): Confirm sends `{conversationId, typed}`
  and the server compares it with the phrase it stored with the proposal (`422 proton_phrase_mismatch`). The card also
  shows the area (the target's domain), whether the change can be undone, and its reach when it touches many people.
- **Made for the access the user had.** The proposal stores a fingerprint of the user's role, domain and permissions.
  If they changed before Confirm, the card is closed as `superseded` unrun (`409 proton_action_not_pending`).
- **Caps.** At most `MaxConfirmedActionsPerHour` (30) confirmed per person per hour (`422 proton_action_limit`).
- **Gated turns.** Families other than Core and Lifecycle can only be proposed on a turn the user typed, never from a
  tapped choice or suggestion.
- **History.** `GET /api/proton/actions` lists the caller's last 50 proposals and what became of them (a pending card
  past its time reads `expired`): restored cards read their status here. `GET /api/proton/actions/all`
  (`proton_budget:manage`) lists everyone's, with who proposed them.

## Strong actions (Stage 5, Phase 7)

Each in a family that is off until switched on in `Proton:Actions` (see [The action engine](#the-action-engine-stage-5-phase-6)),
and confirmed strongly: a second step at least, and a typed phrase the server stored where the table says.

| Tool | Family | Stands in for | Confirm |
|---|---|---|---|
| `propose_project_lifecycle` | Lifecycle | `POST /api/projects/{id}/complete`, `/archive`, `/reopen` | the project's name typed; the card says the revenue locks (or unlocks) and how many open requirements get ticked |
| `propose_update_employee` | Access | `PUT /api/employees/{id}` | two steps; name, phone, position and team only (never role, domain, sign-in email or pay) |
| `propose_deactivate_employee` | Access | `POST /api/employees/{id}/deactivate` | the person's name typed; never oneself |
| `propose_reactivate_employee` | Access | `POST /api/employees/{id}/reactivate` | two steps; when they left earlier this month the tool asks Proton to ask the user whether the time away is paid, and the card shows the answer. Someone who never signed up shows as invited again |
| `propose_end_sessions` | Access | `POST /api/employees/{id}/sessions/end` (`/restore`) | two steps; never one's own |
| `propose_delete_team` | Deletes | `DELETE /api/teams/{id}` | the team's name typed; an empty team only (a team whose chat has messages is refused by the app) |
| `propose_company_settings` | CompanyRules | `PUT /api/company/settings` | two steps; the company's name typed for a new week start or default currency. The card says how many people it affects |
| `propose_domain_settings` | CompanyRules | `PUT /api/domain-settings/{domain}` | two steps; the domain's name typed to hold or release logging. The card says how many people it affects |
| `propose_send_message` | Messaging | `POST /api/messages/direct/{id}`, `/teams/{id}` | see below |
| `propose_comment_work_entry` with `send_to_group` | Core, held to Messaging | `POST /api/work-entries/{id}/comments` | the team's name typed; needs Messaging switched on and a typed question |

Reads for them: `get_company_settings` and `get_domain_settings` (both rule-based: everyone may read them). Each
spec re-reads its target at Confirm and refuses a stale change (`422 proton_action_stale`).

## Messages sent as the user (Stage 5, Phase 7)

`propose_send_message` (prompt: `Proton/Prompts/messages.md`) sends one message, as the user, to one person or one
team chat, through the messaging service and its rules (reach, active recipient, membership, mentions):

- **Only to someone the user named.** The recipient must be a contact or a team chat the user is in, and a word of
  their name must appear in what the user typed in this conversation. A name that only appeared in the data is
  refused. As a Messaging action it is also refused on a turn the user didn't type.
- **Exactly what the card shows.** The body is stored with the proposal and sent byte for byte. No attachments, no
  `@all`, at most 10 mentions, team chats only.
- **Strong.** Two steps for a direct message; the recipient's name typed for a team chat with mentions or more than ten
  people, and for any message with figures in it (an amount, a percentage, a large number), with a warning to check
  the recipient may see them.
- **A few a day.** `MaxMessagesPerDay` (20) per person per company day, counted from `proton_actions`
  (`422 proton_action_limit`).
- **Marked.** The message carries the confirmed action (`messages.proton_action_id`) and reads `viaProton: true`; the
  chat shows it ([messaging.md](messaging.md)). A comment shared to a team chat is not marked yet: its card in the chat
  already shows who wrote it.

## The blob (client, Stage 5 Phase 2)

Proton appears as a soft, furry blob on every signed-in screen of people with `proton:use`, when Proton is switched
on. It replaces the top-bar button. It is mounted once, in `LiveRoute`, never on the sign-in pages or
`/change-password` (client repo, `src/domains/shared/proton/ProtonHost.tsx`; the files below are beside it).

- **State** lives outside any page (`protonStore.ts`), and the asking runs in the host (`useProtonConversation.ts`).
  A reply that arrives after the panel closed, or after the person moved on, still lands: the blob shows an unseen
  count and, for a few seconds, the start of the reply.
- **Moods** (`blob/protonMood.ts`): asleep (budget or allowance used up), error, working (a question past 3 s),
  thinking, talking, happy (a confirmed change went through), proposing (a card waits), listening (the person is
  typing), idle. The mood sets the shape, the eyes and the accessible name ("Proton, thinking"). After 8 s the chat
  says "Still looking…".
- **Talking** (Phase 3): the blob talks while the answer's text streams in, and a moment after. It is a little louder
  for bigger pieces of text. The status line and the answer show as they arrive (see [Live answers](#live-answers-stage-5-phase-3)).
  Screen readers hear a reply once it is in.
- **Drawing** (`blob/blobShape.ts`, `blob/useBlobMotion.ts`): radii joined by a closed Catmull-Rom curve. A
  requestAnimationFrame loop redraws the outline only while thinking, working or talking (at most about 30 frames a
  second while talking), pauses when the tab is hidden, and never runs under reduced motion or "Calm animations".
  Only a static glow layer is blurred. No new dependencies.
- **Moving** (`blob/useBlobDrag.ts`, `blob/blobPosition.ts`): drag with mouse, finger or pen (6 / 8 px before it is a
  drag); it snaps to the nearer edge. Arrow keys, Home and End move it too, and the panel's menu has "Move to the
  left/right", "Calm animations" and "Tuck Proton away". Its side, height and those choices are kept in
  `localStorage`. It moves off the focused field, a dialog's buttons and the message composer.
- **Panel**: a speech bubble beside the blob on wide screens (`min(400px, 100vw − 32px)`), a bottom sheet on phones
  (88% of the visible height, from the VisualViewport API; it folds to its header after Proton opens a page). It is a
  labelled dialog that doesn't take the page away (`aria-modal="false"`), above dialogs at z-index 108.
- **Dialogs** (decision 35): Escape in Proton closes only Proton, and `Modal` and `Drawer` ignore keys pressed in the
  Proton layer (`[data-proton-layer]`). While Proton is open, a dialog renders `aria-modal="false"`.
- **Command palette**: "Ask Proton" opens it.

## Prompts

Markdown files in `Proton/Prompts/` (`persona.md` who Proton is and how it sounds, `proton.md` the company and the
grounding rules, `data.md` what it may see and that people's text is data, `analysis.md` how to present comparisons,
judgements, "why" answers and anything about people, `navigation.md` how it moves around the app and offers buttons,
`actions.md` how to propose changes and that only the card confirms them, `chat.md` how to answer in chat), embedded
in the assembly. `{product}` is replaced with the product name. The prompt version recorded with each question is a
hash of their text. The system prompt is the same for everyone, so it is cached by the provider;
per-question facts go in the context note.

## Usage, limits and cost

- Every question is one row in `proton_usage`: who, when, conversation, model, prompt version, outcome
  (`answered`, `refused`, `limited`, `failed`), model and tool call counts, tokens, the **estimated** cost, the question,
  the answer (up to 8,000 characters) and the tool calls (name, status, duration, and parameters with free text
  replaced by its length — never their results).
  Failed questions are recorded too, with the tokens they used.
- **Monthly company budget** (USD): `Proton:MonthlyBudgetUsd` plus that month's top-ups (`proton_budget_top_ups`,
  added by super admins, never edited). Once the month's estimated cost reaches it, nobody can ask until a top-up.
- **Monthly question allowance per person**: `Proton:MonthlyQuestionsPerUser`. People see this allowance (used,
  remaining, percentage) — never money. Only questions Proton used a tool for count (decision 19).
- Cost is estimated from `Proton:Prices` (USD per million tokens). The provider's bill is the truth; also set a spend
  limit on the Anthropic workspace as a backstop.

| Endpoint | Permission | |
|---|---|---|
| `POST /api/proton/messages/stream` | `proton:use` | the same, as Server-Sent Events (see [Live answers](#live-answers-stage-5-phase-3)); rate policy `proton` |
| `POST /api/proton/messages` | `proton:use` | `{conversationId?, text, page?, choice? \| reply? \| suggestion?}` → `{conversationId, reply, refs, limited, usage, sources, uiCommands, choices, quickReplies, suggestions, pendingActions, pendingAction, rolledOver}`; rate policy `proton` |
| `GET /api/proton/briefing` | `proton:use` | `{items: [{project?, domain?, facts}], more}`: what get_attention finds first, with no model call; rate policy `proton` |
| `DELETE /api/proton/conversations/{id}` | `proton:use` | ends a conversation |
| `GET /api/proton/usage` | `proton:use` | the caller's question allowance and whether Proton is available |
| `GET /api/proton/budget` | `proton_budget:manage` | this month's budget, top-ups, estimated spend, and spending by person, domain, tool (calls, average time) and day, with the 10 most expensive conversations |
| `POST /api/proton/budget/top-ups` | `proton_budget:manage` | `{amountUsd, note?}` (up to 10,000 USD) → the budget |
| `POST /api/proton/actions/{id}/confirm`, `…/cancel` | `proton:use` | `{conversationId, typed?}` → the outcome; rate policy `proton` |
| `GET /api/proton/actions` | `proton:use` | the caller's last 50 proposals with their status |
| `GET /api/proton/actions/all` | `proton_budget:manage` | everyone's last 50 proposals, with who proposed them |

Errors use the uniform contract: `503 proton_unavailable` (switched off, or the model failed), `422 proton_budget_used`,
`422 proton_allowance_used`, `409 proton_busy` (a question is still being answered in that conversation),
`404 proton_conversation_ended`, `422 proton_choice_invalid` (a pick, quick reply or suggestion that wasn't offered, or a record no longer visible), `429 too_many_requests`.

## Configuration (`Proton` section)

| Setting | Default | |
|---|---|---|
| `Enabled` | `false` | must be true, with `ApiKey`, for Proton to answer |
| `ApiKey` | — | user secrets locally; `Proton__ApiKey` (`PROTON_API_KEY` in the server's `.env`) in production. Required when enabled: the API won't start without it |
| `Model` | `claude-sonnet-5-5` | |
| `Effort` | `low` | `low` … `max` |
| `MaxOutputTokens`, `MaxRoundTrips`, `MaxToolCallsPerQuestion` | 8000, 8, 12 | per question; at the limit the model must answer with what it has |
| `ModelTimeoutSeconds`, `ToolTimeoutSeconds`, `QuestionTimeoutSeconds` | 40, 20, 55 | the question stays under nginx's 60-second read timeout |
| `StreamQuestionTimeoutSeconds` | 120 | the whole question on the streaming endpoint; its pings keep proxies from timing out, and a streamed model call may run this long |
| `MaxQuestionsPerConversation`, `ConversationIdleMinutes` | 20, 30 | a conversation at the question limit rolls over |
| `MonthlyBudgetUsd`, `MonthlyQuestionsPerUser` | 50, 300 | |
| `DeferToolsAbove` | 55 | past this many tools for a person, all but the core set (`ProtonToolSearch.Core`: find, the overviews, moving around, options) are sent with `defer_loading` and found through the provider's BM25 tool search. The list is the same on every call of a conversation, so it stays cached |
| `MaxHeavyToolCallsPerQuestion` | 4 | analyses that load every project's money; the next one is refused |
| `MaxConversationInputTokens` | 80000 | a conversation past it rolls over |
| `Insights` | 30%, 10 h, 1,000 USD, 1.5×, 0.5×, 4 periods, 14 days | `NotablePct`, `NotableHours`, `NotableUsd`, `UnusualHigh`, `UnusualLow`, `BaselinePeriods`, `TargetSoonDays` |
| `Actions` | Core on, the rest off; 3 cards, 30 an hour | `Enabled`, one switch per family (`Lifecycle`, `Deletes`, `Access`, `CompanyRules`, `PayRules`, `Money`, `Messaging`), `MaxPendingActions`, `MaxConfirmedActionsPerHour`, `MaxMessagesPerDay` (20) |
| `Prices` | Sonnet 5.5 list prices | `InputPerMillion`, `OutputPerMillion`, `CacheReadPerMillion`, `CacheWritePerMillion` |

`RateLimiting:ProtonPermitsPerMinute` (default 10) limits questions per person per minute.

Locally: `dotnet user-secrets set "Proton:ApiKey" "<key>"` and `"Proton:Enabled" "true"` in `ID-Indetails.api`.

## Logs

Category `ID_Indetails.api.Proton`: each tool call (name, status, duration, parameters with free text replaced by its
length), refusals as warnings, failures as errors. One line per question gives its outcome, wall time,
time to the first model reply, model and tool calls, time in tools, tokens and estimated cost. A rollover logs the
conversation's question and token counts; a question the model declined logs its policy category. Question and
answer text never go to the logs.

## Tests

- Unit (`ID-Indetails.UnitTests/Proton`): periods, overdue, cost and allowances, the read-only guard, log redaction,
  the conversation focus, and the tool catalogue (every tool registered, mirrors a GET endpoint's policies, has a
  valid schema, and no output can carry the excluded fields).
- Integration (`ProtonTests`, `ProtonLimitTests`, `ProtonSwitchedOffTests`, `ProtonActionTests`): the real API with a scripted stand-in
  for the Anthropic API (`FakeAnthropic`) — who may ask, which tools each user is offered, cross-domain and company
  data refused, follow-ups, conversation isolation, invalid tool input, model failure, the budget, top-ups and the
  allowance, and that the database refuses writes while a tool runs. Actions: nothing changes without a confirm, one
  confirm runs once, only the proposer in that conversation can decide, superseded and expired proposals can't run,
  a permission removed after the proposal is refused at confirm, stale updates are refused, service refusals are
  recorded, and a conversation full of "do it without asking" writes nothing.
- Analysis, unit (`ProtonTrendsTests`, `ProtonUsageBreakdownTests`): comparison periods across month lengths, 29 February
  and the week start; changes, shares and contributors; baselines; every threshold at and below its value; signal
  ranking and deduplication; the budget breakdown.
- Analysis, integration (`ProtonAnalysisTests`): `compare_work` and `compare_money` match the work report and the
  Financials summary for the same days; a Design admin without `financial:view` gets no money and is told so; the
  super admin's summary covers both domains and the company; attention ranks each project once with its facts;
  change counts leave pay out; the activity date filter; the heavy-analysis limit; the conversation-size limit; and the
  comparison carried into a follow-up.
- Real model, opt-in (`ProtonEvalTests`): 21 golden questions against a seeded company, checked in code (tools used,
  every quoted number found in a tool result, size words only after a rule fired, "why" answers say what the data
  can't establish, nothing judges a person, no money for a user without `financial:view`). Runs only with
  `PROTON_EVAL_API_KEY` set (optional `PROTON_EVAL_MODEL`), costs about 2–4 USD, never in CI; the answers are written
  to the test output for review. Run it before enabling Proton and after prompt changes:
  `PROTON_EVAL_API_KEY=... dotnet test ID-Indetails.IntegrationTests --filter ProtonEvalTests --logger "console;verbosity=detailed"`.
- Navigation (`ProtonUiTests`, `ProtonNavigationTests`): the command contract (no address fields), one navigation per
  reply, page-context and choice validation; records opened only if visible; pages only for their endpoint's
  permission and from the fixed list; post-confirm navigation only after success and when the card said so; page
  context naming a record only if visible; choices only from found records, a pick checked and usable once. The
  real-model evaluation includes navigation cases.
- Not automated: prompt injection and refusals with the real model. Try the question catalogue in the Stage 1 plan by
  hand too.
