# Database

MySQL 8.4 (see [local-development.md](local-development.md)), accessed through EF Core 9 with the Pomelo provider. The context is `Data/IdIndetailsDbContext.cs`, entities are in `Models/`, and their table mappings are in `Configuration/ModelConfig/`.

> Any change to entities, configurations, seed data or migrations needs explicit approval first (CLAUDE.md §9). That includes applying a migration.

## Single company, two domains

This database serves **one company**. There is no tenant or company key on any table, and `company_settings` is constrained to a single row (`CHECK (id = 1)`).

The company has two business domains, **Design** and **Execution**. They are divisions, not tenants:

- Root tables carry a `domain_code`: `users`, `teams`, `disciplines`, `clients`, `projects`, `conversations`, `activity_events`, `billing_settings`, `domain_settings`, `invoice_sequences`. Child tables inherit the domain through their parent.
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
| Identity and access | `domains`, `roles`, `permissions`, `role_permissions`, `users`, `teams`, `disciplines`, `user_pay_history`, `refresh_tokens`, `user_invitations`, `password_reset_tokens` |
| Clients | `clients`, `client_contacts` |
| Projects | `projects`, `project_members`, `project_requirements`, `requirement_assignees`, `project_meetings`, `project_expenses` |
| Design work | `work_entries`, `work_entry_comments` |
| Execution site costs | `project_materials`, `labour_weeks`, `labour_week_days`, `project_labour_defaults` |
| Billing (cash ledger) | `invoices`, `invoice_lines`, `invoice_deliveries`, `invoice_sequences`, `payments`, `billing_settings`, `exchange_rates` |
| Company layer | `overhead_items`, `overhead_amounts`, `company_settings`, `domain_settings` |
| Activity, messaging, files | `activity_events`, `notifications`, `conversations`, `messages`, `message_mentions`, `message_attachments`, `conversation_reads`, `files`, `user_board_layouts` |

## Accounts and sign-in

There is no self-signup.

1. **The first super admin** is created by a developer-only command-line tool with a generated password that is emailed to them. `users.must_change_password` forces a new password at first sign-in.
2. **Everyone else is invited.** The inviter creates a `users` row with `status = 'invited'` and sets email, role, domain, team, position and pay (`user_pay_history`). A `user_invitations` row holds the hash of a one-time, expiring signup token.
3. **Accepting** the invitation sets first name, last name, phone and password, and makes the user `active`. `CHECK`: an active user always has a password hash and a name.

Super admins may invite anyone into either domain (including other super admins). Admins may invite admins and employees into their own domain. This is enforced by the API, not the schema. Tokens (refresh, invitation, password reset) are stored only as unique hashes.

## Rules the database enforces

- A super admin has no domain; everyone else has exactly one.
- An active user has a password and a name.
- Internal projects have no client and zero revenue; revenue is never negative; progress is 0–100.
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
- Project cost, profit and margin
- Revenue recognised
- Overtime
- Salaries owed
- Status and progress at a past moment (from `activity_events`)
- Group chat membership
- Unread counts

Three values are computed **by MySQL** as stored generated columns: `work_entries.duration_minutes`, `project_materials.total` and `labour_week_days.cost`.

One value is a deliberate cache: `projects.progress_pct` (done ÷ total requirements). It is updated in the same transaction as every requirement change, which also writes a `progress_set` event.

## Seed data

The initial migration seeds:
- **`domains`:** `design` and `execution`.
- **`roles`:** `super_admin`, `admin` and `employee`.
- **`permissions` and `role_permissions`:** from `Constants/PermissionCatalog.cs`.

Permission and role strings match the React client's `config/permissions.ts` and `config/roles.ts`.

`company_settings`, `domain_settings` and `billing_settings` are not seeded. They need the real company name, invoice prefixes and bank details.

## Migrations

```bash
# Create a migration (after approval)
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add <Name> --project ID-Indetails.api --output-dir Data/Migrations

# Review the SQL it will run
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations script --project ID-Indetails.api

# Apply it — only with explicit approval
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update --project ID-Indetails.api
```
