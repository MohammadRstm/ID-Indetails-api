# Proton Stage 5: full assistant (implementation plan)

**Repositories.** Backend: `C:\Users\masst\source\repos\ID-Indetails`. Frontend: `C:\Users\masst\Desktop\Ident-Internal-Management-System-client`. Both are on `MR-Proton-Navigation`. This plan comes from eight read-only investigations, two review passes and spot checks in code. Nothing has been changed.

**How paths are written.**
- `api/…` means `ID-Indetails.api/…`.
- `repo/…` means the backend repo root (`docs/`, `deploy/`, test projects).
- `web/…` means the client repo root.

**Checked in code while writing this plan**
- **Tools.** 40 are registered in `api/Extensions/ProtonServiceCollectionExtensions.cs`:
  - 30 read and analysis tools (24 reads, 6 analysis);
  - 4 UI tools;
  - 6 `propose_*` actions.

  The brief said "about 34" read tools. The code has 30.
- **Write endpoints.** There are 118 (`[HttpPost]`, `[HttpPut]`, `[HttpDelete]`) across 31 controllers. Each one is classified in 2.1.
- **One UI command per reply.** `ProtonUiRules.Pick` (`api/Proton/Ui/ProtonUiCommand.cs:92`) returns at most one navigation, or else one refresh. The client accepts two (`web/src/domains/shared/proton/uiCommands.ts:95`) but runs only one navigation.
- **The panel sits behind modals.**
  - `Drawer` (`web/src/shared/components/Overlays/Modal.tsx`) is not portaled. `ProtonPanel` renders inside `TopBar`, and `.top` (`web/src/styles/hourline.css:139`, sticky, z 35) creates a stacking context. Portaled scrims (z 100, on `body`) therefore cover the drawer's z 101.
  - `Modal` closes on a React `onMouseDown` on the scrim, but only when the click target is the scrim itself (`Modal.tsx:55`). A dock portaled above the scrim never triggers it.
  - What does affect Proton:
    - the document-level Escape (`Modal.tsx:44`);
    - `dialog.focus()` on open, unless a field already took focus (`Modal.tsx:38`);
    - `aria-modal="true"` (`Modal.tsx:59`).
- **Focus and Enter.**
  - These forms have `autoFocus` fields: `CreateProjectModal`, `EditProjectModal`, `ClientModal`, `ExpenseModal` and `LogWork`.
  - The messages composer sends on Enter (`web/src/domains/shared/messages/Composer.tsx:113`).
- **Admins never log their own hours.**
  - `PermissionCatalog.OwnWork` (`work_entry:create`, `work_entry:view_own`) is removed from the admin and super-admin grants (`api/Constants/PermissionCatalog.cs:106,125,127`, `api/Business/Auth/EffectivePermissions.cs`).
  - The work-entry writes need `work_entry:create` (`api/Controllers/WorkEntriesController.cs:57-76`).
  - `my-work`, `pay` and the area `profile` route are employee-only (`web/src/app/routing/routeTable.tsx:136-169`).
- **Permissions are seeded.**
  - `api/Configuration/ModelConfig/PermissionConfiguration.cs:27` seeds codes and descriptions with `HasData`, and `RolePermissionConfiguration.cs:30` seeds the role grants. Changing the `proton:use` description, or granting it to employees, generates a migration.
  - That description already reads "the read-only company assistant" (`PermissionCatalog.cs:81`), which has been wrong since Stage 4.
  - A new C# property on `PermissionCatalog.Definition` is not a schema change, because `HasData` projects only `Code`, `Description` and `DomainCode`.
- **`proton_actions`.**
  - `tool` is `varchar(64)`. `target_type` and `result_type` are free strings.
  - `risk` has `ck_proton_actions_risk`, built from `ProtonActionRisk {Low, Medium}`.
  - Indexes: `(UserId, CreatedAt)`, `(ConversationId, Status)` and `(TargetType, TargetId)` (`api/Configuration/ModelConfig/ProtonActionConfiguration.cs:36-38`).
- **`proton_usage`.**
  - It keeps question and answer text forever.
  - Its `tools` column stores raw tool inputs (`input = t.Input`, `api/Proton/Agent/ProtonChatAgent.cs:459`). This contradicts the model comment (`api/Models/ProtonUsage.cs:44`), and the existing `ProtonInputSummary` helper (`api/Proton/Tools/ProtonToolbox.cs:283`) is used only for logs.
  - Its index is `(UserId, OccurredAt)`.
  - `ProtonConversationStore` says "the transcript is in proton_usage" (`api/Proton/Conversation/ProtonConversationStore.cs:9`).
- **Guardrail tests** (`repo/ID-Indetails.UnitTests/Proton/ProtonToolCatalogTests.cs`).
  - The tests pin the 6 action names, forbid `Delete` endpoints, and forbid 11 money policies. `overhead:*`, `statement:*`, `material:*`, `labour_report:*`, `working_time:manage`, `hours:excuse` and `schedule:manage` are missing from that list.
  - `No_tool_output_can_carry_the_excluded_fields` walks only the `Proton.Tools` namespace (`:130`). So `ProtonChange` (free strings, in `Proton.Actions`) is not covered.
  - `DailyPay` is not in the excluded list.
- **`ProtonReadOnly` is only a `DbCommandInterceptor`** (`api/Proton/Safety/ProtonReadOnly.cs:47`). It doesn't stop `IEmailSender`, `IFileStorage`, `INotificationSender` or `ILiveUpdates`.
- **External sends.**
  - `BugReportService` emails support (`api/Services/Support/Implementation/BugReportService.cs:46`).
  - `Invitations.Create`, `Invitations.Resend`, `Employees.ChangeEmail`, `Invoices.Email` and `Statements.Email` carry `[EnableRateLimiting(Email)]`.
  - `InvoiceScheduleService` emails scheduled invoices (`:69`).
  - `EmployeeService.ReactivateAsync` sends no email. It only sets the status, to invited for someone who never signed up.
- **Role and domain changes.** `Employees.ChangeRole` and `Employees.Move` clear personal permission overrides (`api/Services/Employees/Implementation/EmployeeService.cs:310,342`). `Employees.Update` needs `employee:manage`.
- **Pay in employee reads.** `EmployeeResponse.CurrentPay` is filled for callers with financial or pay access (`api/DTO/Response/Employees/EmployeeResponses.cs:56`).
- **Messages.** There is no edit or delete endpoint (`api/Controllers/MessagesController.cs`). A chat opens only in `MessagesPage` (`?with=` or `?group=`), the only place `Thread` and `Composer` render.
- **Time.** `IClock`/`SystemClock` (`api/Services/Common/Implementation/SystemClock.cs`) has `ToCompanyTime` and `CompanyDayStartUtc`, but nothing that converts a local time of day to UTC.
- **Product name.** `Email:ProductName` already exists (`api/Configuration/Options/EmailOptions.cs:31`, "ID InDetails").
- **Errors.** `api/Middlewares/GlobalExceptionHandler.cs` maps exceptions to errors. `api/Middlewares/ApiErrorWriter.cs` builds the uniform body (`Build`).
- **Infrastructure.**
  - nginx `location /api/` already has `gzip off` (`web/deploy/nginx/default.conf.template:47`) and nginx's default 60 s timeouts. It honours an upstream `X-Accel-Buffering: no`.
  - `repo/deploy/Caddyfile` has `encode zstd gzip`.
  - A SignalR hub (`/hubs/`, `api/Hubs/LiveHub.cs`) already exists.
  - `QuestionTimeoutSeconds = 55` is documented as staying under nginx's 60 s (`api/Configuration/Options/ProtonOptions.cs:40-42`).
- **Client.**
  - 15 app forms use `useState` plus `useFormErrors`. `useForm` (zod) is used only by the 5 auth pages. `web/src/shared/format/limits.ts` mirrors the API's number limits.
  - `web/src/api/http.ts` exports `refreshSession` and `sessionExpired`, and axios is `^1.20`.
  - `billingLinks.ts` pushes a history entry on open and replaces it on close (`:14`).
  - `onSessionReset` clears only toasts, file URLs and log work.
  - `TopBar` shows `ProtonButton` only with `P.PROTON_USE` (`TopBar.tsx:59`).
  - `LiveRoute` (`web/src/app/live/LiveRoute.tsx`) wraps both the `/company` launcher and `AppShell`, inside `RequirePasswordChanged` (`web/src/App.tsx:64-73`).
  - `ReportBugModal` has two local hosts (`Sidebar.tsx:166`, `TopBar.tsx:123`).
  - `overdue=1` is linked from `web/src/app/shell/searchPlaces.ts:161` and from help content, but the Billing tab ignores it.
- **SDK.** Anthropic 12.53.0. From the Claude API reference, not yet compiled against:
  - tool search (`ToolSearchToolRegex20251119`/BM25 with `defer_loading`) is non-beta in C#;
  - `thinking.display: "updates"` is a beta (`thinking-display-updates-2026-08-18`) on the `Anthropic.Models.Beta.Messages` types;
  - the cache prefix is tools → system → messages, so a changed `tools` array invalidates it;
  - Sonnet 5.5 accepts `role: system` messages in the middle of a conversation without a beta.

---

## 1. Where Proton is today

- **Who has it.**
  - Admins and super admins, through `proton:use`. Employees don't have Proton.
  - Admins never hold the own-work permissions, so anything about "my work" needs employee access (decision 26).
- **What it reads (30 tools).**
  - Records: projects and their requirements, team, financials and site summary; work entries and time summaries; people and their work; activity; meetings; the domain dashboard; clients; domain finance and finance projects; money attention; invoices and payments; the company overview.
  - Analysis: `compare_work`, `compare_money`, `review_project`, `get_company_summary`, `summarize_changes`, `get_attention`.
  - Every tool inherits the `[HasPermission]` policies of a real GET endpoint (`ProtonEndpoint.Of`). It runs inside `ProtonReadOnly`, which refuses database writes only.
  - Salaries, pay, bank details and credentials never reach the model. This is enforced, for tool output types only, by `No_tool_output_can_carry_the_excluded_fields`.
- **What it opens (4 UI tools).** `open_record`, `open_page`, `offer_choices`, `refresh_page`.
  - The contract has 5 command types: OpenProject, OpenClient, OpenPerson, OpenPage, Refresh.
  - Proton can reach 10 pages, 3 record types, 4 project sections and 5 financials tabs.
  - The server sends one command per reply. `uiCommands.ts` checks it against `routeTable` `canOpen`.
  - A pick from `offer_choices` comes back as `AskProtonRequest.Choice`, and the server rebuilds the question itself. This is the only turn origin the server knows today.
- **What it does: 6 confirmed actions covering 8 of 118 write endpoints.**
  - The actions: add requirement, update requirement, set requirement done/undone, hold/resume a project, update project details, create a Design project (no money).
  - **Propose step.** `propose_*` runs inside the read-only guard and does the following:
    - reads the target through the GET services;
    - pre-checks with `Business/*Rules`;
    - validates with the endpoint's FluentValidation validator;
    - stores a `proton_actions` row, with its risk taken from the tool.

    There is one pending action per conversation, and it expires after 10 minutes.
  - **Confirm step.** `POST /api/proton/actions/{id}/confirm` runs the action at most once. It re-checks the endpoint's policies, validates again, and calls the same service method as the controller.
- **Platform.**
  - Model: Claude Sonnet 5.5 at effort low, with adaptive thinking and an automatic top-level cache. A manual tool loop runs in `api/Proton/Agent/ProtonChatAgent.cs`.
  - Replies arrive whole: no streaming, and a 55 s question timeout.
  - Conversations are in memory only. A conversation ends after 30 idle minutes, 4 h, 20 questions, 80k input tokens, a restart, or a change to the access fingerprint.
  - Limits: 300 questions per user per month and a $50 monthly company budget.
  - Prompts live in `api/Proton/Prompts/*.md`: proton, data, analysis, navigation, actions, chat.
- **What users see.**
  - A compass icon in the top bar opens a modeless 520 px drawer. Chat state (`ProtonChat`, `web/src/domains/shared/proton/protonChat.ts`) is owned by the top bar.
  - While Proton works, the drawer shows "Proton is looking…" for up to 55 s.
  - Replies come with record links, a sources note and record-choice buttons.
  - Actions appear as cards with a Low/Medium pill and Confirm/Cancel buttons.

## 2. What is missing

### 2.1 Action coverage (118 write endpoints, 31 controllers)

The tiers are defined in 3.2. N means "opened for the user": Proton opens the real surface, and the user fills it in and submits.

