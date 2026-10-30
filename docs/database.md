# Database

MySQL 8.4 (see [local-development.md](local-development.md)), accessed through EF Core 9 with the Pomelo provider. The context is `Data/IdIndetailsDbContext.cs`, entities are in `Models/`, and their table mappings are in `Configuration/ModelConfig/`.

EF Core 9 is supported until 10 November 2026, and Pomelo has no release for EF Core 10 yet. Until that changes, the API stays on the newest EF Core 9 patch and references the driver under Pomelo (MySqlConnector) directly, so the driver keeps getting its fixes. Moving to EF Core 10 through Microting's fork of Pomelo (`Microting.EntityFrameworkCore.MySql`) is the planned next step; it keeps the same driver and migration format.

> Any change to entities, configurations, seed data or migrations needs explicit approval first (CLAUDE.md §9). That includes applying a migration.

## Single company, two domains

This database serves **one company**. There is no tenant or company key on any table, and `company_settings` is constrained to a single row (`CHECK (id = 1)`).

The company has two business domains, **Design** and **Execution**. They are divisions, not tenants:

- Root tables carry a `domain_code`: `users`, `teams`, `clients`, `projects`, `conversations`, `activity_events`, `billing_settings`, `domain_settings`, `invoice_sequences`. Child tables inherit the domain through their parent.
- Super admins have no domain (`CHECK`: `role_code = 'super_admin'` exactly when `domain_code IS NULL`). They see both domains and the company layer.
- Company overhead, salaries and company settings belong to neither domain.
- `projects (client_id, domain_code)` references `clients (id, domain_code)`, so a project can never point at the other domain's client.

The database is the second line of defence. The API must still filter every query by the caller's domain.

## Conventions

| Area | Convention |
|---|---|
| Names | snake_case tables, columns and constraints (`EFCore.NamingConventions`). Check constraints are named `ck_<table>_<rule>`. |
| Keys | `BIGINT AUTO_INCREMENT` ids. Tiny fixed reference tables (`domains`, `roles`, `permissions`) use their code as the key. |
| Time | `DATETIME(3)` in **UTC** (`UtcDateTimeConverter` marks values read back as UTC). Calendar values (deadlines, pay effective dates, labour days) are `DATE`. The company time zone (`Asia/Beirut`) lives in `company_settings`. |
| Money | `DECIMAL(18,2)` by default. Rates are `(18,4)`, exchange rates `(18,6)`, quantities `(14,3)`. Every amount has a `CHAR(3)` currency next to it; currencies are never silently converted. |
| Enums | Stored as snake_case strings (`SnakeCaseEnumConverter`), each limited by a CHECK constraint. Open vocabularies (event kinds, expense categories, units) are plain strings validated by the API. |
| Text | `VARCHAR(255)` unless configured otherwise; long free text is `TEXT`; structured payloads are `JSON`. |
| Deleting | RESTRICT everywhere, except the eight composition children (contacts, invoice lines and deliveries, labour days, requirement assignees, overhead amounts, message mentions and attachments). Money is voided, business objects are archived or deactivated, work records are soft-deleted (`deleted_at`). |

## Tables

