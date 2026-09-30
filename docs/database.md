# Database

MySQL 8.4 (see [local-development.md](local-development.md)), accessed through EF Core 9 with the Pomelo provider. The context is `Data/IdIndetailsDbContext.cs`, entities are in `Models/`, and their table mappings are in `Configuration/ModelConfig/`.

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
| Projects | `projects`, `project_requirements`, `requirement_assignees`, `project_meetings`, `project_expenses` |
| Design work | `work_entries`, `work_entry_comments` |
| Execution site costs and pricing | `project_materials`, `labour_weeks`, `labour_week_days`, `project_labour_defaults`, `client_statements`, `client_statement_lines`, `client_statement_deliveries`, `statement_sequences` |
| Billing (cash ledger) | `invoices`, `invoice_lines`, `invoice_deliveries`, `invoice_sequences`, `payments`, `billing_settings`, `exchange_rates` |
| Working time and hours ledger | `working_time_policies`, `user_schedules`, `user_schedule_days`, `hour_excuses`, `hour_excuse_days` |
| Company layer | `overhead_items`, `overhead_amounts`, `company_settings`, `domain_settings` |

## Execution pricing

An Execution project has a fixed **project fee** and a default **material markup** (`projects.project_fee`, `projects.material_markup`, 0–500%). Design projects have neither.

- **Client price of a material:** `project_materials.client_price` when someone set it by hand, or when a client statement froze it at the price shown. Otherwise it is calculated as cost × (1 + markup), and never stored. A price typed equal to the automatic one is stored as automatic (`NULL`).
- **Material cost:** quantity × unit cost when both are known, otherwise the lump-sum `amount`. The generated `total` column holds whichever applies.
- **Agreed amount:** the material client prices plus the project fee. It is calculated live while the project is open. Finalising writes the frozen amount into `projects.revenue`.
- **Ground labour is never billed.** The fee and the markup cover it.
- **Client statements** are immutable snapshots of materials supplied and payments received, numbered `ST-YYYY-NNN`. A material that appears on a statement is *stated*, and it can't be deleted while that statement exists.

## Working time

- **The company working week** (days, start and end), the **break** and the **overtime multiplier** live in `working_time_policies`. Each row is dated, and the row in force on a day is the latest with `effective_from` on or before it. A change never rewrites past months. The seed row is Monday–Friday, 09:00–17:00, a 60-minute break and a 1× multiplier.
- **The break is paid.** It only reduces the hours required on a day longer than 6 hours, never pay: a 9–5 day requires 7 hours and pays 8.
- **Personal schedules** (`user_schedules` with their `user_schedule_days`) override the company days and hours for one person from a date onward. A weekday with no row is a day off, and a row with `follows_default` returns the person to the company week.
- **Excuses** (`hour_excuses`) cover minutes, specific days or a whole month in a person's hours ledger. They are paid unless `is_paid` is false.
| Activity, messaging, files | `activity_events`, `notifications`, `conversations`, `messages`, `message_mentions`, `message_attachments`, `conversation_reads`, `files`, `user_board_layouts` |

## Accounts and sign-in

There is no self-signup.

1. **The first super admin** is created by a developer-only command-line tool with a generated password that is emailed to them. `users.must_change_password` forces a new password at first sign-in.
2. **Everyone else is invited.** The inviter creates a `users` row with `status = 'invited'` and sets email, role, domain, team, position and pay (`user_pay_history`). A `user_invitations` row holds the hash of a one-time, expiring signup token.
3. **Accepting** the invitation sets first name, last name, phone and password, and makes the user `active`. `CHECK`: an active user always has a password hash and a name.
4. **Employment** is kept as dated periods in `employment_periods` (`started_on`, `ended_on` the last working day, empty while it lasts; unique per person and start). Accepting the invitation starts the first; **deactivating** sets `status = 'deactivated'` and `users.deactivated_at` and ends the period; reactivating clears `deactivated_at` and either continues the period or starts a new one (see [employees.md](employees.md#deactivating-and-reactivating)). Salaries, the hours ledger and project pay follow the periods.

Who may invite whom is decided by permissions (`employee:invite`, `employee:invite_super_admin`) and enforced by the API, not the schema. See [authorization.md](authorization.md). Tokens (refresh, invitation, password reset) are stored only as unique hashes.

## Rules the database enforces

- A super admin has no domain; everyone else has exactly one.
- A permission override is a grant or a revoke, at most one per user and permission.
- An active user has a password and a name.
- Internal projects have no client and zero revenue; revenue is never negative; progress is 0–100.
- Execution projects are always client work and always have a fee and a material markup (0–500%); Design projects have neither.
- A material has either quantity and unit cost or a lump-sum amount; its client price is never negative.
- No labour day falls on a Sunday.
- At most one live (not voided) initial payment per project.
- A statement lists each material once; a stated material cannot be deleted.
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

Also derived: required hours per day, overtime (outside a person's hours, on days off, and beyond the day's required hours), the monthly hours ledger (expected, short, made up, flagged), paid breaks, shortfall deductions, project pay, "my pay", and in Execution the automatic material client prices, the agreed amount, what is owed or paid ahead, and cash and expected profit.

These are computed **by MySQL** as stored generated columns:
- `work_entries.duration_minutes`
- `project_materials.total`
- `labour_week_days.cost`
- `payments.initial_for_project_id`, which backs the one-live-initial-payment rule

One value is a deliberate cache: `projects.progress_pct` (done ÷ total requirements). It is updated in the same transaction as every requirement change, which also writes a `progress_set` event.

## Seed data

The initial migration seeds:
- **`domains`:** `design` and `execution`.
- **`roles`:** `super_admin`, `admin` and `employee`.
- **`permissions` and `role_permissions`:** from `Constants/PermissionCatalog.cs`.

`user_permission_overrides` holds individual people's differences from their role defaults (see [authorization.md](authorization.md)). The `UserPermissionOverrides` migration adds it, with the `permission:manage` and `employee:invite_super_admin` permissions.

Permission and role strings match the React client's `config/permissions.ts` and `config/roles.ts`.

The `ExecutionPricingAndWorkingTime` migration adds the default working-time policy row.

The `TeamsAndTeamPermission` migration seeds the starting teams of each domain (`Constants/TeamSeed.cs`) and the `team:manage` permission.

The `ExchangeRatePermission` migration seeds the `exchange_rate:manage` permission, held only by super admins.

The `MeetingCreatePermission` migration seeds the Design `meeting:create` permission (set meetings, and move or remove your own), held by every role.

The `MaterialMarkupEventPayload` migration only moves data: existing `material_markup_set` events get their markup in the payload (`markup`, `prevMarkup`) and empty amount columns. Each project's latest event takes the project's current markup, which is exact; older events and every `prevMarkup` keep the 2-decimal value they were saved with (12.5% stays 0.13), since the exact one was never stored.

The `EmploymentPeriods` migration adds `employment_periods` and gives everyone who has worked here one period, from their joining day to the day their account was deactivated (from UTC timestamps, so a date can be a day off near midnight; nothing was in production). Someone deactivated with no known date gets none and stays out of salaries, as before.

The `CompanyAddressAndWebsite` migration adds the optional `address` and `website` columns to `company_settings`, for the letterhead of invoices and statements.

The `DropProjectMembers` migration drops `project_members` and `disciplines`. Nothing used them, and projects have no member list: anyone in the domain may work on any project (30 Sep 2026).

`company_settings`, `domain_settings` and `billing_settings` are not seeded. They need the real company name, invoice prefixes and bank details.

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