| Area (controllers) | Write endpoints | Proton today | T1 | T2 | T3 prefill | N open-only | T4 human-only | Proton's own | Read-only POST |
|---|---|---|---|---|---|---|---|---|---|
| Projects and site work: Projects, Clients, Requirements, Meetings, WorkEntries, Expenses, Materials, Labour, Statements, Files, BoardLayouts | 43 | 8 | 21 | 18 | 0 | 2 | 0 | 0 | 2 |
| Money and company rules: Invoices, Payments, BillingSettings, Overhead, Company, HourExcuses, Schedules, DomainSettings, MyPay | 33 | 0 | 0 | 26 | 0 | 6 | 0 | 0 | 1 |
| People, access, messaging, platform: Employees, Invitations, Permissions, Teams, Profile, Auth, Setup, Notifications, Messages, BugReports, Proton | 42 | 0 | 3 | 8 | 2 | 14 | 11 | 4 | 0 |
| **Total** | **118** | **8** | **24** | **52** | **2** | **22** | **11** | **4** | **3** |

**What that adds up to.** Take away the 3 read-only POSTs and Proton's own 4 controls (ask, confirm, cancel, end conversation). That leaves 111 user actions. After Stage 5:
- **76** are carried out on a confirmation card (24 T1, 52 T2). 8 of them exist today.
  - 4 of them (log, edit, delete and restore your own work) only reach anyone once employees get Proton (decision 26).
- **2** are prepared in the real form, and the user sends them.
- **22** are opened for the user to complete.
- **11** stay human-only.

A tier is a floor. A spec may raise it for a particular input, for example a team message reaching more than 10 people, or a comment sent to the group. It can never lower it.

**Every endpoint by tier** (`*` = exists today, `†` = employees only)
- **T1 (24).**
  - Projects: Create\*, Update\*, Hold\*, Resume\*.
  - Clients: Create, Update, Archive, Restore.
  - Requirements: Create\*, Update\*, Done\*, Undone\*.
  - Meetings: Create, Update.
  - WorkEntries: AddComment, Create†, Update†, Delete† (Restore exists), Restore†.
  - BoardLayouts: Save, Delete.
  - Teams: Create, Rename.
  - Profile: UpdateProfile.
- **T2 (52).**
  - *Lifecycle:* Projects Complete, Archive, Reopen.
  - *Deletes:* Clients.Delete (hard delete), Requirements.Delete, Meetings.Delete, Teams.Delete.
  - *Access:* Employees Update, Deactivate, Reactivate, EndSessions, RestoreSignIn.
  - *Messaging:* Messages SendDirect, SendToTeam.
  - *CompanyRules:* Company.UpdateSettings, DomainSettings.Save.
  - *PayRules:* Company.SetWorkingTime, HourExcuses.Add, Schedules.Set.
  - *Money (33):*
    - Projects UpdateBilling, UpdatePricing;
    - Expenses Create, Delete;
    - Materials Add, Update, Delete, SetPrice;
    - Labour SubmitWeek, SetDefaults;
    - Statements Create, RecordDelivery;
    - Invoices Create, Update, Discard, Issue, RecordDelivery, Unschedule, Void;
    - Payments Record, Apply, Void;
    - BillingSettings Save (non-bank fields), AddExchangeRate;
    - Overhead AddGroup, RenameGroup, RemoveGroup, Create, Update, ChangeAmount, ChangeCurrency, Cancel, Resume.
- **T3 prefill (2).**
  - BugReports.Report.
  - Invitations.Create. Email, position and team only; the email only when that exact address is in the user's own typed words.
- **N open-only (22).**
  - Files.Upload; Employees SetPhoto, RemovePhoto; Profile SetPhoto, RemovePhoto; Company SetLogo, RemoveLogo.
  - Invoices Email, CreatePlan, Schedule (scheduling emails the invoice automatically); Statements.Email; Invitations.Resend.
  - Employees ChangeEmail, ChangePay, ChangeRole, Move; Permissions.Update.
  - Messages MarkTeamRead, MarkDirectRead; Notifications MarkRead, MarkAllRead.
  - HourExcuses.Delete.
- **T4 human-only (11).** The 7 Auth endpoints; Setup.CreateSuperAdmin; Invitations Validate and Accept; Proton.TopUp.
- **Read-only POST (3).** Clients.Check, Statements.Preview, MyPay.OvertimePreview.

**Structural blockers in today's code**
- **`ProtonEndpoint`** (`api/Proton/Tools/ProtonEndpoint.cs`):
  - `Of` accepts only GET.
  - `Build` throws on `[Authorize]`-only endpoints: Messages, Notifications, Profile, BugReports, Files, BoardLayouts, and the GETs for teams, company settings and domain settings.
  - It ignores `[EnableRateLimiting]`. The plan avoids that gap with a coverage rule (4.1) instead of a second limiter.
- **The read-only guard is too narrow.** `ProtonReadOnly` blocks database writes only. Emails, file writes, notifications and live signals would still run during a propose step.
- **Previews aren't covered by the pay and bank test.** The excluded-fields test can't see previews or stored arguments. A `Load` built from `EmployeeService.GetAsync` would carry `CurrentPay`.
- **Risk handling is too thin.**
  - Only two risk levels exist (Low/Medium, behind a check constraint). The tier comes from the stored row.
  - `ActionCard.tsx` has a single Confirm button.
  - `DecideProtonActionRequest` carries only `ConversationId`.
- **One pending action per conversation.** `ProtonToolbox.ProposeAsync` supersedes the rest.
- **No turn origin for typed text.** Apart from `Choice`, the server can't tell typed text from text the app wrote.
- **Missing read tools for finding targets.** Single invoice and payment; expenses, materials, labour weeks and defaults; statements; a single work entry and its comments; the user's own work; teams; messaging contacts; notifications; company, domain and billing settings (without bank fields); exchange rates; overhead lines; schedules; working time.
- **No company-time conversion.** Services treat a DateTime with no time zone as UTC (`WorkEntryRules.AsUtc`), and `IClock` can't convert a local time to UTC.
- **No undo** for requirement, meeting, expense and material deletes (soft deletes with no restore) or for client delete (a hard delete).
- **No coverage registry,** no record of what Proton did, and no kill switch that covers the existing actions.

### 2.2 UI reach

| Surface | In the app | Proton today | Gap |
|---|---|---|---|
| Pages (routes) | 31: 18 per business area, 11 company, the launcher, change-password | 13: 10 pages plus project, client and person records | 18 pages, listed below the table. |
| Tabs and sections | Project tabs (4), financials tabs (5), company overview tabs (3), settings sections (8), reports tabs (local state), notifications filter, help topics and articles | Project tabs and financials tabs | Company overview, settings, reports, notifications, help |
| Modals and drawers | About 30 open only from local state; about a dozen from the URL (`?new=1`, `?invite=1`, `?add=1`, `?money=`, `?report=1`, `?complete=1`, `?statement=`, `?invoice=`/`?edit=`/`?send=`/`?pay=`, `?entry=`, `?with=`/`?group=`, `?section=`) | None | All of them, except the one-click confirm dialogs of T2 and T4 actions. Those stay out of reach on purpose (3.5). |
| Forms | About 15 app forms (`useState` plus `useFormErrors`), plus inline forms | None | The fillable ones (5C). Immediate-save controls (settings toggles, material cells, statement prices, owner popover, status menus) can't be filled. |
| Filters and periods | Projects table, clients, people, activity, reports, my work, meetings, materials, salaries, project pay, plus the dashboard, financials and company periods | `?client=` only | Nearly all |

The 18 missing pages:
- **Area pages admins can reach:** settings, notifications, messages, help.
- **Company pages:** salaries, project pay, schedules, Proton, settings, profile, notifications, messages, help.
- **The launcher.**
- **Employee-only pages** (decision 26): my-work, pay, the area profile.
- **Change-password:** navigation only.

**Things that block the rest**
- **The panel sits behind modals.** While a modal is open, a click in the panel lands on the scrim and closes the modal, and Escape typed in the composer also closes it.
- **Proton could steal focus.** A modal or an `autoFocus` field that Proton opens takes the keyboard. The user's next keystrokes and Enter would then submit a form Proton filled, or send a message Proton drafted. Neither would have a card.
- **One-click confirm dialogs.** PersonActions' Deactivate, End sessions, Role, Move and Restore dialogs, `CompleteProjectModal` (`?complete=1`) and the void and discard dialogs each have a single Go button. Opening them for the user would turn a T2 action into one click.
- **Screen readers lose the panel.** Any layer outside the dialog is inert to assistive technology while `aria-modal="true"` is set.
- **The page context doesn't report the open modal.** `useProtonPageContext.ts` knows only the 10 pages.
- **No command sequences.** The server sends one command, and the executor can't run "navigate, wait until mounted, open, fill" or confirm that a surface actually rendered.
- **URL key collisions.** `?edit=` is already used by the billing modals on the project and client pages.
- **Existing bug.** The `financials?tab=billing&overdue=1` link is ignored, because the Billing tab filter is local state.

### 2.3 Messaging

- **No tools at all.** Proton has none for messages or notifications. That was an approved choice in Stages 1 and 3.
- **The endpoints can't be wrapped yet.** `MessagesController` and `NotificationsController` are `[Authorize]`-only. Access is decided in `api/Business/Messaging/MessagingRules.cs` and `MessagingService`.
- **Finding a recipient.** `find` resolves people through `EmployeesController.List` (`employee:view`). That is a different set from who the user may message (`IMessagingService.ContactsAsync`). There is no `Team` ref type.
- **Messages can't be taken back.** There is no edit or delete endpoint, so every send is irreversible.
- **The UI side.**
  - Chats open only in `MessagesPage`.
  - `Composer.tsx` can't be prefilled, and it sends on Enter.
  - `ActionCard` has no variant for a send.
- **No marker or cap.** Nothing tells recipients that Proton drafted a message, and nothing caps how many messages Proton sends.
- **No reminders.** Notifications have no due time, the scheduler ticks every 60 minutes, and `NotificationSender` drops self-notifications.
- **The one existing path.** Work-entry comments with `SendToGroup` already post into a team chat.

### 2.4 Conversation quality and human feel

- **No persona.** The prompts are lists of rules.
  - Proton talks about itself in the third person.
  - `analysis.md` mandates stock phrases ("Based on this data…").
  - Nothing covers greetings, small talk, replying in the user's language, or being honest about being an AI.
- **Wrong product name.** 10 prompt mentions say "Hourline" (4 in `proton.md`, 2 in `analysis.md`, 2 in `data.md`, 1 each in `actions.md` and `navigation.md`), and so does `ActionCard.tsx:64`. Users only ever see "ID InDetails" (`Email:ProductName`) and the company branding name.
- **Hard stops show up as red errors.** "This conversation is full", and `proton_conversation_ended` is not handled in the client. A reload or a deploy loses the chat.
- **The user always speaks first.** Quick replies can only offer projects, clients or people.
- **One step at a time, no undo, no memory of preferences.**

### 2.5 Visual

- **Today:** a static compass icon in the top bar, shown only with `proton:use`, and a side drawer. There are no Proton tokens or animations, and nothing handles reduced motion in JavaScript.
- **Two existing bugs.**
  - Closing the drawer while Proton is answering drops the reply and its UI commands. The `ask.mutate` callbacks don't run after unmount.
  - Visiting the `/company` launcher (outside `AppShell`) loses the chat.
- **No real signal for "inflates when it talks",** because replies arrive whole.

### 2.6 Platform

- **No streaming.** Notes the model writes between tool calls come back as empty thinking blocks unless the beta `display: "updates"` is used.
- **Every permitted tool schema goes out on every call.** That is 40 today and about 120 once actions cover everything.
- **`Echo` drops block types.** It passes back only text, thinking and tool_use, which blocks tool search.
- **No explicit cache breakpoint on the system prompt.**
- **One timeout for everything.** `QuestionTimeoutSeconds` is shared, and documented as staying under nginx's 60 s.
- **Nothing is measured.** Latency and cost per question have never been measured, and `ProtonEvalTests` (21 cases, `repo/ID-Indetails.IntegrationTests/Api/ProtonEvalTests.cs`) has never been run.
- **Text is kept forever.** `proton_usage` stores raw tool inputs plus question and answer text with no retention rule. `proton_actions.arguments` has none either.

### 2.7 Where the investigations and reviews disagreed, and what this plan picks