| Area | Tables |
|---|---|
| Identity and access | `domains`, `roles`, `permissions`, `role_permissions`, `user_permission_overrides`, `users`, `teams`, `employment_periods`, `user_pay_history`, `refresh_tokens`, `user_invitations`, `password_reset_tokens` |
| Clients | `clients`, `client_contacts` |
| Projects | `projects`, `project_payment_changes`, `project_requirements`, `requirement_assignees`, `project_meetings`, `project_expenses` (Design's), `blocked_changes` |
| Design work | `work_entries`, `work_entry_comments` |
| Execution site costs and pricing | `project_staff`, `site_logs`, `pocket_entries`, `pocket_requests`, `site_expenses`, `supervision_withdrawals`, `labour_weeks`, `labour_week_days`, `project_labour_defaults`, `boqs`, `boq_lines`, `boq_deliveries`, `boq_sequences` |
| Billing (cash ledger) | `invoices`, `invoice_lines`, `invoice_deliveries`, `invoice_sequences`, `payments`, `billing_settings`, `exchange_rates` |
| Working time and hours ledger | `working_time_policies`, `user_schedules`, `user_schedule_days`, `hour_excuses`, `hour_excuse_days` |
| Company layer | `overhead_groups`, `overhead_items`, `overhead_amounts`, `account_labels`, `company_settings`, `domain_settings` |

## Execution pricing

An Execution project has a **work price** (`projects.project_fee`, 0 until a BOQ is agreed), and a **supervision rate** (`projects.supervision_rate`, a fraction from 0 to 1, 10% by default). Design projects have neither (`project_fee` and `supervision_rate` are NULL). There is no material markup: the column went with the site expenses migration.

- **Site expenses** (`site_expenses`, formerly `project_materials`) are an Execution site's costs and nothing else: no client price, no markup. The cost is quantity × unit cost when both are known, otherwise the lump-sum `amount`; the generated `total` column holds whichever applies. Each has a `spent_on` day (the day it is dated by for exchange rates), a `category` (`materials`, `contractor`, `equipment`, `fees`, `other`; `materials` by default) and an optional `account_label_id` (an account label, which may be archived later and stays). `client_ref` is the phone's idempotency key.
- **Supervision taken** (`supervision_withdrawals`) is the company withdrawing part of a site's supervision fee from the project account: a positive `amount_usd` (check `ck_supervision_withdrawals_amount_positive`), `taken_on`, a note, who recorded it (`created_by_id`) and `created_at`; removed with `deleted_at`, never deleted. It is not a cost, so nothing in the finance calculator reads it ([execution.md](execution.md#supervision-taken)).
- **Agreed amount:** the work price plus supervision (`project_fee × supervision_rate`). TVA is on the work, never on supervision. It is calculated live while the project is open. Finalising writes the frozen amount into `projects.revenue`.
- **Ground labour is never billed.** The price covers it.
- **BOQs** (`boqs`, `boq_lines`, `boq_deliveries`) price an Execution site's work: a kind (`internal`, `client`, `vo`), a status (`draft`, `agreed`), a number (`BQ-YYYY-NNN` or `VO-YYYY-NNN` from `boq_sequences`, keyed by prefix and year; null for internal drafts), an author, an adjustment and the stored `subtotal_usd` and `total_usd`, which `BoqRules` recomputes from the lines on every save. A line keeps its exact quantity, waste rate (a fraction), unit and rate; the final quantity and the line total are calculated, not stored. A delivery keeps the BOQ's total when it was sent. Drafts are soft-deleted (`deleted_at`) so what was sent stays on record. Check constraints keep a number on every kind but internal, an internal draft never agreed, a reason on VOs only, `agreed_at` with the agreed status, and negative totals to VOs. Agreeing or withdrawing sets `projects.project_fee` (see [execution.md](execution.md#boqs-and-variation-orders)). Client statements are gone.

## Working time

- **The company working week** (days, start and end), the **break** and the **overtime multiplier** live in `working_time_policies`. Each row is dated, and the row in force on a day is the latest with `effective_from` on or before it. A change never rewrites past months. The seed row is Monday–Friday, 09:00–17:00, a 60-minute break and a 1× multiplier.
- **The break is paid.** It only reduces the hours required on a day longer than 6 hours, never pay: a 9–5 day requires 7 hours and pays 8.
- **Personal schedules** (`user_schedules` with their `user_schedule_days`) override the company days and hours for one person from a date onward. A weekday with no row is a day off, and a row with `follows_default` returns the person to the company week.
- **Excuses** (`hour_excuses`) cover minutes, specific days or a whole month in a person's hours ledger. They are paid unless `is_paid` is false.
| Activity, messaging, files | `activity_events`, `notifications`, `conversations`, `messages`, `message_mentions`, `message_attachments`, `conversation_reads`, `files`, `user_board_layouts` |
| Phone app | `user_devices`, `user_notification_settings`, `user_notification_mutes` |

## Accounts and sign-in

There is no self-signup.

1. **The first super admin** is created by a developer-only command-line tool with a generated password that is emailed to them. `users.must_change_password` forces a new password at first sign-in.
2. **Everyone else is invited.** The inviter creates a `users` row with `status = 'invited'` and sets email, role, domain, team, position and pay (`user_pay_history`). A `user_invitations` row holds the hash of a one-time, expiring signup token.
3. **Accepting** the invitation sets first name, last name, phone and password, and makes the user `active`. `CHECK`: an active user always has a password hash and a name.
4. **Employment** is kept as dated periods in `employment_periods` (`started_on`, `ended_on` the last working day, empty while it lasts; unique per person and start). Accepting the invitation starts the first; **deactivating** sets `status = 'deactivated'` and `users.deactivated_at` and ends the period; reactivating clears `deactivated_at` and either continues the period or starts a new one (see [employees.md](employees.md#deactivating-and-reactivating)). Salaries, the hours ledger and project pay follow the periods.

Who may invite whom is decided by permissions (`employee:invite`, `employee:invite_super_admin`) and enforced by the API, not the schema. See [authorization.md](authorization.md). Tokens (refresh, invitation, password reset) are stored only as unique hashes.

Client portal accounts are separate tables (`portal_accounts`, `portal_project_access`, `portal_invitations`,
`portal_refresh_tokens`, `portal_password_reset_tokens`). A Design project's files are in `portal_uploads`,
`portal_files` and `portal_items` (`for_client` marks the ones chosen for the client), with the team's comments in
`project_file_comments` and the client portal's hero images (draft and published lists) in `portal_hero_images`; the
bytes live in the content store, never in MySQL or on the server's disk. See [client-portal.md](client-portal.md).

## Rules the database enforces

- A super admin has no domain; everyone else has exactly one.
- A permission override is a grant or a revoke, at most one per user and permission.
- An active user has a password and a name.
- Internal projects have no client and zero revenue; revenue is never negative; progress is 0–100.
- Execution projects are always client work and always have a work price; Design projects have none (`ck_projects_execution_money_fields`).
- A supervision rate is a fraction from 0 to 1.
- A site expense has either quantity and unit cost or a lump-sum amount, and one of the five categories.
- No labour day falls on a Sunday.
- At most one live (not voided) initial payment per project.
- A BOQ's number is unique; a line is part of its BOQ and goes with it.
- Working-time policies have valid hours (0–1440, start before end), at least one working day, and a break shorter than the day; one policy per effective date. Personal schedule days are single spans within a day, one change per person per date.
- An excuse belongs to a month (first day of the month) and carries minutes exactly when it is a "minutes" excuse.
- A project's client belongs to the same domain.
- A work entry ends after it starts; a meeting ends after it starts.
- Pay, labour defaults, overhead amounts and exchange rates are dated: one row per effective date.
- One labour report per project per week, and the week starts on a Monday.
- An invoice has a number exactly when it has been issued, and an issued invoice has a due date. Invoice numbers are unique.
- Payment amounts, exchange rates and USD amounts are positive.
- A conversation is either a DM between two distinct users (stored low id first, one row per pair) or a team group chat (one per team).
- A notification digest key is unique per recipient.
- A refresh token is the website's or the phone app's, and its revoke reason is one of the four known.
- A push token belongs to one device; quiet hours are both set or both empty, each a minute of the day; a category is muted once per person.
- A site expense's client reference is unique per person who added it.
- A payment's and a site expense's account label must be an `account_labels` row (a label that is used can't be deleted); supervision taken is more than zero and belongs to a site.
- `company_settings` has exactly one possible row.

## Derived, never stored

These are calculated by the API from the tables above, so they can never drift:

- Invoice status and balances
- Project cost, profit and margin (`Business/Finance/ProjectFinance.cs`, see [projects.md](projects.md))
- Revenue recognised
- Overtime
- Salaries owed
- Status and progress at a past moment (from `activity_events`)
- Group chat membership
- Unread counts

Also derived: required hours per day, overtime (outside a person's hours, on days off, and beyond the day's required hours), the monthly hours ledger (expected, short, made up, flagged), paid breaks, shortfall deductions, project pay, and in Execution the agreed amount, what is owed or paid ahead, and cash and expected profit.

These are computed **by MySQL** as stored generated columns:
- `work_entries.duration_minutes`
- `site_expenses.total`
- `labour_week_days.cost`
- `payments.initial_for_project_id`, which backs the one-live-initial-payment rule

One value is a deliberate cache: `projects.progress_pct` (done ÷ total requirements). It is updated in the same transaction as every requirement change, which also writes a `progress_set` event.

## Seed data

The initial migration seeds:
- **`domains`:** `design` and `execution`.
- **`roles`:** `super_admin`, `admin` and `employee`.
- **`permissions` and `role_permissions`:** from `Constants/PermissionCatalog.cs`.

`user_permission_overrides` holds individual people's differences from their role defaults (see [authorization.md](authorization.md)). The `UserPermissionOverrides` migration adds it, with the `permission:manage` and `employee:invite_super_admin` permissions. The `CompanyPermissions` migration adds `overhead_category:manage`, `session:manage` and `employee:change_email`, held by super admins, for what used to be theirs alone: expense categories, ending sessions and sign-in addresses.

Permission and role strings match the React client's `config/permissions.ts` and `config/roles.ts`.

The `ExecutionPricingAndWorkingTime` migration adds the default working-time policy row.

The `TeamsAndTeamPermission` migration seeds the starting teams of each domain (`Constants/TeamSeed.cs`) and the `team:manage` permission.

The `ExchangeRatePermission` migration seeds the `exchange_rate:manage` permission, held only by super admins.

The `MeetingCreatePermission` migration seeds the Design `meeting:create` permission (set meetings, and move or remove your own), held by every role.

The `MaterialMarkupEventPayload` migration only moves data: existing `material_markup_set` events get their markup in the payload (`markup`, `prevMarkup`) and empty amount columns. Each project's latest event takes the project's current markup, which is exact; older events and every `prevMarkup` keep the 2-decimal value they were saved with (12.5% stays 0.13), since the exact one was never stored. Markups are gone since (see `SiteExpenses` below); these events stay as history.

The `EmploymentPeriods` migration adds `employment_periods` and gives everyone who has worked here one period, from their joining day to the day their account was deactivated (from UTC timestamps, so a date can be a day off near midnight; nothing was in production). Someone deactivated with no known date gets none and stays out of salaries, as before.

The `CompanyAddressAndWebsite` migration adds the optional `address` and `website` columns to `company_settings`, for the letterhead of invoices, quotes and BOQs.

The `DropProjectMembers` migration drops `project_members` and `disciplines`. Nothing used them, and projects have no member list: anyone in the domain may work on any project (30 Sep 2026).

The `InvoiceSchedule` migration adds three nullable columns to `invoices` for Auto invoice ([billing.md](billing.md#auto-invoice)): `send_on` (indexed, for the hourly run), `send_to` and `scheduled_by_id` (a user), and a check that only a draft has a `send_on` (2 Oct 2026).

The `InvoiceReminders` migration adds the reminder settings to `billing_settings` (`reminders_enabled`, off, and `reminder_first_after_days`, `reminder_every_days`, `reminder_max`, which existing rows get as 1, 7 and 3, with a range check), and `is_reminder` to `invoice_deliveries` ([billing.md](billing.md#reminders-of-overdue-invoices)) (2 Oct 2026).

The phone app's migrations (8 Oct 2026), all additive:

- `MobileSessions` adds `client` (`web` or `mobile`; existing rows become `web`), `session_id` (indexed) and `revoke_reason` to `refresh_tokens` ([authentication.md](authentication.md#mobile-sessions)). Existing rows keep no session and no reason.
- `UserDevices` adds `user_devices`: a phone's push token (unique), its provider (`expo`), platform (`ios` or `android`), app version and the session it belongs to ([me.md](me.md#my-devices)).
- `NotificationSettings` adds `user_notification_settings` (one row per person, quiet hours in minutes after midnight, company time) and `user_notification_mutes` (one row per person and muted category). No row means the defaults ([me.md](me.md#my-notification-settings)).
- `MaterialClientRef` adds `project_materials.client_ref` (now `site_expenses.client_ref`) with a unique index on (`added_by_id`, `client_ref`), so a batch the phone sends twice adds each expense once ([execution.md](execution.md#site-expenses)). Existing rows stay `NULL`, which the index allows any number of.

The `SiteExpenses` migration (29 Oct 2026, phase 3 of the BOQ rework, see [execution-boq-plan.md](execution-boq-plan.md)) turns the materials into cost-only site expenses:

- `project_materials` becomes `site_expenses`: a new table is made and filled from the old one (the rows keep their ids, which activity events point at), then the old one is dropped. It gains `spent_on` (required; each row gets the day it was added in the company time zone, read from `company_settings`, or in UTC where the server has no time zone tables), `category` (default `materials`, with a check constraint) and `account_label_id` (a foreign key to `account_labels`), and loses `client_price`.
- `projects.material_markup` and its range check are dropped, and `ck_projects_execution_money_fields` is rewritten without it (an Execution project has a work price, a Design project has none).
- An Execution site's `project_expenses` rows (an amount above zero) move into `site_expenses` and are deleted from `project_expenses`: label to `name`, `created_at` and `created_by_id` to `added_at` and `added_by_id` (the project's owner where none), the category folded in (contractor stays; assets and hardware become equipment; services and software become fees; travel and anything else become other).
- `project_payment_changes.material_markup` and `previous_material_markup` stay as history and are no longer written.
- Activity kinds and notification kinds keep their stored `material_*` names, so there is no data change to them. `Down` makes the materials table again (a material's client price comes back empty) and turns every other site expense into a project expense of its site.

The `AccountFigures` migration (29 Oct 2026, phase 4 of the BOQ rework, see [execution-boq-plan.md](execution-boq-plan.md)) is additive:

- `payments.account_label_id` (optional, a foreign key to `account_labels`, indexed): the label a payment is filed under ([billing.md](billing.md#payments)). Existing payments have none.
- The `supervision_withdrawals` table (id, `project_id`, `amount_usd` decimal(14,2), `taken_on` date, `note` varchar(500), `created_by_id`, `created_at`, `deleted_at`), with foreign keys to `projects` and `users` (both RESTRICT) and the positive-amount check.
- No data changes. `Down` drops the foreign key, the table, the index and the column.

The `WebsiteAnalyticsPermission` migration seeds the `website_analytics:view` permission, limited to the website: super admins and the website's own staff hold it ([website.md](website.md#analytics)) (20 Oct 2026).

`company_settings`, `domain_settings` and `billing_settings` are not seeded. They need the real company name, invoice prefixes and bank details.

## Who connects

The running API connects as its own user (`MYSQL_API_USER`), granted `SELECT, INSERT, UPDATE, DELETE` on the
schema and nothing else: it can't create, alter or drop tables. Only the migrations connect as the schema owner
(`MYSQL_USER`). Each deployment creates the API user, or updates its password, with `deploy/api-db-user.sh`.

## Migrations

In **cmd**, set the environment once per window (see [local-development.md](local-development.md)):

```bat
set ASPNETCORE_ENVIRONMENT=Development

rem Create a migration (after approval)
dotnet ef migrations add <Name> --project ID-Indetails.api --output-dir Data/Migrations

rem Review the SQL it will run
dotnet ef migrations script --project ID-Indetails.api

rem Apply it - only with explicit approval
dotnet ef database update --project ID-Indetails.api
```

In bash (Git Bash, WSL, macOS or Linux):

```bash
# Create a migration (after approval)
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add <Name> --project ID-Indetails.api --output-dir Data/Migrations

# Review the SQL it will run
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations script --project ID-Indetails.api

# Apply it — only with explicit approval
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update --project ID-Indetails.api
```
