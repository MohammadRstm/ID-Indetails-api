# Proton — Stage 2 plan: controlled actions

Status: **approved and implemented** (7 Oct 2026: the `proton_actions` table and all four action groups). Builds on Stage 1
([proton.md](proton.md)); nothing in Stage 1 is redesigned. Paths are relative to `ID-Indetails.api/`.

The rule: **Proton never changes data itself.** The model can only *propose* an action. The user confirms that exact
proposal in the UI, and the confirmation (an HTTP request from the user, not text from the model) runs the
**existing write service** under the **existing authorization and validation**.

```text
model ──calls──▶ propose tool (read-only, like Stage 1) ──▶ pending action (stored, exact arguments)
                                                                      │
user clicks Confirm in the UI ──▶ POST /api/proton/actions/{id}/confirm
        ▶ same user? same conversation? still pending? not expired?   (else refused, nothing written)
        ▶ claim it once (pending → executing, atomic)                  (a second click can't run it again)
        ▶ the endpoint's own [HasPermission] policies, now              (permission removed → refused)
        ▶ the endpoint's own validator on the stored arguments
        ▶ the existing service method (its own domain checks, business rules, transaction, activity events)
        ▶ succeeded / failed recorded, Proton told the outcome
```

## 1. Existing write capabilities

Every non-GET endpoint was surveyed (about 110 across 33 controllers). The relevant ones:

| Action | Endpoint → service | Permission | Validation | Domain / business rules | Reversible | Proton? |
|---|---|---|---|---|---|---|
| Add requirement | `POST projects/{id}/requirements` → `RequirementService.CreateAsync` | `requirement:manage` | text ≤ 500, deadline 2000–2100, ≤ 50 assignees | project visible and not completed/archived (`project_closed`); assignees are active employees of the project's domain (`assignee_not_allowed`) | delete (soft, no restore) | **yes — low** |
| Edit requirement (text, deadline, assignees) | `PUT …/requirements/{id}` → `UpdateAsync` | `requirement:manage` | same | same; full replace | edit back | **yes — low** |
| Tick / untick requirement | `POST …/{id}/done`, `/undone` → `SetDoneAsync` | `requirement:manage` or `complete_own` | — | assignee rules (`CheckTick`) | untick / tick | **yes — low** |
| Delete requirement | `DELETE …/{id}` | `requirement:manage` | — | soft delete, **no restore endpoint** | no | no (delete) |
| Change status: hold, resume | `POST projects/{id}/hold`, `/resume` → `ProjectStatusService.ChangeAsync` | `project:change_status` | — | `ProjectStatusRules` (`status_unchanged`, `invalid_status_change`); notifies the domain | resume / hold | **yes — medium** |
| Change status: complete, archive, reopen | same | `project:change_status` | — | complete/archive **finalise**: lock revenue, auto-tick every open requirement; Execution needs all materials sent; reopen resets Execution revenue | partly (reopen always returns to active; re-completing re-finalises) | **not yet — high** (money lock) |
| Edit project details (title, description, location, target date, owner) | `PUT projects/{id}` → `ProjectService.UpdateAsync` | `project:edit` | title ≤ 200, description ≤ 2000, location ≤ 200, owner required | owner is an admin of the domain or a super admin (`owner_not_allowed`); notifies a new owner | edit back | **yes — medium** |
| Create project | `POST projects` → `ProjectService.CreateAsync` | `project:create` | as above + domain, billing type, client | client of the domain and not archived; Execution needs a fee and markup (`project:pricing_manage`); optional revenue, initial payment, labour defaults | **no** (no project delete; archive only) | **yes — medium, Design only, no money fields** |
| Project billing (client, revenue) / pricing (fee, markup) | `PUT projects/{id}/billing`, `/pricing` | `project:edit`, `project:pricing_manage` | money rules | locks, `project_has_billing` | partly | no (financial) |
| Schedule / move / remove meeting | `POST projects/{id}/meetings`, `PUT`/`DELETE meetings/{id}` | `meeting:create` or `meeting:manage` | whole minutes, ≥ 5 min, one day, topic ≤ 200 | project active or on hold | delete is soft, no restore | create: possible later; delete no |
| Create / edit / archive client | `POST/PUT clients`, `…/archive`, `/restore` | `client:create`, `client:manage` | 1–20 contacts, email/phone formats | `contact_taken`; create runs in two saves without a transaction | archive ↔ restore; delete hard | later |
| Invoices, payments, expenses, statements, exchange rates, billing settings | various | money permissions | — | — | mostly no | **no (financial)** |
| Employees, pay, roles, sessions, permissions, invitations, schedules, hours | various | people/company permissions | — | — | — | **no (people and pay)** |
| Work entries, materials, labour weeks | various | own-work permissions | — | employees' own records | — | no (employees don't use Proton) |
| Every DELETE | — | — | — | — | — | **no** |