| Topic | Disagreement | Pick and reason |
|---|---|---|
| Panel layering | Under modal scrims vs above them | **Under** (checked: not portaled, inside `.top`). |
| Read tool count | About 34 vs 30 | **30** (registrations). |
| Write endpoint total | About 85 vs 118 | **118**, each classified in 2.1. |
| UI commands per reply | "2 commands" vs 1 | **1** (`ProtonUiRules.Pick`). It becomes a capped sequence in B. |
| Generic adapter shape | Three shapes proposed | **`ProtonServiceAction<TInput, TRoute, TRequest>`** with a typed route record, because routes carry `domainCode`, `weekStart` and board `key`. |
| "Prefill-only" tier | 14 endpoints vs "mostly never fillable" | **2 prefilled plus 22 open-only.** Pay, permissions, sign-in email, external sends, files and read markers are opened, never filled. |
| Invitations | Create T2 and Resend T1, vs T3 | **Create is prefilled, Resend is open-only.** Both email someone and carry the `email` rate policy. Create also makes an account. |
| Role change and domain move | T2 vs prefill-only | **Open-only.** Both clear permission overrides, so they belong with permission edits. |
| Invoice and statement email, scheduling | T2 vs prefill | **Open-only.** Scheduling emails automatically. |
| Bug report | T1 vs external send | **Prefill.** It emails support. |
| Message tiers | DM T1 vs T2 | **T2 two-step for both.** Messages can't be edited or deleted. |
| Messaging first step | Draft first vs a send card | **Draft first (Phase 5), send card later (Phase 7).** |
| Read markers | Notifications T1, chats T3 | **Both open-only.** Opening marks them read. Proton never marks unseen things read. |
| Company and domain rules | T1 vs T2 | **T2** (`CompanyRules` family). They affect everyone's logging and pay periods. |
| Rate limits on confirm | A limiter built from config vs a coverage rule | **Coverage rule:** no Action may wrap an endpoint with a named rate policy (other than `proton`). |
| Tool filtering | Page relevance filter vs a fixed list | **A fixed list per conversation.** Tool search does relevance, so the cache holds. |
| Gated tools on turns the user didn't type | Drop them from the list vs a fixed list | **Refuse them at run time** in `ProtonToolbox`. The list stays fixed. |
| Streaming proxy | A dedicated nginx location vs a header | **The `X-Accel-Buffering: no` header.** Caddy is checked on staging. |
| Transcript storage | New tables vs `proton_usage` | **`proton_usage` first.** |
| Time conversion | `ProtonTime` with its own logic vs `IClock` | **`IClock.FromCompanyTime`.** |
| URL state | Replace vs push | **Push on open and replace on close,** as `billingLinks.ts` does. Moves only where a deep link pays off. |
| Employee access | Optional last phase vs a prerequisite | **A firm Phase 8** after cost is measured. |
| Proton layer z-index | 105–110 | **108.** Above modals (100–102) and the lightbox (105); below the palette (110), toasts (120), tooltips (200) and menus (300). |

## 3. Principles kept and what changes

### 3.1 Kept unchanged

- **The model never gets database access or SQL, and never decides authorization.**
  - Every tool names a real endpoint through `ProtonEndpoint`.
  - Policies are evaluated at propose time and again at confirm time.
  - Services keep their own checks: domain visibility, ownership, role order.
- **Every write needs the user's explicit action.**
  - The model can never reach `/confirm`.
  - Confirmation happens only by the user's click on a card, or the user's own press of the app's Save or Send button.
  - **Proton never moves keyboard focus into a form or a composer.** So a keystroke meant for Proton can't submit anything.
- **No arbitrary URLs, DOM access or JavaScript.** Every UI effect is a typed command, checked by the server and again by the client.
- **These never reach the model:** salaries, pay, bank details and credentials. Previews go back to the model (`ProtonToolbox.ProposeAsync`), so no pay amount or bank field may ever be on a card or in stored arguments.
- **Prompts stay separate from code.** No new dependencies and no version changes. Database changes only with approval (CLAUDE.md §9).

### 3.2 Risk tiers: how each tier is confirmed

| Tier | Confirmation | Used for | Examples |
|---|---|---|---|
| **T0 Open** | None. A UI command checked by the server, by the client's `canOpen`, and by the host page's own check. | Pages, tabs, modals, drawers; browser-only preferences | Open an invoice; theme |
| **T1 Standard** | One Confirm on a card showing the exact fields, before and after values, the area (Design or Execution) and who gets notified | Reversible, internal, no money effect | Create a client; a meeting; rename a team |
| **T2 Strong** | A card with a server-computed effect, money or reach summary, then a second step. A typed phrase (record name, invoice number or amount, checked on the server) for irreversible, destructive, broadcast or money-recording actions. | Lifecycle, Access, CompanyRules, PayRules, Money, Deletes, Messaging | Complete a project; void an invoice; deactivate someone; send a message |
| **T3 Prefill and review** | Proton opens the real form and fills only allowed fields; the user presses the app's own button | Bug report, invitation | — |
| **N Open for the user** | Proton opens the real surface and fills nothing. When the surface is a one-click confirm dialog, it opens the parent page instead. | Files, external emails and scheduling, pay, permissions, role, move, sign-in email, read markers | "I've opened their page — change the role from the menu." |
| **T4 Human-only** | Proton can at most point to the page | Credentials, sessions, setup, Proton's budget | Login, change password, top-up |

- **Where the tier lives.**
  - Each tool spec declares its tier and family, and `ProtonActionService.ConfirmAsync` enforces it at confirm. The required tier is the spec's tier or the family minimum, whichever is higher. It never comes from the stored preview.
  - The preview JSON carries only the label shown on the card. The database `risk` column stays Low/Medium (decision 2).
- **Card behaviour.**
  - Confirm arms about 600 ms after the card is visible. It is never autofocused and has no Enter shortcut.
  - The card shows a tier label ("Can't be undone", "Moves money", "Sends as you") instead of the Low/Medium pill, plus the area.
  - Neither `fill_form` nor a chip can reach the typed-phrase input.
- **Who can start a gated proposal (turn origin).**
  - The server records each turn's origin: typed, choice (`Choice`), suggestion (a chip, sent as an index and rebuilt by the server), or continue.
  - On any turn the user didn't type, `ProtonToolbox` refuses `propose_*` tools in the Messaging, Deletes, Money, Access, CompanyRules and PayRules families. The tool list doesn't change.
  - A message recipient must come from the user's `offer_choices` pick, or from a `find_recipient` search whose text appears in a question the user typed in this conversation.
  - No yes/no or confirm-like chips are offered while a card is pending.
- **What the second step really is.** The T2 second step is friction against slips. The typed phrase, origin rule and recipient rule are the server-enforced parts.

### 3.3 What stays human-only or open-only, and why

- **Credentials, sessions and sign-up** (7 Auth endpoints, setup, invitation validate and accept). They take passwords or secret tokens and write the HttpOnly refresh cookie, which the confirm request can't carry.
- **Bank account, bank name, IBAN and SWIFT.** They are never shown to Proton, and a model-typed IBAN is a payment-redirect fraud vector.
- **Pay amounts** (`Employees.ChangePay`, invitation pay). Previews go back to the model.
- **Permission edits, someone else's sign-in email, role changes and domain moves.** These are the highest-value targets for prompt injection and account takeover. Role changes and moves also clear permission overrides, which can hand back a revoked grant. Proton opens them and never fills them.
- **The Proton budget top-up.** An assistant raising its own spending cap is a conflict of interest.
- **External email in Stage 5:** invoice and statement email, scheduling, the auto-invoice plan and invitation resend. Proton opens these. Two are prefilled: the bug report and the invitation, and the user sends them.

### 3.4 How existing authorization, validation and services are reused

- **Endpoint.** Each action names its endpoint with `ProtonEndpoint.OfWrite<Controller>(action)`. The policies are evaluated at propose and again at confirm.
- **New endpoint modes.**
  - **Read-only POST:** an allow-list of 3 endpoints.
  - **Rule-based `[Authorize]`:** an allow-list for messaging, notifications, own profile, bug reports, teams, board layouts and the settings GETs.
    - It evaluates `AuthorizationPolicies.Default` (signed in, password change done) plus `proton:use`.
    - A test asserts that every allowed service takes the actor from `ICurrentUser`, never from input.
- **Rate-limited endpoints.** `ProtonEndpoint` records the `[EnableRateLimiting]` policy name. The coverage test fails if an Action wraps an endpoint whose policy is not `proton`. Every such endpoint is T3, N or T4 in this plan.
- **Allowed fields.**
  - The model's input record (`TInput`) **is** the allow-list. Server-controlled or money fields (`InitialPayment`, `Moves`/`Deletes`, `Pay`) don't exist in it. `Build` maps it onto the real request DTO (CLAUDE.md §12.4).
  - `Load` returns a narrow projection declared in the Proton namespace, with no pay or bank properties, so the excluded-fields walk covers it.
  - `Before` always has the `TRequest` shape, as `UpdateProjectArgs` does today.
- **Full-replace PUTs** (client contacts, labour days, settings, profile, draft invoices) merge into the current record, read through the GET service. The `Before` snapshot is checked on confirm.
- **Validation.** The endpoint's own FluentValidation validator, through `ProtonToolContext.ValidateAsync`, at propose and at confirm.
- **Business rules.** The existing `Business/*Rules` run at propose time for early feedback. The service stays the authority.
- **Execution.** Exactly the service call the controller makes, with the known exceptions mirrored:
  - `IssueInvoiceRequest` defaults;
  - overhead cancel returning 204.

  Calling controllers by reflection or over HTTP loopback is forbidden.
- **Side effects while proposing.** Decorators on `IEmailSender`, `IFileStorage`, `INotificationSender` and the live-signal publisher refuse to run while `ProtonReadOnly.IsActive`.
- **External sends are tested at run time.** The integration conformance suite uses a recording fake email sender. It fails if any Action's confirm sends email.
- **Figures.** Money, effect and reach figures on cards are computed on the server from the same repositories. The model never calculates them.
- **Record text is data.** Comment and message text comes from the user's own words, never from text inside records.

### 3.5 No DOM control

- **Forms are filled through per-form adapters** that call the form's own setters. Fields are controlled components with typed values (`AmountField`, `DateInput`, `TimeField`, `Select`…).
  - Proton fills only fields that are visible, expanding their section first.
  - It fills empty fields only. A replacement shows before and after values per field, and the user accepts each one.
  - It never submits.
- **Opening without taking focus.** A surface opened by Proton opens with `openedByProton`. `Modal` skips `dialog.focus()` and forms skip `autoFocus`.
- **The Enter guard.** In a form Proton filled, and in a composer holding a Proton draft, Enter does nothing until the user clicks or types inside it. The first submit needs the app's own Save or Send button.
- **Never driven.** Menus and popovers, and the one-click confirm dialogs of T2 and T4 actions (tagged `confirmsAction` in the surface catalogue).
- **Immediate-save controls are never filled.** They get a proposal instead.

### 3.6 Feels human without pretending to be human

- **Voice.** Proton gets a warm first-person voice, small talk, follow-through after a confirm, and memory that the user controls.
- **Honesty about being an AI.** Proton:
  - says it is an AI whenever asked, and never denies it;
  - never claims human experiences;
  - corrects a user who seems to think it is a person;
  - never writes a message that pretends to come from someone other than the user (decision 21).

## 4. Architecture

### 4.1 Backend

**Action engine** (new `api/Proton/Actions/ProtonServiceAction.cs`, on top of the existing `ProtonActionTool`):

```text
model ─ propose_x(TInput) ─▶ [ProtonReadOnly: DB writes, email, files, notifications, live signals refused]
          Origin           → gated family on a turn the user didn't type → refused
          Load(route)      → narrow Proton-namespace projection via the GET service (no pay, no bank)
          Build(input,cur) → TRequest  (merge for full-replace PUTs)
          Validate(TRequest) → the endpoint's FluentValidation validator
          Check(cur, req)  → existing Business/*Rules (early refusal)
          Preview(cur,req) → lines, area, tier label, Phrase, Money/Reach summary (server-computed)
        ─▶ proton_actions row: ProtonCall<TRoute,TRequest>(Route, Request, Before, AccessFingerprint)
user  ─ Confirm(Typed?) ─▶ claim once → global and family switches → hourly cap → access fingerprint unchanged
          → endpoint policies → tier from the spec (≥ family minimum) → typed phrase
          → validate again → stale check (Before) → Execute: the controller's one service call → ProtonActionDone
```

- **Spec shape.** Each endpoint becomes a short spec: input record, route record, endpoint, tier, family, Load, Build, Check, Preview, Execute.
  - `ProtonIdAction<TInput,TRoute>` covers the id-only endpoints.
  - Inverse pairs stay merged (hold/resume, done/undone, archive/restore, cancel/resume).
  - That gives about 55–60 action tools.
- **Coverage registry** (new `api/Proton/Actions/ProtonCoverage.cs`).
  - Every write endpoint maps to one of `Action(tool)`, `Prefill(form)`, `Open(surface)`, `ReadOnly(usedBy)` or `Never(reason)`, with an `ExternalSend` flag.
  - A reflection test fails on any unclassified endpoint. Today that is 118 entries, 8 of them `Action`.
  - It replaces the hard-coded action list in `ProtonToolCatalogTests`.
