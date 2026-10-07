# Proton — the company assistant

Proton answers questions about Hourline's data in plain language: projects, requirements, meetings, clients,
people, logged time, activity and, for people allowed to see it, money (Stage 1). It can also **propose** a few
changes, which happen only when the user confirms them on a card (Stage 2, [Controlled actions](#controlled-actions-stage-2)).
Proton itself never writes anything. The design and decisions are in [proton-stage-1-plan.md](proton-stage-1-plan.md)
and [proton-stage-2-plan.md](proton-stage-2-plan.md).

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
`this_month`, `last_month`, `this_quarter`, `last_quarter`, `this_year`, `last_year`, `last_7_days`, `last_30_days`,
`last_90_days` or `custom` with from/to. They become company-time-zone days in code, with the company's first day of
the week; running periods end today. Every result says which days it covers.

## Conversations and follow-ups

- Kept **in server memory** (`ProtonConversationStore`), keyed by user and conversation id: nobody else can continue
  someone's conversation. They end after 30 idle minutes, after 4 hours, after 20 questions, or on restart. The client
  never sends history back.
- The message list is exactly what was sent to the model, append-only. A failed question adds nothing to it.
- A **focus** (`ConversationFocus`) is updated from tool results, not guessed from text: the current project, client,
  people and period. It is sent with each question in a short context note, with today's date and who is asking, so
  "it", "they" and "the month before" resolve reliably.
- If the user's role, permissions or domains change, the conversation is closed (answers given under the old access
  must not carry over).

## Controlled actions (Stage 2)

| Tool | Risk | Stands in for (its policies are required to propose *and* to confirm) | Notes |
|---|---|---|---|
| `propose_add_requirement` | low | `POST /api/projects/{id}/requirements` | open projects only; assignees are employees of the domain |
| `propose_update_requirement` | low | `PUT …/requirements/{id}` | text, deadline, assignees; refused if the requirement changed since |
| `propose_set_requirement_done` | low | `POST …/requirements/{id}/done` (`/undone`) | |
| `propose_change_project_status` | medium | `POST /api/projects/{id}/hold` (`/resume`) | hold and resume only: completing, archiving and reopening lock or unlock revenue |
| `propose_update_project` | medium | `PUT /api/projects/{id}` | title, description, location, target date, owner; refused if the project changed since |
| `propose_create_project` | medium | `POST /api/projects` | Design only, client or internal, no money fields |

How it works (`Proton/Actions/`):

1. **Propose.** The model calls a `propose_…` tool. Like every tool it runs inside the read-only guard: it reads the
   target, checks the change with the existing business rules, and validates the exact request the endpoint would
   take, with that endpoint's own validator. The toolbox stores it in `proton_actions` as the conversation's one
   **pending** action (a newer proposal supersedes the older one) and the reply carries it as `pendingAction`. Nothing
   else is written.
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

The client shows Proton as a drawer from the top bar (`proton:use` only), with each proposal as a card listing what
will change (before → after), its risk, and Cancel / Confirm. Typing "yes" confirms nothing.

## Prompts

Markdown files in `Proton/Prompts/` (`proton.md` who Proton is and its rules, `data.md` what it may see and that
people's text is data, `actions.md` how to propose changes and that only the card confirms them, `chat.md` how to
answer in chat), embedded in the assembly. The prompt version recorded with
each question is a hash of their text. The system prompt is the same for everyone, so it is cached by the provider;
per-question facts go in the context note.

## Usage, limits and cost

- Every question is one row in `proton_usage`: who, when, conversation, model, prompt version, outcome
  (`answered`, `refused`, `limited`, `failed`), model and tool call counts, tokens, the **estimated** cost, the question,
  the answer (up to 8,000 characters) and the tool calls (name, parameters, status, duration — never their results).
  Failed questions are recorded too, with the tokens they used.
- **Monthly company budget** (USD): `Proton:MonthlyBudgetUsd` plus that month's top-ups (`proton_budget_top_ups`,
  added by super admins, never edited). Once the month's estimated cost reaches it, nobody can ask until a top-up.
- **Monthly question allowance per person**: `Proton:MonthlyQuestionsPerUser`. People see this allowance (used,
  remaining, percentage) — never money.
- Cost is estimated from `Proton:Prices` (USD per million tokens). The provider's bill is the truth; also set a spend
  limit on the Anthropic workspace as a backstop.

| Endpoint | Permission | |
|---|---|---|
| `POST /api/proton/messages` | `proton:use` | `{conversationId?, text}` → `{conversationId, reply, refs, limited, usage}`; rate policy `proton` |
| `DELETE /api/proton/conversations/{id}` | `proton:use` | ends a conversation |
| `GET /api/proton/usage` | `proton:use` | the caller's question allowance and whether Proton is available |
| `GET /api/proton/budget` | `proton_budget:manage` | this month's budget, top-ups, estimated spend and usage per person |
| `POST /api/proton/budget/top-ups` | `proton_budget:manage` | `{amountUsd, note?}` (up to 10,000 USD) → the budget |

Errors use the uniform contract: `503 proton_unavailable` (switched off, or the model failed), `422 proton_budget_used`,
`422 proton_allowance_used`, `409 proton_busy` (a question is still being answered in that conversation),
`404 proton_conversation_ended`, `429 too_many_requests`.

## Configuration (`Proton` section)

| Setting | Default | |
|---|---|---|
| `Enabled` | `false` | must be true, with `ApiKey`, for Proton to answer |
| `ApiKey` | — | user secrets locally; `Proton__ApiKey` (`PROTON_API_KEY` in the server's `.env`) in production. Required when enabled: the API won't start without it |
| `Model` | `claude-sonnet-5-5` | |
| `Effort` | `low` | `low` … `max` |
| `MaxOutputTokens`, `MaxRoundTrips`, `MaxToolCallsPerQuestion` | 8000, 8, 12 | per question; at the limit the model must answer with what it has |
| `ModelTimeoutSeconds`, `ToolTimeoutSeconds`, `QuestionTimeoutSeconds` | 40, 20, 55 | the question stays under nginx's 60-second read timeout |
| `MaxQuestionsPerConversation`, `ConversationIdleMinutes` | 20, 30 | |
| `MonthlyBudgetUsd`, `MonthlyQuestionsPerUser` | 50, 300 | |
| `Prices` | Sonnet 5.5 list prices | `InputPerMillion`, `OutputPerMillion`, `CacheReadPerMillion`, `CacheWritePerMillion` |

`RateLimiting:ProtonPermitsPerMinute` (default 10) limits questions per person per minute.

Locally: `dotnet user-secrets set "Proton:ApiKey" "<key>"` and `"Proton:Enabled" "true"` in `ID-Indetails.api`.

## Logs

Category `ID_Indetails.api.Proton`: each tool call (name, status, duration, parameters with free text replaced by its
length), refusals as warnings, failures as errors. Question and answer text never go to the logs.

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
- Not automated: answer quality with the real model (tool choice, follow-ups, refusals, prompt injection). Run the
  question catalogue in the plan by hand against a seeded database before enabling Proton and after prompt changes.