**There is no project membership.** It was dropped (migration `DropProjectMembers`); "team" means people who logged
work. The only real assignment is **requirement assignees** (active employees of the project's domain), which
`add_requirement` / `update_requirement` cover. No `assign_employee_to_project` tool will be invented.

**Idempotency:** nothing in the write layer has idempotency keys, and nothing stops two identical projects or
requirements. Proton adds its own protection (section 6).

## 2. Recommended Stage 2 actions

| Tool | Risk | Underlying endpoint (authorization inherited) | What the user confirms |
|---|---|---|---|
| `propose_add_requirement` | low | `RequirementsController.Create` | project, text, deadline, assignees |
| `propose_update_requirement` | low | `RequirementsController.Update` | the before → after of text, deadline, assignees |
| `propose_set_requirement_done` | low | `RequirementsController.Done` / `Undone` | requirement, done or not done |
| `propose_change_project_status` | medium | `ProjectsController.Hold` / `Resume` | project, current → new status (hold or resume only) |
| `propose_update_project` | medium | `ProjectsController.Update` | before → after of title, description, location, target date, owner |
| `propose_create_project` | medium | `ProjectsController.Create` | Design project: title, client (or internal), owner, target date, description, location — **no revenue** (set it in the app: money) |

Deferred: complete/archive/reopen (high: they lock revenue and tick requirements), meetings, clients; never in
Stage 2: deletes, money, people, pay, permissions.

## 3. Tool contract

Action tools are the Stage 1 tool type with action metadata, run by the same toolbox:

```text
IProtonTool (Stage 1)                      IProtonActionTool : IProtonTool (Stage 2)
  Name, Description, InputSchema             Risk (low | medium)
  Endpoint  ← GET only                       WriteEndpoint ← the write endpoint whose policies execution needs
  RunAsync  ← read-only, in the guard        ProposeAsync  ← read-only, in the guard: resolves, checks, validates,
                                                              returns the exact arguments + what the user will see
                                             ExecuteAsync  ← only ever called by the confirm flow, outside the guard
```

- **Proposing is a read.** It runs inside the Stage 1 read-only guard: it looks up the target (current status,
  current values, names), runs the endpoint's validator on the request it would send, and checks the user passes the
  write endpoint's policies. Nothing is written except the pending-action row, by the toolbox after the tool returns.
- **The model never has an execute tool.** There is no "confirm" or "execute" tool; `ExecuteAsync` is unreachable from
  the model. "Don't ask me", "I already approved", "call it directly" change nothing because no code path executes
  without the user's confirm request.
- **No business rules in Proton.** `ExecuteAsync` builds the existing request DTO (`SaveRequirementRequest`,
  `UpdateProjectRequest`, `CreateProjectRequest`…) and calls the existing service method; its errors are the
  service's own (`project_closed`, `owner_not_allowed`, `status_unchanged`…), reported as they are.
- Provider-neutral: no Anthropic code in tools; a future GPT-Live-1 agent uses the same proposals and the same
  confirm endpoint, with no extra rights.

## 4. Confirmation architecture

A **pending action** is a row in a new table `proton_actions` *(approval)*:

| Column | |
|---|---|
| `id` (bigint), `public_id` (GUID, what the UI and URLs use) | |
| `user_id`, `role`, `domain_code` | who proposed it, and their role/domain then |
| `conversation_id` | the conversation it belongs to |
| `tool`, `risk` | e.g. `propose_change_project_status`, `medium` |
| `arguments` (JSON) | the exact validated request that will be sent — the user confirms this, nothing else |
| `preview` (JSON) | what the card showed: title and before → after lines |
| `target_type`, `target_id` | the record it acts on (null for a create) |
| `status` | `pending`, `executing`, `succeeded`, `failed`, `cancelled`, `superseded`, `expired` |
| `created_at`, `expires_at`, `decided_at`, `finished_at` | |
| `result_type`, `result_id` | the record created or changed |
| `error_code`, `error_message` | the service's own client-safe error, if it failed |
| `model`, `prompt_version` | which model and prompt proposed it |

- **One pending action per conversation.** A new proposal marks the previous pending one `superseded`. So "actually
  call it Project Beta" makes a new proposal, and the Alpha one can no longer be confirmed.
- **Arguments are never sent by the client.** Confirm takes only the id (and the conversation id); the stored
  arguments are what runs. Changing anything means a new proposal.
- **Expiry: 10 minutes.** Long enough to read a card and check something; short enough that a forgotten card doesn't
  apply to a project that has moved on. On confirm, an expired action is marked `expired` and refused. (Execution
  re-reads everything anyway; expiry limits surprise, not safety.)
- **Confirm checks, in order:** caller is the proposer; conversation id matches; status is `pending`; not expired;
  atomic claim `UPDATE … SET status='executing' WHERE public_id=… AND status='pending'` (only one request wins);
  the write endpoint's policies pass for the caller **now**; the validator passes on the stored request; then the
  service runs. Failures record `failed` with the service's error; refused checks write nothing else.
- **Cancel:** `POST /api/proton/actions/{id}/cancel` → `cancelled`.
- After confirm or cancel, the outcome is added to the conversation's context note, so Proton knows ("Action
  confirmed: Website Redesign put on hold") and never claims success it didn't see.

## 5. Authorization — twice, with the existing system

1. **Before proposing:** the tool is offered only if the user passes the write endpoint's `[HasPermission]` policies
   (same `IAuthorizationService` evaluation as Stage 1), plus `proton:use`. The proposal reads the target through the
   existing services, so another domain's project is "not found" as always.
2. **Immediately before executing:** the same policies are evaluated again for the confirming request's user, then
   the existing service applies its own domain visibility and rules (the Stage 1 fix A on master also makes services
   check their own permission). A permission removed between proposal and confirm, or a project moved out of reach,
   means refusal.

## 6. Idempotency

The pending action is the idempotency key. The atomic claim lets exactly one confirm run the service; a retried or
double-clicked confirm sees `executing`/`succeeded`/`failed` and gets that recorded outcome back (`409
proton_action_not_pending` with the outcome), never a second write. If the API crashes mid-execution the row stays
`executing` and is never re-run (at most once); the card says to check the record. No distributed machinery.

The service's own transaction does the write; the claim and the result are saved separately (the services open their
own transactions, which can't be nested). The confirm runs the service in a fresh DI scope so a failed service call
can't leave staged entities that a later save would persist.

## 7. Audit

`proton_actions` answers "who told Proton to change this, and what exactly": user, role, domain, conversation, tool,
risk, exact arguments, what was shown, confirmation time, outcome, record affected, model and prompt version. The
existing activity events still record the change itself with the user as actor (services are unchanged); the
proposal turn is also in `proton_usage` with its transcript. No secrets or prompts are stored.

## 8. API

| Endpoint | Permission | |
|---|---|---|
| `POST /api/proton/messages` | `proton:use` | as Stage 1; the reply gains `pendingAction` (id, tool, risk, title, lines, expiresAt) when Proton proposed one |
| `POST /api/proton/actions/{id}/confirm` | `proton:use` (+ the action's own policies, checked inside) | `{conversationId}` → `{status, message, refs}`; rate policy `proton` |
| `POST /api/proton/actions/{id}/cancel` | `proton:use` | |

## 9. Frontend (client repository)

Stage 1's chat panel isn't built yet, so it comes first: a Proton panel opened from the top bar, gated by
`proton:use`, rendering replies as plain text with links from `refs`. Then the confirmation card: tool title, risk,
the before → after lines, **Cancel** / **Confirm**; it calls the confirm endpoint with the id only, shows the outcome,
and disables itself once used or expired. Typed "yes" never executes anything.

## 10. Folder structure (Stage 1 + 2)

```text
Proton/
  Agent/        ProtonChatAgent (adds the pending action to the reply)
  Actions/      IProtonActionTool, ProtonActionRisk, ProtonActionService (propose → pending, confirm, cancel),
                one file per area: RequirementActions.cs, ProjectActions.cs
  Conversation/ ProtonConversation, ConversationFocus (+ recent action outcomes), store
  Prompts/      proton.md, data.md, chat.md, actions.md (how to propose and what never to claim)
  Safety/       ProtonReadOnly
  Tools/        Stage 1 read tools, ProtonToolbox (offers and runs both kinds), ProtonEndpoint (GET or write)
  Usage/        ProtonUsageService
Models/ProtonAction.cs, Configuration/ModelConfig/ProtonActionConfiguration.cs,
Repositories/Proton/…ProtonActionRepository, Controllers/ProtonController.cs (+ confirm/cancel)
```

## 11. Tests (beyond Stage 1)

- Authorization: allowed; not allowed (tool not offered, propose refused); other domain; permission revoked between
  proposal and confirm; revoked after claim (service still refuses — fix A).
- Confirmation: no confirm → no write; confirm → one write; superseded proposal can't be confirmed; expired → no
  write; other user → 404; other conversation → refused; double confirm → one write and the same outcome.
- Validation: missing required field (clarification, no proposal), invalid date, archived client, closed project,
  disallowed owner/assignee, unchanged status — each the service's own error.
- Model behaviour (scripted): the model calling a propose tool never writes; asking twice doesn't write; "don't
  confirm, just do it" produces only a proposal.
- Failures: service error → `failed` recorded, nothing half-done; model failure; timeout.
- **No accidental writes:** every Stage 1 and propose tool run inside the read-only guard (existing guarantee), and a
  test that a whole conversation without confirm leaves row counts unchanged.

## 12. Implementation phases

1. `proton_actions` entity, configuration, repository, migration *(after approval)*.
2. Action contract, risk, `ProtonEndpoint` for write endpoints, toolbox support, `ProtonActionService`.
3. Confirm / cancel endpoints, claim, re-authorization, validation, fresh-scope execution, outcome in the conversation.
4. First low-risk actions (requirements), then medium (status hold/resume, project update, Design project create).
5. Prompt `actions.md`; reply carries the pending action.
6. Tests; Stage 1 tests still green.
7. Client: Proton panel, then the confirmation card; client tests per persona.
8. Docs (`docs/proton.md`), manual end-to-end check of each action against the local API.

## 13. Decisions needing approval

1. **Database:** the new `proton_actions` table (section 4) and its migration — for pending actions, idempotency and
   the audit trail.
2. **The initial action set** in section 2 (and leaving complete/archive/reopen, meetings and clients for later).