- **Sensitivity tags.** `PermissionCatalog.Definition` gains a C#-only `Sensitivity`: None, Money, Pay, Access, CompanyRules, Credential. No schema change.
  - **Money:** `financial:view`, `invoice:*`, `payment:*`, `expense:manage`, `project:pricing_manage`, `billing_settings:manage`, `exchange_rate:manage`, `overhead:manage`, the overhead-category permission, `material:*`, `statement:*`, `labour_report:*`.
  - **Pay:** `employee:pay_manage`, `salary:view`, `hours:excuse`, `schedule:manage`, `working_time:manage`.
  - **Access:** `employee:manage`, `employee:invite*`, `employee:change_email`, `session:manage`, `permission:manage`.
  - **CompanyRules:** `company_settings:manage`, `domain_settings:manage`.
  - The catalog test asserts that a tagged policy, or a `Delete` endpoint without a named restore, makes an Action T2 or higher in the matching family.
- **Families and kill switches.**
  - Families: `Core`, `Lifecycle`, `Deletes`, `Access`, `CompanyRules`, `PayRules`, `Money`, `Messaging`.
  - `ProtonOptions.Actions` holds:
    - `Enabled`, a global switch that also covers Core;
    - one flag per family, non-Core off by default;
    - `MaxPendingActions = 3`;
    - `MaxConfirmedActionsPerHour` per user;
    - `MaxMessagesPerDay = 20`.

    The caps are counted from `proton_actions` through the existing `(UserId, CreatedAt)` index.
  - When a family is off, its tools stay in the list (so the cache holds) and refuse with a clear message. Kept in the list but refused, rather than hidden, so the cache holds.
- **Confirmation.**
  - `ProtonPreview` gains `Confirm` (Standard, TwoStep or Typed), `Phrase`, `Money[]` (label, before, after, currency), `Reach` (who is notified or affected), `Irreversible` and `Area`.
  - `DecideProtonActionRequest` gains `Typed?` (up to 200 characters), checked in `ProtonActionService.ConfirmAsync`.
- **Access changes.**
  - A confirm whose access fingerprint differs from the one stored in `ProtonCall` marks the action Superseded.
  - So does reopening a conversation with a changed fingerprint, for that conversation's pending actions.
  - This needs no schema change: the fingerprint lives inside the `arguments` JSON.
- **Several pending actions.**
  - `ProposeAsync` supersedes only a proposal with the same tool and target, and caps the conversation at 3 pending.
  - The reply returns `pendingActions[]`. `pendingAction` stays for one release.
  - Batches, Confirm all, auto-continue and undo are deferred (decision 6).
- **Time.**
  - `IClock` gains `FromCompanyTime(DateOnly day, int minutes)` using `TimeZoneInfo.ConvertTimeToUtc`, with unit tests for daylight-saving days and midnight. Beirut's clocks change at midnight, so `CompanyDayStartUtc(day).AddMinutes(m)` would be wrong on those days.
  - The model passes the company-local date and start and end minutes, as the UI's `TimeField` does.
- **Tool discovery.**
  - The tool list is fixed for a conversation, filtered by permissions only. The access fingerprint already pins it.
  - A core set of about 12–15 tools is never deferred: `find`, `open_record`, `open_page`, `open_surface`, `fill_form`, `offer_choices`, `suggest_replies`, `get_project`, `list_projects`, `review_project`, `get_attention`.
  - The rest go out with `defer_loading` and are found through the BM25 tool search tool (non-beta in the C# SDK).
  - There is no page relevance filter.
  - Prerequisite: `Echo` passes every block type back unchanged, including `server_tool_use` and `tool_search_tool_result`.
- **UI contract on the server.** `ProtonUiRules.Pick` returns an ordered, capped sequence: at most 1 navigation, then 1 surface, then 1 fill. A refresh is dropped when the reply navigates.
- **Streaming.**
  - A new `POST /api/proton/messages/stream` with the same DTO, `HasPermission(proton:use)` and the `proton` rate policy.
  - It returns `text/event-stream` with these events:
    - `status`: a fixed server phrase per tool, never raw data;
    - `delta`: answer text;
    - `done`: the unchanged `ProtonReplyResponse`;
    - `error`;
    - a `: ping` comment every 10 s.
  - The response sets `X-Accel-Buffering: no`.
  - All pre-checks run before the first byte, so they keep their normal status codes through `GlobalExceptionHandler`.
  - After the first byte, one SSE writer formats the `error` event from a shared exception-to-error mapping (`ApiErrorMapping.Of`, extracted from `GlobalExceptionHandler`) and `ApiErrorWriter.Build`. That gives the same `{code, message, traceId}` with no controller-local format.
  - On an abort or error, proposals created during that turn are marked Superseded.
  - New `StreamQuestionTimeoutSeconds` (120). The JSON endpoint keeps 55 s.
  - The agent uses `client.Messages.CreateStreaming` per round trip (not yet compiled against). The JSON endpoint stays.
- **Memory.**
  - **Phase A (no database change).** When a limit would end a chat, the server rolls over into a new conversation, seeded with an app-written recap: focus, known refs and the last 2–3 question and answer texts.
  - **Phase B (no new tables).** After a restart, the recap is rebuilt from `proton_usage` (question, answer, conversation id, through the `(UserId, OccurredAt)` index). The access-fingerprint check runs on resume. Columns for focus or refs come only if needed, with approval.
  - **Preferences (approval).** A `proton_memories` table, saved only from an explicit user control. Memories are injected into the context note as user-preference data, never into the system prompt and never as instructions.
- **What Proton did.**
  - `GET /api/proton/actions` returns the caller's own proposals and outcomes. It is used by the panel and by restored cards.
  - An admin list for `proton_budget:manage` holders reads from `proton_actions`, with no schema change.

### 4.2 Frontend

- **Proton layer.**
  - `protonStore.ts` is a module store built with `useSyncExternalStore`, like `web/src/domains/design/work/logWorkStore.ts`. It holds the existing `ProtonChat` shape from `protonChat.ts` and is cleared by `onSessionReset` and `sessionExpired`.
  - `useProtonConversation.ts` moves the ask logic out of the panel, which fixes the lost-reply bug.
  - `ProtonHost.tsx` is mounted once in `web/src/app/live/LiveRoute.tsx`, which covers the launcher and `AppShell` but never `/change-password` or the auth pages. It mounts only for users with `proton:use`, and only when Proton is configured. It portals a `.proton-dock` (`data-proton-layer`, z 108) to `body`.
  - `ProtonButton.tsx` is removed from `TopBar.tsx`.
- **Keeping the transcript.**
  - The visible transcript goes to `sessionStorage` under a key that includes the user id. The id is checked on restore.
  - The transcript is cleared on session reset and when a token refresh fails.
  - Restored cards show closed until `GET /api/proton/actions` returns their current status.
- **UI command contract.** Three new types:
  - `OpenSurface {surface, ids, params}`
  - `FillForm {form, values}`
  - `SetPreference {theme | sounds}`

  The executor in `uiCommands.ts` runs the ordered sequence: navigate → wait until the surface registers (5 s) → open → fill.
  - It never moves focus.
  - It reports honestly when a surface didn't open, both as a quiet system line and in the next page context.
- **Surface catalogue.**
  - `web/src/domains/shared/proton/surfaces.ts` maps each surface id to a route builder, a zod schema for its params, an open check and a `confirmsAction` tag. Tagged surfaces are never opened. The open check is the same exported pure function the host page uses.
  - Pages register a surface's opener with `useProtonSurface(surfaceId, open)`. Moving a modal into the URL happens only where a deep link pays off on its own (B).
  - `Modal` and `Drawer` get an optional `surfaceId` prop. It feeds "what's open" to the page context and to fill readiness.
  - Backend mirror: `api/Proton/Ui/ProtonSurfaces.cs`. A contract test keeps the server and client registries in step, through exhaustive `Record<>` maps over the generated enums.
- **App-wide modals.** `web/src/app/shell/modalStore.ts` plus `ShellModalHost`, on the `logWorkStore` pattern. Report bug gets one host instead of the two local copies, and new meeting can open from anywhere.
- **Form registry.** `web/src/shared/hooks/useProtonForm.ts`, beside `useFormErrors`.
  - A mounted form registers its form id, a zod schema (shape only, `.strict()`), its apply function and the record ids it is bound to. Limits come from `limits.ts`, and the server stays the authority.
  - A fill:
    - is bound to the record id;
    - expires after a timeout or a navigation;
    - is applied to visible, empty fields only;
    - is marked (a `filled` prop on `Field`, plus a "Filled by Proton — check and save" banner with undo per field);
    - is followed by `errors.tried()`;
    - is never submitted.
- **Layering, focus and keyboard.**
  - `Modal`'s document-level Escape ignores events from `[data-proton-layer]`, and the composer stops Escape from propagating. No mousedown exemption is needed: the dock is a portaled sibling above the scrim.
  - `openedByProton` (from the executor) makes `Modal` skip `dialog.focus()` and forms skip `autoFocus`. The Enter guard is described in 3.5.
  - Screen readers: decision 35.
- **Streaming client.** In `protonApi.ts`:
  - First try axios's fetch adapter (`adapter: "fetch"`, `responseType: "stream"`, `signal`), so the token and 401-refresh interceptors are reused.
  - If that doesn't work, use `fetch` plus the exported `refreshSession` and `toApiError`.
  - Only the SSE line parser is new. An `AbortController` backs the Stop button.

## 5. Workstreams

### A. Actions for everything

- **Goal.** All 76 T1 and T2 endpoints become confirmed actions through one engine, each family switched on only once approved. The 2 T3 endpoints get prefill, and the 22 N endpoints get surfaces (B).
- **Changes.**
  - **A1. Engine.**
    - New `api/Proton/Actions/ProtonServiceAction.cs` and `ProtonCoverage.cs`; `Sensitivity` on `PermissionCatalog.Definition`.
    - `api/Proton/Tools/ProtonEndpoint.cs`: rate-policy capture, `OfReadOnlyPost`, and `OfRuleBased` with its allow-list and default policy.
    - `api/Proton/Tools/ProtonToolbox.cs`: families, origin check, supersede by tool and target, pending cap.
    - `api/Proton/Actions/ProtonActionService.cs`: switches, hourly cap, fingerprint, tier from the spec, typed phrase, central stale check.
    - `ProtonPreview`, `DecideProtonActionRequest` and its validator.
    - `api/Configuration/Options/ProtonOptions.cs`: the action options listed in 4.1.
    - `IClock.FromCompanyTime`.
  - **A2. Move the existing 6 onto the engine.** `ProjectActions.cs` and `RequirementActions.cs`, with no behaviour change, in their own PR.
  - **A3. Read tools for targets** (new files in `api/Proton/Tools/`):
    - `get_invoice`, `list_expenses`, `list_materials`, `get_labour_week`, `list_statements`, `get_work_entry` (with its comments);
    - `list_teams`, `get_company_settings`, `get_domain_settings`, `get_billing_settings` (no bank fields), `list_exchange_rates`, `list_overhead`, `list_schedules`, `get_working_time`;
    - `list_my_work`, with employee access (Phase 8).

    `ProtonRefType` (`api/Proton/Tools/ProtonTool.cs`) gains Team, Invoice, Payment, Meeting, WorkEntry, Statement. They are stored as free strings in `target_type`.
  - **A4. T1 specs by area,** one PR per area:
    - `ClientActions.cs`: create, update with contact merge, archive/restore. The duplicate warning comes from `ClientService.CheckAsync`.
    - `MeetingActions.cs`: through `IClock.FromCompanyTime`.
    - `TeamActions.cs`: create, rename.
    - `SelfActions.cs`: own profile, board layouts.
    - `WorkEntryActions.cs`: a plain comment now. Your own entries (no Moves or Deletes; delete and restore) in Phase 8.
  - **A5. Several pending cards.** `pendingActions[]`, the cap of 3, supersede by tool and target. Batches, Confirm all, auto-continue and undo are deferred (decision 6).
  - **A6. Strong-tier mechanism.** Typed phrase and two-step on the server and in `ActionCard`, plus the arming delay and tier labels.
  - **A7. T2 without money.**
    - *Lifecycle:* project complete, archive and reopen. Widen `ProtonStatusChange`; the card shows that revenue locks and that N requirements get ticked.
    - *Access:* employee details update, deactivate, reactivate (`payGap` chosen only through `offer_choices`, and the card says when the person will show as invited), end sessions, restore sign-in.
    - *Deletes:* team delete.
    - *CompanyRules:* company settings and domain settings. The card carries a server-computed reach summary ("affects 23 people in Design"). `HoldLogging`, currency and week-start changes need a typed phrase.
  - **A8. Money family (decision 4).** One PR per sub-family:
    - invoices: draft create and update, issue, void, discard, record delivery, unschedule;
    - payments: record, apply, void;
    - expenses;
    - project billing and pricing, and material client price;
    - materials (cost entries);
    - labour weeks and defaults (decision 13);
    - statements: create (its card is `StatementService.PreviewAsync`) and record delivery;
    - billing settings (non-bank fields: bank fields are re-read on confirm and never stored in `arguments`);
    - exchange rates;
    - overhead (all 9).
  - **A9. Deletes family (decision 5) and PayRules family (decision 8).** PayRules covers working time, hour-excuse add and schedules, on cards that describe the effect with no amounts.
  - **A10. What Proton did.** `GET /api/proton/actions` and the admin list (decision 37).
- **Frontend.**
  - `web/src/domains/shared/proton/ActionCard.tsx` gets these variants: standard, two-step, typed phrase, money summary, message. It shows the tier label and the area, and arms Confirm after a delay.
  - `ProtonPanel` (later the bubble) shows several cards in order.
- **Reuse.**
  - The existing validators and policies.
  - `Business/*Rules`: `ProjectStatusRules.Plan`, `RequirementRules.CheckEditable`, `SiteRules.CheckOpen`, `MaterialRules.CheckNotStated`, `InvoiceRules.*`, `ExchangeRateRules.Check`, `WorkWeek.CheckEffectiveFrom`, `ExcuseRules.Check`, `EmploymentRules.AsksAboutGap`.
  - The GET services, `WorkingTimeService.Describe`, `StatementService.PreviewAsync`, `OverheadRules.CurrencyChangeSplits`.
- **Tests.**
  - **Unit, per spec:**
    - the input-to-request mapping sets only allowed fields;
    - preview lines;
    - stale detection;
    - Check refusals;
    - fixtures holding distinctive amounts and IBANs, asserting that none of those values appear in the preview or the stored arguments.
  - **`ProtonCoverageTests`:**
    - every write endpoint is classified;
    - every `Action(tool)` points at the endpoint whose service it calls (Invoice, Payment, Overhead, Company, WorkingTime and DomainSettings services don't re-check permissions themselves);
    - no Action wraps an endpoint with a named rate policy;
    - tagged policies imply T2 or higher;
    - the `ExternalSend` flag is set on every endpoint with an `email` or `bug_report` policy.
  - **Rewritten `ProtonToolCatalogTests`:** the excluded-fields walk covers `Proton.Tools`, `Proton.Actions` and `Proton.Ui`, and `DailyPay` is explicitly allowed (decision 13).
  - **One parameterised integration conformance theory**, driven by the registry, run for every Action:
    - authorized → done;
    - unauthorized → refused at propose and at confirm;
    - another domain → 404;
    - at most once;
    - family off or global off → refused;
    - a changed fingerprint → superseded;
    - no email sent unless the action is flagged;
    - side effects refused during propose.
  - **Hand-written integration tests** only for special cases: typed phrase, stale merge, `payGap`, reach summary, hourly cap.
- **Approvals:** decisions 1–9, 12, 13, 29, 32–34, 37.

### B. Universal UI reach (every page, tab, modal)

- **Goal.** Every page, tab, drawer and modal a user can open, Proton can open by meaning, under the same permission checks. The exception is one-click confirm dialogs of T2 and T4 actions.
- **Changes.**
  - **Contract.**
    - `api/Proton/Ui/ProtonUiCommand.cs`: `OpenSurface` and `SetPreference`, plus `ProtonUiRules.Pick` returning the capped sequence.
    - `ProtonPage` additions:
      - now: Settings, Notifications, Messages, Help, Schedules, CompanyProfile, Launcher, and navigation-only Salaries, ProjectPay and ProtonBudget;
      - with employee access: MyWork, MyPay, Profile (decision 10).
    - New `api/Proton/Ui/ProtonSurfaces.cs`, and `open_surface` in `api/Proton/Tools/UiTools.cs`.
    - Regenerate `web/src/api/schema.d.ts` (`npm run api:types`).
  - **Executor and context.** New `surfaces.ts` (with `confirmsAction`), sequencing in `uiCommands.ts`, and `surface` added to `useProtonPageContext.ts`.
  - **Opener registration.** `useProtonSurface` for local-state surfaces:
    - contributor drawer, day story, timeline moment;
    - add materials, labour history;
    - overhead categories;
    - the schedule and salary drawers (open only; Proton never reads figures);
    - notifications filter, reports filters.
  - **URL state, only where a deep link pays off on its own.** Push on open and replace on close, as `billingLinks.ts` does. `web/src/shared/hooks/useUrlParam.ts` takes ids and enums only and checks every key against one param-key registry:

    | Surface | Where | URL key |
    |---|---|---|
    | Edit project | project page | `?details=1` (not `?edit=`) |
    | Edit client | client page | `?details=1` (the page hosts `BillingModalHost`) |
    | Work entry | project tabs | `?entry=` (as on the profile page) |
    | Edit requirement | requirements | `?requirement=` |
    | New meeting | meetings | `?new=1&project=` |
    | Overhead line | overhead | `?line=` |
    | Labour week | labour | `?week=` |
    | Reports tab and period | reports | `?tab=`, `?period=` |
    | List filters and periods | projects, clients, people, activity, project pay, company period | ids and enums only |

  - **Overdue bug** (own small PR, Phase 0). The Billing tab reads the existing `overdue=1`. Neither the palette link nor the help content changes.
  - **Kept out of reach (`confirmsAction`).** For these, Proton proposes the T2 card or opens the parent page:
    - the PersonActions dialogs (deactivate, reactivate, end sessions, restore sign-in, role, move);
    - `CompleteProjectModal`;
    - invoice void and discard, payment void;
    - delete confirms;
    - top-up.
  - **Open-only forms** (N): `PermissionsModal`, change pay, sign-in email, `SendInvoiceModal`, `AutoInvoiceModal`, statement email, upload pickers. Those that email someone open only on a typed turn.
  - **Preferences.** `set_preference` for theme, and for sounds only when the user asked in a typed turn. Turning sounds off posts a visible "Message sounds are off" line.
  - **App-wide modals.** `modalStore.ts` and `ShellModalHost` for report bug and new meeting.
- **Reuse.** `routeTable` `canOpen`, `companyRoutes.access`, the `searchPlaces` synonyms, and the existing URL-driven hosts (`BillingModalHost`, `StatementHost`, `?new=1`, `?invite=1`).
- **Tests.**
  - **Vitest:**
    - each surface opens only when the host would show it;
    - denied surfaces report "not opened";
    - a `confirmsAction` surface is never reachable;
    - sequence then fill;
    - the param-key registry fails on a collision.
  - **Registry sync** between the server and client enums.
  - **Backend:** the surface resolver refuses invisible ids; `Pick` caps and orders the sequence.
- **Approvals:** decisions 10, 11, 28.

### C. Form filling (prefill and review)

- **Goal.** Proton fills a real form so the user can review it and press the form's own Save.
- **Changes.**
  - **Backend.** A `fill_form {form, values}` tool parses through `ProtonForms.Parse(form, json)` into typed records (`api/Proton/Ui/ProtonForms.cs`).
    - Field-level errors go back to the model so it can correct itself.
    - Excluded fields can't be expressed at all.
    - The shape of `values` follows decision 11b.
  - **Client.** `useProtonForm.ts` and a short adapter per form. Pilots in this order:
    1. `ClientModal` (it already takes a `prefillName` prop);
    2. `MeetingModal`;
    3. `CreateProjectModal`, without money fields (the "more details" section expands first);
    4. `EditProjectModal` (`?details=1`);
    5. `ReqForm`;
    6. `ReportBugModal` (T3);
    7. `InviteModal`: email, position and team only, and the email only when it is in the user's typed text (T3);
    8. company details;
    9. `ReportModal` options;
    10. `OverheadModal` name and category;
    11. `MaterialSheet` cost-only rows, replacing its `window.confirm` with a `Modal`. Money values and amounts only once decision 4 is approved.

    Two more come with other workstreams: the message `Composer` draft (D2) and log work (Phase 8, employees).
  - **Never fillable.**
    - Passwords and email changes, `PermissionsModal`, pay fields, bank fields, the top-up.
    - `SendInvoiceModal`, `AutoInvoiceModal`, the payment and invoice void and discard dialogs, the PersonActions dialogs.
    - Statement client prices (they save as you type) and all immediate-save settings controls.
  - **Page context.** `forms: [{formId, fields, filled/empty}]`, field names only. Sending values would need a later decision (11d).
- **Reuse.** The `logWorkStore` pattern, controlled field components, `useFormErrors`, zod 4 (already a dependency) for shape, and `limits.ts`.
- **Tests (Vitest, per adapter):**
  - fills only declared, visible fields;
  - refuses a mismatched record id or an expired fill;
  - doesn't overwrite typed values without a per-field accept;
  - marks filled fields;
  - never submits;
  - Enter in the Proton composer while a surface opens never submits;
  - Enter in a filled form does nothing until the user interacts.

  **Backend:** excluded fields are refused, and a payload can't express them.
- **Approvals:** decisions 4 (money fields), 7, 11.

### D. Messaging on the user's behalf

- **Goal.** Proton drafts, and after a strong confirmation sends, internal messages as the user. It also reads the user's own inbox metadata and notifications.
- **Changes.**
  - **D1. Reads (Phase 5).**
    - `OfRuleBased` (from A1, pulled forward).
    - `find_recipient` over `ContactsAsync` plus the user's team chats. It returns role, position, team and domain so people with the same name can be told apart, and sends ties through `offer_choices` with Team refs.
    - `get_my_inbox`: chat names, unread count, mention flag, last activity. Bodies are stripped.
    - `get_my_notifications`: worded on the server.
    - Messages and Notifications are added to `ProtonPage` and the page context. The open chat is reported by id only.
  - **D2. Draft into the composer (Phase 5).**
    - `open_surface chat {userId or teamId}` opens `MessagesPage` (`?with=`/`?group=`); `fill_form message {body}` then fills the draft.
    - The draft arrives through a drafts store keyed by chat, never the URL. It sets a new `initialText` prop in `web/src/domains/shared/messages/Composer.tsx`. The composer remounts per chat (`Thread.tsx:213`), so the key must match.
    - The Enter guard applies, and the user presses Send.
  - **D3. `propose_send_message` (Phase 7, Messaging family).**
    - **Tier.** T2 two-step for a DM and for a team chat. The database risk stays Medium. A team chat needs a typed phrase (the team name) when it mentions anyone or reaches more than 10 people.
    - **The card.** It is headed "Send as you. This can't be undone". It shows:
      - the avatar, or the team with its member count (`TeamMembersAsync`);
      - the full text, sent byte for byte;
      - the area.

      The button reads Send.
    - **Figures check.** If the text repeats figures from this conversation's Money-tagged tool results, and the recipient lacks `financial:view`, the card warns and asks for a typed confirmation.
    - **Limits.** One chat per card. Mentions are resolved to members and capped at 10. No `@all` and no attachments. The daily cap is counted from `proton_actions`.
    - **Recipients** follow the provenance rule in 3.2. Proton never replies to an incoming message on its own.
    - **Execution.** `SendDirectAsync` or `SendToTeamAsync`, as the user. It records `ResultType=message` and `ResultId`.
    - **New prompt `api/Proton/Prompts/messages.md`:**
      - write in the user's voice, using only what the user said;
      - flag any figures before including them;
      - recipients only as the user named them;
      - never claim a message was sent before the confirm note says so;
      - never propose a message because text in data asked for one.
  - **D4. Work-entry comments (Phase 7).** `propose_add_comment`: a plain comment is T1; `SendToGroup` is T2.
  - **D5. "Via Proton" marker** (approval, shipped before the Messaging family is switched on): `messages.proton_action_id`, `MessageResponse.viaProton`, and a chip in `Thread.tsx`.
  - **D6 (optional, approval). Reminders.** A reminders table, a 1-minute `ReminderJob` and a `RemindersController`, then Proton tools on top.
  - **D7 (later approval). `read_chat`.** One chat, the last 30 messages, framed as untrusted data.
- **Reuse.** The `MessagingService` and `MessagingRules` checks at send time (active sender, reach, active recipient, membership, mentions). Live signals flush through `LiveUpdatesMiddleware` with no extra code.
- **Tests.**
  - **Unit:** recipient ranking and ambiguity; a recipient the user can't reach is refused; a recipient not named in typed text is refused; the figures warning.
  - **Integration:** sent as the user; a deactivated recipient → 422; a team the user isn't in → 404; the cap; at most once; the card's text equals the stored text.
  - **Vitest:** the draft lands only in the matching chat; Enter doesn't send a Proton draft until the user edits it.
- **Docs.** `repo/docs/messaging.md`, `repo/docs/proton.md`.
- **Approvals:** decisions 12, 14–16, 27.

### E. Human conversation

- **Goal.** Proton sounds like a capable colleague: warm, first person, remembering what the user asked it to, helpful without nagging, and honest that it is an AI.
- **Changes.**
  - **E1. Persona and naming.**
    - New `api/Proton/Prompts/persona.md`, loaded first in `ProtonPrompts.cs`. Its rules:
      - first person, contractions, the occasional first name, one clarifying question at a time;
      - small talk answered briefly, then back to work;
      - replies in the user's language, with record names kept as they are;
      - the identity rules in 3.6.
    - Rewrite the third-person lines, and turn `analysis.md`'s mandated phrases into examples to vary.
    - Replace "Hourline" in the 10 prompt mentions and in `ActionCard.tsx:64` with the name from the existing `Email:ProductName`. That includes a name-only edit to `data.md`. Every other word of `data.md` and the grounding rules stays as it is.
  - **E2. Copy and ended conversations.**
    - Rewrite server and client strings in the first person. Error codes don't change.
    - Handle `proton_conversation_ended` with a seamless rollover (4.1 Memory, Phase A).
    - The transcript goes to `sessionStorage` under the rules in 4.2.
    - Log refusal categories.
  - **E3. Greeting and suggestions.**
    - A greeting from the client using the first name and the time of day, with no model call.
    - New `GET /api/proton/briefing`. It runs `get_attention`'s code path without a model call. The client fetches it at most every 5 minutes (React Query `staleTime`), and there is no server cache unless it is measured slow (decision 18).
    - A `suggest_replies` UI tool offers up to 3 chips. A chip is sent as `Suggestion {index}` and rebuilt on the server, with origin "suggestion". It never prefills the composer with model text.
    - Typed quick replies (period words, domain, yes/no) extend `ProtonChoice` and are checked on the server. No yes/no chips while a card is pending.
    - Static, permission-aware starter chips per page.
  - **E4. Streaming** (4.1).
    - Status lines come from the server's tool-to-phrase map.
    - `display: "updates"` only if the prototype in decision 17e passes.
    - A Stop button.
    - `StreamQuestionTimeoutSeconds`; `ModelTimeoutSeconds` and `MaxRetries` lined up with it.
  - **E5. Multi-step plans.** Prompt support for "plan, say the steps, then propose one card at a time", within the cap of 3 pending.
  - **E6. Memory.**
    - **Phase A:** rollover and `sessionStorage`.
    - **Phase B:** resume from `proton_usage` (no new tables).
    - **Preferences** (approval): `proton_memories`, 300 characters at most and 20 per user.
      - Saved only from a "Remember this" control on the user's own message, or from a compact card, and only from the user's own text.
      - Refused if a memory mentions tools, actions, people or recipients, or holds secrets, pay or credentials.
      - `GET` and `DELETE /api/proton/memories`.
- **Reuse.** `ConversationFocus` and known refs, `get_attention`, the `offer_choices` validation path, `ProtonPrompts.ChatVersion` (prompt versions are recorded automatically), `Email:ProductName`.
- **Tests.**
  - **Evals (G5):**
    - greeting, thanks, off-topic;
    - Arabic and French;
    - ambiguous names;
    - "are you a person?";
    - injection through record text, and chained injection (record text → chip or continue → a send, delete or money proposal);
    - recall from memory.
  - **Integration:**
    - a rollover keeps focus;
    - a changed access fingerprint ends the chat with no recap;
    - memories never carry permission;
    - a suggestion turn can't start a gated proposal.
- **Approvals:** decisions 17–21, 24, 25, 33.

### F. The blob

- **Goal.** A soft, fluffy, draggable AI blob on every signed-in screen of users who have Proton. It inflates when it talks and shows its mood. No new dependencies.
- **Changes** (client, about seven commits):
  1. **State.**
     - `protonStore.ts`, `useProtonConversation.ts`, and `ProtonHost.tsx` in `LiveRoute`, gated by `proton:use`.
     - Remove `ProtonButton`.
     - This fixes the lost reply and the launcher bug, with no visual change.
  2. **Figure and tokens.**
     - `blob/BlobFigure.tsx` and `blob/blobShape.ts`: a layered SVG (glow, body, fur, sheen, eyes) with radii driven by Catmull-Rom curves.
     - Only a static glow layer is blurred, and it is animated through `transform` and `opacity`. Geometry is animated only on unfiltered layers. No animated turbulence.
     - `blob/blobMotion.ts` and `useBlobMotion.ts`: a requestAnimationFrame loop that runs only in active moods. It is capped at 30 fps while talking, pauses when `document.hidden`, and never runs under reduced motion or "Calm animations".
     - Idle breathing is a CSS scale, and it pauses under "Calm animations".
     - `web/src/shared/hooks/useReducedMotion.ts`.
     - `--proton-*` tokens in light and dark (all three theme blocks), plus keyframes, reduced-motion and forced-colors rules, in the Proton block of `web/src/styles/client.css`.
  3. **Moods and talking.**
     - `blob/protonMood.ts`: a pure `moodOf` over idle, listening, thinking, working, talking, proposing, happy, error and asleep, with a priority order. The mood is reflected in the accessible name ("Proton, thinking") as well as the shape.
     - `blob/talkPlan.ts`: duration `clamp(chars×28, 900, 4200)` ms, with the level dropping on spaces and punctuation.
     - A CSS mask-wipe reveal keeps the full text in the DOM. It is instant under reduced motion and can be skipped with a click.
     - Typing dots, "Still looking…" after 8 s, an unseen badge and a peek bubble.
     - Once E4 lands, inflation follows the smoothed rate of `delta` characters.
  4. **Moving it.**
     - `blob/useBlobDrag.ts`: Pointer Events, a 6/8 px threshold, snap to the nearest edge.
     - `blob/blobPosition.ts`: saves `{side, y-fraction}` in `localStorage` with try/catch, and recalculates on resize and visual-viewport changes.
     - It keeps clear of the focused element (on `focusin`, it moves aside if their rects intersect), of `.scrim .modal-foot` and of `.msg-composer-wrap`.
     - Arrow keys, Home and End move it, and a "Move to left/right" button gives a single-pointer alternative.
  5. **Panel.**
     - A speech-bubble panel anchored to the blob on desktop (`min(400px, 100vw−32px)`), and a bottom sheet on phones (88 dvh, sized with the VisualViewport API, collapsing to a peek after Proton navigates).
     - Split into `ProtonThread.tsx` and `ProtonComposer.tsx`. The dock is portaled at z 108.
     - Escape closes only Proton. Focus goes to the composer on open and back to the blob on close.
     - `dir="auto"` on message text; screen-reader announcements only on `done`.
     - Behaviour over an open modal follows decision 35.
  6. **Palette and docs.** An "Ask Proton" action in `web/src/app/shell/searchPlaces.ts` and `web/src/app/shell/CommandPalette.tsx` through `ctx.run.openProton`. Update `repo/docs/proton.md`.
  7. **Modal changes.** The Escape exemption, `openedByProton` focus handling, and `surfaceId`.
- **Reuse.** The `logWorkStore`, `theme.ts`, `AppShell` `localStorage` and `DragDay` pointer-capture patterns, `useEffectEvent`, and the global reduced-motion rule.
- **Tests.**
  - **Pure modules:** shape, springs, `moodOf`, `talkPlan`, position and keep-clear.
  - **Integration with MSW:**
    - `data-mood` transitions and the accessible name;
    - asleep at the budget limit;
    - a drag snaps without opening the panel, and a click opens it;
    - arrow keys and the move button persist the position;
    - a reply that arrives after closing is there on reopen;
    - sign-out clears the chat and `sessionStorage`;
    - no blob without `proton:use`, and none on `/change-password`.
  - **The requestAnimationFrame loop is spied on:** it starts on thinking, stops when hidden, and never runs under reduce or calm.
  - **A Modal regression suite** (`kit.test.tsx` plus each feature's tests). Every existing `proton.test.tsx` case passes.
  - **Manual checks** in section 9, including NVDA and VoiceOver.
- **Approvals:** decisions 22, 35, 36.

### G. Platform (tool scaling, cost, eval, observability)

- **Goal.** Grow from 40 to about 120 tools without losing accuracy, speed or budget, and measure everything.
- **Changes.**
  - **G0.** `ProtonTurn.Record` stores `ProtonInputSummary.Of(input)` in `proton_usage.tools`, as its model comment says, with a regression test (Phase 0 bug fix).
  - **G1.** `Echo` in `ProtonChatAgent.cs` passes every block type back unchanged. Update `repo/ID-Indetails.IntegrationTests/Api/FakeAnthropic.cs`, including SSE bodies when `stream:true`.
  - **G2.** Send `System` as a `TextBlockParam` with an explicit cache breakpoint, and keep automatic caching for the tail.
    - Prototype sending the per-question context note as a mid-conversation `role: system` message, so it carries app authority and keeps the prefix stable.
    - Check the result through `CacheReadTokens` and `CacheWriteTokens`.
  - **G3.** Tool search with `defer_loading` and the core set, over a list fixed per conversation. It must land before the tool count passes about 55.
  - **G4.** Log wall time, time to first token, round trips and tools per question. Logs only (decision 23).
  - **G5.** Evals.
    - Run `ProtonEvalTests` for a baseline (decision 24).
    - Add cases: conversational, plan, memory, messaging tone, tool-search accuracy, chained injection, focus-steal and Enter-submit (the last two through the client harness).
    - Record latency and cost per case.
    - An LLM judge for tone is advisory only; code checks decide pass or fail.
    - Every `ChatVersion` change is gated on the evals.
  - **G6.** Turns with no tool calls don't count toward the allowance: `QuestionsAsync` filters `tool_calls > 0`, with no schema change (decision 19).
  - **G7.** Retention jobs for `proton_actions` and `proton_usage` (decision 25).
  - **G8.** Employee access in Phase 8, once G4 and G5 show acceptable cost (decisions 26 and 31). It is a seed change on `role_permissions` and `permissions`, so it is a migration.
  - **G9.** Side-effect guards (3.4) before any new endpoint mode. Each new read and propose path gets a test.
- **Tests.**
  - Tool search finds the right tool for each eval case.
  - Core tools are always present.
  - Permission filtering happens before deferral, and the `tools` array is byte-identical across a conversation's calls.
  - Rollover and caching don't break preserved thinking.
  - FakeAnthropic SSE scenarios:
    - an error before the first byte gets a normal status code;
    - an error after the first byte becomes an `error` event with the uniform body;
    - an abort still records tokens, adds nothing to history, and marks that turn's proposals Superseded.
- **Approvals:** decisions 17, 23–26, 29–31.

## 6. Phased delivery

Each PR is small and follows CLAUDE.md: inspect, tests, docs, `git diff` review, a commit saying what changed and why, and a clean `git status`. This plan goes to `repo/docs/proton-stage-5-plan.md` in the first PR. PR counts and sizes are rough planning estimates.

| Phase | Ships | Workstreams | PRs (size) | Database change | Depends on |
|---|---|---|---|---|---|
| **0. Groundwork** (nothing visible) | Plan doc; `proton_usage` input summary; `Echo` pass-through; timing logs; side-effect guards; coverage registry, sensitivity tags and test (118 classified, 8 covered); `overdue=1` fix; eval baseline run | G0, G1, G4, G5, G9, A1 (part) | 7 (M) | None | Decision 24 |
| **1. Sounds like a person** | Persona and naming; first-person copy; rollover; `sessionStorage`; greeting; briefing; starter and suggestion chips with turn origin; typed quick replies; the allowance rule | E1–E3, G6 | 5 (M) | None | Phase 0 evals; decisions 18, 19, 21, 33 |
| **2. The blob** | F1–F7, including the layering, Escape, focus and accessibility fixes and the two bugs | F | 7 (L) | None | Phase 1 copy; decisions 22, 35, 36 |
| **3. Live replies** | SSE endpoint and error writer; stream timeout; `X-Accel-Buffering`; Caddy matcher after the staging check; streaming client; Stop; real-time inflate; status lines; cache breakpoint | E4, G2 | 4 (M) | None | Phases 0 and 2; decision 17 |
| **4. Every page, tab and modal** | `Pick` sequence; surface catalogue with `confirmsAction`; opener registration; `open_surface`; the deep-link URL moves; missing admin pages; page context `surface`; `set_preference`; shell modals | B | 6 (L) | None | Phase 2 (the layer must sit above modals); decisions 10, 11, 28 |
| **5. Fill forms and draft messages** | `useProtonForm`, `fill_form`, the pilot forms and the Enter guard; `OfRuleBased`, the Team ref type and the messaging reads (D1); composer drafts (D2) | C, D1, D2 | 7 (L) | None | Phase 4; decisions 7, 11, 12, 16 |
| **MVP cut** | After Phase 5, the user has the blob, the human feel, live replies, reach to every page, tab and modal, form filling and message drafts. There is no database change. Proton still carries out only the 6 existing actions. | | **36** | | |
| **6a. Action engine** | Engine; the 6 existing actions moved (own PR); tool search; strong-tier mechanism and card variants; `IClock.FromCompanyTime`; "what Proton did" views; conformance theory | A1, A2, A5, A6, A10, G3 | 6 (L) | None | Phase 0; decisions 1–3, 32–34, 37 |
| **6b. Everyday actions** | Target read tools; T1 specs area by area (clients, meetings, teams, self, comments) | A3, A4 | 8 (M) | None | Phase 6a; decision 13 (Execution sites) |
| **7. Strong-tier actions and sending** | Lifecycle, Access (without role and move), team delete, CompanyRules; the "via Proton" marker; `propose_send_message`; group comments | A7, D3–D5 | 6 (L) | Yes: `messages.proton_action_id` column, foreign key and index (decision 15) | Phase 6; decisions 3, 9, 14, 15 |
| **8. Employees get Proton** | Seed change (grant `proton:use` to employees, fix its description); own-work actions; my-work, my pay and profile pages; log-work prefill; `list_my_work`; overtime preview | G8, A4 (own work), C (log work) | 4 (M) | Yes: seed (`role_permissions`, `permissions`) | Two weeks of measured cost after Phases 3 and 6; decisions 26, 31 |
| **9. Money, deletes, pay rules** | One family or sub-family per PR, each switched on separately | A8, A9 | 10 (L) | None (unless decision 5's restore endpoints are approved: code only) | Decisions 4, 5, 8, 13 |
| **10. Memory** | Resume from `proton_usage`; `proton_memories` with explicit save | E6 | 3 (M) | Yes: `proton_memories` table | Decision 20 |
| **11. Optional** | Reminders; `read_chat`; batches, Confirm all, auto-continue and undo, if measured need | D6, D7, A5 (deferred parts) | — | Reminders table | Decisions 6, 16, 27 |

Phases 0 to 10 come to about 73 PRs.

**Why this order**
1. Human feel and the blob are cheap, highly visible and low risk, and they are what the user asked for first.
2. The layering, focus and Enter fixes must come before any flow that reviews work in the real form.
3. Reach comes before filling, and filling before actions. Prefill and open-only are the safe fallback for every action, so they exist before the action breadth arrives.
4. The engine, the strong tier and the side-effect guards come before breadth, so specs stay short and every risky action lands on a finished safety mechanism.
5. Tool search comes before the tool count grows, on a list fixed per conversation.
6. Employee access comes once cost is measured, because "every action a user can do" mostly means employee actions.
7. Money, deletes and persistence come last, because they need policy or database approval.

## 7. Decisions needing approval

Each decision gives the options, with the recommended one in **bold**.

1. **What "every action" means.** **Adopt the tiers in 2.1: 76 actions on a card, 2 prefilled, 22 opened for the user, 11 human-only.** The alternative is to push the open-only endpoints to prefill or cards.
   - The 4 own-work actions need decision 26.
   - Bank details, pay amounts, permissions, sign-in email, passwords and role changes stay excluded at field or endpoint level.
2. **Where the confirmation tier lives** (database). **The tier lives in the tool spec, enforced at confirm with a family minimum. The card shows the tier label, and `ProtonActionRisk` stays Low/Medium (no migration).** The alternative is adding High and Critical, which means a migration of `ck_proton_actions_risk`.
3. **Strong-confirmation UX.** **T2 gets a summary card plus a second step. Irreversible, destructive, broadcast and money-recording actions need a typed phrase, checked on the server. Confirm arms after about 600 ms, is never autofocused, and has no Enter shortcut.** Hold-to-confirm is rejected.
4. **Money actions (lifting the "no money" guardrail).** **Yes, in Phase 9:**
   - the `Money` family is defined by sensitivity tags (4.1) and is off by default;
   - every action in it is T2 with a server-computed money summary;
   - there is one PR per sub-family;
   - money fields in prefilled forms follow the same approval.

   Pay amounts, bank details and the top-up stay excluded.
5. **Deletes.** **Allow them as the `Deletes` family:**
   - work-entry delete at T1, because restore exists;
   - every other delete at T2 with a typed phrase (client hard delete, requirement, meeting, team; money deletes sit in Money);
   - the prompt suggests archiving first.

   **Don't add restore endpoints for the four soft deletes in Stage 5.** If they are wanted later, they need no schema change (`DeletedAt` exists), but they are new behaviour, and restoring may collide with unique indexes, which must be checked first.
6. **Undo, batches, Confirm all and auto-continue.** **Defer all four until evals or usage show a need.** When they come:
   - Confirm all only for one batch tool, one family, T1 and the same parent record;
   - never for Messaging, Money, Deletes, Access or any T2 card;
   - a status per item, and a retry re-proposes only the failed items;
   - undo never deletes a created record.
7. **External sends.** **In Stage 5:**
   - open-only for invoice and statement email, invoice scheduling, the auto-invoice plan and invitation resend;
   - prefill for the bug report and invitation create (the email only when it is in the user's own typed words).

   Later, T2 with a typed phrase, with recipients resolved on the server from saved contacts.
8. **Pay-affecting actions.**
   - **The `PayRules` family at T2, with cards that show no amounts:** working time, hour-excuse add, schedules.
   - **Hour-excuse delete is open-only.**
   - **Confirm before exposing hour excuses whether `HourExcuseService`'s missing role-order check is intended.**
9. **Access and security actions.**
   - **T2 with a typed phrase:** employee details update, deactivate, reactivate, end sessions, restore sign-in.
   - **Open-only:** role change and domain move (they clear permission overrides, `EmployeeService.cs:310,342`), permission edits, someone else's sign-in email, pay.

   If role changes are enabled later, the input's role enum excludes admin and super admin, the card lists every override that will be cleared (computed on the server), and a typed phrase is required.
10. **New pages.** **Add the missing pages admins can reach now. Salaries, Project pay and the Proton budget are navigation only. My work, My pay and the area profile come with employee access.** Proton never reads their figures, and the page context sends the page name only.
11. **Contract and UX changes.**
    - a. **Approve:**
      - `ProtonUiRules.Pick` returning an ordered sequence (at most 1 navigation, 1 surface, 1 fill);
      - `OpenSurface`, `FillForm`, `SetPreference`;
      - the surfaces and forms enums;
      - `pendingActions[]` and `Typed`;
      - `Suggestion` on the ask request;
      - the new `ProtonRefType` values.
    - b. **The shape of `FillForm` values: prototype per-form records with `[JsonPolymorphic]` through `npm run api:types` first.** The fallback is a form discriminator plus a values object checked by `ProtonForms.Parse`. That is a looser type, an exception to CLAUDE.md §1.1, and needs approval if chosen.
    - c. **Register openers for local modals, and move a modal into the URL only where a deep link pays off on its own (push on open).**
    - d. **The page context sends field names and filled/empty only.** Values only after a later decision.
12. **Authorization mode for `[Authorize]`-only endpoints.** **A rule-based `ProtonEndpoint` mode with an explicit allow-list. It evaluates `AuthorizationPolicies.Default` plus `proton:use`, and is tested to take the actor from `ICurrentUser`.** The alternative is a new `message:send` permission: a seed change for every employee.
13. **Business rules.**
    - **Allow** creating Execution sites without fee or markup (both start at 0, which the service allows).
    - **Treat crew daily pay as a site cost, not "pay".** It is already visible with `project:view`.
      - Allow `DailyPay` explicitly in the excluded-fields test, as a recorded relaxation of the Stage 1 rule.
      - Put labour weeks and defaults in Money at T2.
14. **Messaging sends.** **Drafts first (Phase 5). Then a send card at T2 two-step for both DM and team chat, with:**
    - a typed phrase for a team chat with mentions or more than 10 people;
    - one chat per card;
    - 20 sends a day;
    - no attachments and no `@all`;
    - at most 10 mentions;
    - the figures warning;
    - the recipient provenance rule;
    - no replies sent on Proton's own initiative.
15. **Messaging attribution** (database). **Ship the "via Proton" marker before the Messaging family is switched on:**
    - a nullable `messages.proton_action_id` foreign key and index (a migration on `messages`; existing rows stay null);
    - `MessageResponse.viaProton`;
    - a subtle chip in `Thread.tsx`.

    The risk is low: an additive nullable column. The alternative is no marker, with the plan stating plainly that recipients can't tell Proton drafted a message.
16. **Reading chats.** **No message bodies in Stage 5,** only a metadata inbox. `read_chat` would come only on a later explicit approval: colleagues' messages would go to the model provider, and they are an injection channel right next to a send tool.
17. **Streaming.**
    - a. **Transport: SSE on `POST /api/proton/messages/stream`.** The alternative is Proton events over the existing `LiveHub` (SignalR, `/hubs/`) after a 202 start. That avoids proxy work but moves the question out of the request's lifetime (scoping, cancellation, several tabs).
    - b. **nginx: the API sets `X-Accel-Buffering: no`, and no nginx change is needed.** `gzip` is already off on `/api/`, and 10 s pings stay under the 60 s read timeout. A dedicated location (`web/deploy/nginx/default.conf.template`) would be added only if staging shows buffering.
    - c. **Caddy: prove the behaviour on the staging stack, then add an `encode` matcher that excludes `text/event-stream`** (`repo/deploy/Caddyfile`).
    - d. **A separate `StreamQuestionTimeoutSeconds` (120). The JSON endpoint stays at 55 s.**
    - e. **`thinking.display: "updates"` only if a prototype shows that the beta `Anthropic.Models.Beta.Messages` types fit the agent and the conversation store.** Otherwise, status lines come from the server's tool-to-phrase map only.
18. **Proactive briefing.** **Yes. A greeting with attention items when the panel opens, from `GET /api/proton/briefing`, with no model call.**
    - The client fetches it at most every 5 minutes.
    - There is no server cache unless it is measured slow. Then it gets a 60 s `IMemoryCache` keyed by user and access fingerprint (CLAUDE.md §1.2).
    - This changes Proton from "on demand only".
19. **Small talk and the allowance.** **Turns with no tool calls don't count toward the monthly question allowance** (`QuestionsAsync` filters `tool_calls > 0`, no schema change). They still count toward the budget.
20. **Persistent conversations and memory** (database).
    - a. **Phase A rollover now** (no database change).
    - b. **Resume after a restart from `proton_usage` text, with no new transcript tables.** Columns for focus and refs only if needed, with approval then.
    - c. **`proton_memories`** (new table, migration):
      - saved only from an explicit "Remember this" control or a compact card, and only from the user's own text;
      - presented to the model as user-preference data;
      - refused when a memory mentions tools, actions, people or recipients;
      - listed and deleted by the user.
21. **Identity and name.** **Proton follows the persona rules in 3.6, and the UI labels it "AI assistant".** The user-facing name comes from the existing `Email:ProductName`; the company name comes from branding. **Proton replies in the user's language (English, Arabic, French).**
22. **The blob's look and placement.** **The blob becomes the only way into Proton, plus a ⌘K action.**
    - It is mounted in `LiveRoute`, only for `proton:use` holders when Proton is configured.
    - It has eyes and uses the teal brand family.
    - Desktop gets an anchored bubble panel, and phones get a bottom sheet.
    - It can be tucked away, and has "Calm animations" and "Move to left/right" controls.
    - There is no global shortcut beyond ⌘K.
23. **Optional `proton_usage` timing columns** (database). **Logs only for now.** Columns later, if dashboards need them.
24. **Eval spend.** **Approve the baseline runs** (about $2–4 per run, the investigation's estimate) **and one run per prompt change.**
25. **Retention** (a data job, not a schema change).
    - **`proton_actions`:** blank `arguments` and `preview` text of proposals that were never carried out after 7 days, and keep the rest for 90 days.
    - **`proton_usage`:** tool inputs are summarised from Phase 0; blank question and answer text after 90 days. Counts and costs stay, for the budget.
26. **Employees get Proton** (seed change, migration on `role_permissions`). **A firm Phase 8, after two weeks of measured cost following Phases 3 and 6.** This is the prerequisite for the own-work actions, the employee pages and the log-work prefill.
27. **Reminders** (database: a table, a job, a new notification kind). **Optional, Phase 11.**
28. **Read markers.** **Open-only: opening a chat or a notification marks it read, and Proton never marks something read that the user hasn't seen.** Every write still needs a card.
29. **Dependencies and SDK versions.** **No new dependencies, and no version change of any package, the Anthropic SDK included.** If 12.53.0 lacks a needed type (streaming, tool search, the beta display), stop and ask before upgrading. No animation, drag or visual-regression libraries.
30. **Budget.** **Keep $50 a month and 300 questions per user until two weeks of measured use after Phases 3 and 6.** Then revisit with the real cost per question, since the $0.01–0.04 range is an estimate.
31. **The `proton:use` description** (seed change, migration on `permissions`). It already says "read-only" although Stage 4 ships actions.
    - **Fix it in the first approved migration (decision 15 or 26).**
    - The alternative is a standalone one-row migration now.
    - The risk is limited to running a migration.
32. **Indexes** (database). **None now.** The existing `(UserId, CreatedAt)` and `(ConversationId, Status)` indexes cover the message cap, the hourly cap, the pending cap and the audit list. **Stop and ask before adding any.**
33. **Turn origin and injection controls.**
    - **The server derives each turn's origin: typed, choice, suggestion or continue.**
    - **Gated families are refused on turns the user didn't type, enforced in `ProtonToolbox` with the tool list unchanged.**
    - **Chips are rebuilt on the server, and none are confirm-like while a card is pending.**
    - **The recipient provenance rule applies.**
34. **Kill switches and action rates.** **A global `Proton:Actions:Enabled` that covers Core, a switch per family (non-Core off), `MaxConfirmedActionsPerHour` = 30 per user, and `MaxPendingActions` = 3.**
35. **Screen readers while a modal is open.** **While Proton is open over a modal, the modal renders `aria-modal="false"` and the dock is a labelled dialog region, tested with NVDA and VoiceOver.** The alternative is to state plainly that Proton isn't reachable by screen readers while a modal is open.
36. **Mobile viewport.** **Don't add `interactive-widget=resizes-content` to `web/index.html`,** because it changes viewport behaviour across the whole app. Size the sheet with the VisualViewport API instead.
37. **Who sees what Proton did.**
    - **`GET /api/proton/actions` (own) in the panel.**
    - **An admin list on the Proton budget page for `proton_budget:manage` holders.**
    - **Activity events keep the user as the actor, and the audit list is the record of what Proton proposed and the user confirmed.** No schema change.

## 8. Risks and how they're mitigated

| Risk | Mitigation |
|---|---|
| Prompt injection through record text (descriptions, comments, notes) steering Proton into sends, deletes or money changes, including chains through chips or continue | Turn origin enforced in code; gated families refused on turns the user didn't type; recipients only from the user's own words; comment and message text only from the user; typed phrases; cards show exact text, recipients and money; chained-injection evals. |
| A write or send with no card, through focus moving into a form or the composer | Proton never moves focus (`openedByProton`); Enter guard in filled forms and drafts; vitest cases. |
| Proton opening a one-click destructive dialog | `confirmsAction` surfaces are never opened; a test fails if one is reachable. |
| Side effects during propose (email, files, notifications, live signals) | Decorators refuse them while `ProtonReadOnly.IsActive`; tests per new path. |
| Pay or bank details reaching the model through a preview or stored arguments | `Load` projections without those properties; the excluded-fields walk widened to `Proton.Actions` and `Proton.Ui`; value-based fixture tests; bank fields re-read at confirm and never stored. |
| The model choosing an external recipient and leaking data | External sends are open-only; invitation emails only from the user's typed text; a runtime "no email sent" conformance check. |
| Privilege changes through role or domain moves clearing overrides | Role and move are open-only; permission edits are open-only. |
| Mass assignment through a generic adapter | `TInput` is the allow-list; money and server fields don't exist in it; unit tests per spec. |
| Full-replace PUTs dropping data or overwriting a colleague's edit | Merge into the current record, plus a `Before` stale check on confirm, done in the base class. |
| A wrong endpoint binding giving access a user shouldn't have | The coverage test pins each action to its endpoint; policies re-checked at propose and at confirm; the conformance theory. |
| Skipping per-endpoint rate limits | No Action may wrap a named rate policy (coverage test). |
| Confirm fatigue, or a card appearing under the pointer | Tiers; arming delay; no Enter shortcut; tier labels; a cap of 3 pending; no Confirm all in Stage 5. |
| Proposals the user never saw (an aborted stream) | Marked Superseded on abort or error; FakeAnthropic test. |
| Access changing while a card is pending | Fingerprint stored in `ProtonCall`; a confirm with a changed fingerprint is superseded. |
| Time-zone mistakes | `IClock.FromCompanyTime`; tests around daylight-saving days and midnight. |
| Notification floods | Cap of 3 pending; one chat per card; daily message cap; hourly action cap. |
| More tools hurting accuracy, cost and latency | Tool search with a core set; a fixed list per conversation (cache holds); evals measuring tool choice; a cache breakpoint. |
| Streaming buffered by a proxy, or errors after headers | `X-Accel-Buffering: no`; pings; Caddy checked on staging; checks before the first byte; the SSE error writer reuses the central mapping; the JSON endpoint stays at 55 s under nginx's 60 s. |
| The panel or blob hiding Save or Confirm buttons, or breaking modals | Portaled dock at z 108; Escape exemption; keep clear of the focused element; dragging and a move button; a phone peek; the Modal regression suite. |
| Screen-reader users losing Proton under a modal | Decision 35, tested with NVDA and VoiceOver. |
| A fill landing in the wrong form or record, or replacing typed text | Bound to the form id, record ids and an expiry; visible, empty fields only; a per-field accept for replacements; a marker and undo; never submits. |
| A chat in a shared tab showing to the next person | `sessionStorage` keyed by user id, cleared on session reset and failed refresh; restored cards closed until refetched. |
| Personal data at rest (messages, contacts, questions, answers, memories) | Input summaries in `proton_usage`; retention jobs for both tables; memories only from explicit saves; deletion by the user. |
| Memories becoming a lasting injection channel | Explicit save only; the user's own text; refused when they mention tools, actions, people or recipients; presented as data. |
| Loosened tone weakening grounding | `data.md` and the grounding rules unchanged except the product name; every prompt change gated on evals. |
| Employee-only features shipping unused | Scheduled with employee access (Phase 8), not before. |
| Budget exhaustion blocking everyone | Measure first; small talk outside the allowance; tool search; a budget review; the blob shows "asleep" with the reason. |
| Beta API changes | Thinking display only after a prototype, with a clean fallback; tool search is non-beta. |

**Not verified yet.** These need checking during implementation:
- that the SDK types compile on 12.53.0: `CreateStreaming`, the tool-search types and `DeferLoading`, the beta display types, the mid-conversation system role (decision 29);
- whether Caddy's `encode` buffers or compresses SSE (decision 17c);
- whether `[JsonPolymorphic]` survives `npm run api:types` (`scripts/api-types.mjs` makes every response property required);
- whether axios's fetch adapter streams through the existing interceptors;
- the field details of `EditProfileModal` (found by grep, not fully read);
- `HourExcuseService`'s role-order check (decision 8);
- whether any service on an Action path sends email (the conformance check settles it);
- real cost and latency per question.

## 9. Testing and verification strategy

- **Backend unit tests** (`repo/ID-Indetails.UnitTests/Proton/`):
  - per spec: mapping, allowed fields only, preview and money lines, Check refusals, stale detection, value fixtures (no pay amounts or IBANs in previews or arguments);
  - `ProtonCoverageTests`: all 118 classified; actions bound to the right endpoint; no named rate policy wrapped; tagged policies at T2 or higher; the `ExternalSend` flag;
  - the rewritten `ProtonToolCatalogTests`: families, tiers, the excluded-fields walk over `Proton.Tools`, `Proton.Actions` and `Proton.Ui`, `DailyPay` explicitly allowed;
  - `IClock.FromCompanyTime` (daylight-saving days, midnight);
  - the typed-phrase check, turn origin, recipient provenance and ranking, the figures warning;
  - `ProtonForms` parsing that refuses excluded fields;
  - `ProtonUiRules.Pick` sequencing;
  - the rollover recap, and the `ProtonInputSummary` regression.
- **Backend integration tests** (`repo/ID-Indetails.IntegrationTests/`):
  - **One parameterised conformance theory driven by `ProtonCoverage`:**
    - authorized → done;
    - unauthorized → refused at propose and at confirm;
    - another domain → 404;
    - at most once;
    - expiry and supersede;
    - family and global switches;
    - a changed fingerprint;
    - no email sent unless flagged;
    - side effects refused during propose;
    - validation errors in the uniform contract.
  - **Hand-written tests** for the special cases, FakeAnthropic SSE scenarios (including abort → superseded), messaging (reach, deactivated recipient, cap, live signals flushed), rule-based endpoints evaluating the default policy, and streaming error semantics.
  - Containers used by local tests get `--cpus` and `--memory` limits.
- **Frontend (Vitest):**
  - `surfaces.ts` open checks shared with the hosts, and `confirmsAction` never reachable;
  - executor sequencing and honest "not opened" results;
  - the `useUrlParam` key registry (collisions fail);
  - the server/client registry sync;
  - `useProtonForm` adapters, the Enter guard, and "Enter in the Proton composer while a surface opens never submits";
  - the Modal and Drawer Proton-layer regression suite;
  - ActionCard variants (typed phrase, two-step, money summary, message), the arming delay and tier labels;
  - the SSE parser (chunk boundaries, ping, error, abort);
  - `sessionStorage` keyed by user and cleared on reset, and restored cards closed;
  - the blob's pure modules and the MSW integration flows from F;
  - every existing `proton.test.tsx` case kept green.

  Run with the project's small worker count (`maxWorkers 2`).
- **Evals** (`ProtonEvalTests`):
  - a baseline in Phase 0;
  - new cases: greeting and thanks, off-topic, Arabic and French, ambiguity, multi-step plan, memory, "are you a person?", injection through record text, chained injection through chips or continue, messaging tone, tool-search accuracy;
  - latency and cost recorded per case;
  - required before every `ChatVersion` change.
- **Manual browser checks** for anything jsdom can't see:
  - the blob in light and dark, and at 375 px;
  - reduced motion through DevTools emulation, and "Calm animations";
  - forced colors;
  - a Performance recording showing no long tasks while idle or talking;
  - the stack: modal, then palette, then lightbox, then toast, all over the Proton layer;
  - NVDA and VoiceOver with a modal open;
  - dragging and the keyboard on a real phone;
  - a stream through the production proxies on the staging stack;
  - prefill review in real forms.
- **Per-PR checks.**
  - `dotnet build` and the unit and integration tests for the API.
  - `npm run build`, `npm run lint` and `npm test` for the client, and `npm run api:types` after contract changes.
  - Docs updated: `repo/docs/proton.md`, `repo/docs/messaging.md`, the stage plan.
  - `git diff` and `git status` reviewed, with one scoped commit per logical change.
  - No migration is generated or applied without the matching approval.
  - Test results are reported only after the tests have actually run.

## Review notes

Every critique point was adopted except the ones below, which the code proves wrong or which conflict with each other.

- **"Reactivate also re-invites people who never signed up" (as an external send). Rejected.** `EmployeeService.ReactivateAsync` (`EmployeeService.cs:200-222`) only sets the status, and `EmployeeService` has no email dependency. The link goes out only through `Invitations.Resend`, which is open-only. Reactivate stays T2 (Access), and its card says when the person will show as invited.
- **"The daily cap, undo lookup and pending cap may need indexes". Rejected for now.** The existing `(UserId, CreatedAt)` and `(ConversationId, Status)` indexes on `proton_actions` (`ProtonActionConfiguration.cs:36-38`) serve those queries. The rule to stop and ask before adding any index is kept (decision 32).
- **"Extend `useForm` rather than add a registry". Partly rejected.** `useForm` is used only by the 5 auth pages, while the 15 app forms use `useState` plus `useFormErrors`. Moving them would be a broad refactor. Using zod for shape only and reusing `limits.ts` are adopted.
- **"The executor waits until the Proton composer is idle". Replaced.** The stronger rule is that commands Proton starts never move focus, which makes waiting unnecessary and avoids stalls. The Enter guard and its test are kept.
- **Two conflicting fixes: drop gated tools on turns the user didn't type vs keep the tool list fixed per conversation. Both kept.** Origin is enforced at run time in `ProtonToolbox` (refusal), so the `tools` array never changes.
- **"Invitation resend can stay T2 with a typed phrase". Not adopted in Stage 5.** It carries `[EnableRateLimiting(Email)]` (`InvitationsController.cs:36`), and the review's own rule bars Actions from wrapping named rate policies. It is open-only.
- **The ignore-backdrop-mousedown exemption is dropped,** as the second review said. The code confirms the scrim closes only on its own target (`Modal.tsx:55`).
- **"About 34 read tools" in the brief. Corrected to 30**, from the registrations.
