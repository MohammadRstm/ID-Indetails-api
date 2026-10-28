# CLAUDE.md — ID InDetails API

Section 0 explains what the project is and what to read first. Sections 1 to 21 are the development rules. They
apply to all work on this backend, and when a rule conflicts with speed or convenience, the rule wins.

## Contents

0. [Start here: what this project is](#0-start-here-what-this-project-is)
1. [Core Development Principles](#1-core-development-principles)
2. [Mandatory Git Commit Rule](#2-mandatory-git-commit-rule)
3. [Required Backend Architecture](#3-required-backend-architecture)
4. [FluentValidation](#4-fluentvalidation)
5. [Services](#5-services)
6. [Business Logic](#6-business-logic)
7. [Repositories](#7-repositories)
8. [Database](#8-database)
9. [Database Changes Require Explicit Approval](#9-database-changes-require-explicit-approval)
10. [Centralized Exception Handling](#10-centralized-exception-handling)
11. [Uniform Error Contract](#11-uniform-error-contract)
12. [Security Rules](#12-security-rules)
13. [Testing Requirements](#13-testing-requirements)
14. [Documentation Requirements](#14-documentation-requirements)
15. [API Design](#15-api-design)
16. [Business Logic and Data Integrity](#16-business-logic-and-data-integrity)
17. [Folder Structure](#17-folder-structure)
18. [Change Discipline](#18-change-discipline)
19. [Do Not Make Assumptions](#19-do-not-make-assumptions)
20. [Definition of Done](#20-definition-of-done)
21. [Final Rule](#21-final-rule)

## 0. Start here: what this project is

Read this section first, in every new session, before planning or changing anything. The numbered rules after it
say how to work.

### The product

ID InDetails is a company of architectural engineers. This repository is the API of its internal system, codenamed
**Hourline**. Staff log hours, run projects and clients, and see the money. Clients follow their projects in a portal.
**Proton**, the AI assistant, answers questions and fills in forms.

- **Domains** are the company's businesses, and almost everything belongs to one:
  - **Design** runs on logged hours: work entries, overtime, pay by the hour.
  - **Execution** runs construction sites on materials, weekly ground labour, client statements and site staff with
    daily logs. The **Execution box** pools what Execution clients pay and moves money to sites in the red.
  - **Website** is a third domain for the company's public website. Its staff are admins only.
- **The company area** sits above the domains: the overview, salaries, schedules, overhead, settings and permissions.
- **Roles** (`super_admin`, `admin`, `employee`) only supply *default* permissions. Endpoints check **permissions**,
  never roles. A domain the caller can't see reads as `404`.

### The repositories

The checkouts sit under `C:\Users\masst\` on the laptop and `C:\Users\mohammadrstm\` on the desktop:

| Repo | Local checkout | What it is |
|---|---|---|
| API (this one) | `source\repos\ID-Indetails` (`master`) | ASP.NET Core .NET 10, EF Core + Pomelo MySQL 8.4 |
| Client | `Desktop\Ident-Internal-Management-System-client` (`main`) | React 19 + TypeScript + Vite. It has its own CLAUDE.md. |
| Website | `Desktop\id-indetails-website` (`master`) | The public website. It reads `/api/public/website/` only. It has its own CLAUDE.md. |
| Mobile app | `Desktop\ID-Indetails-mobile` (`main`) | The phone app (Expo + React Native), a client of this API. It has its own CLAUDE.md. |
| Prototype | `source\repos\hourline` | The approved HTML/JS prototype. It is the source of truth for screens and business rules. **Read-only: never change, branch, commit or push it.** |

Feature work usually touches the API and the client together: one `MR-<Topic>` branch and one PR in each.

### What to read before working on a feature

1. [docs/status.md](docs/status.md): what the API does today and what is still missing.
2. [docs/architecture.md](docs/architecture.md): where code lives. Then read the doc for the area you're touching:

| Area | Doc |
|---|---|
| Sign-in, invitations, tokens | [authentication.md](docs/authentication.md) |
| Permissions, roles, domain visibility | [authorization.md](docs/authorization.md) |
| Projects, status, pricing, payment history | [projects.md](docs/projects.md) |
| Design: work entries, meetings, work views, outside-scope hours | [design.md](docs/design.md) |
| Execution: materials, labour, statements, site staff, daily logs, the box | [execution.md](docs/execution.md) |
| Money: the finance calculator, dashboards, Financials | [finance.md](docs/finance.md) |
| Invoices and payments | [billing.md](docs/billing.md) |
| Clients | [clients.md](docs/clients.md) |
| People, pay, hours | [employees.md](docs/employees.md), [me.md](docs/me.md) |
| Company settings, working time, overhead | [company.md](docs/company.md) |
| Client portal, project files, storage | [client-portal.md](docs/client-portal.md), [content-storage.md](docs/content-storage.md), [files.md](docs/files.md) |
| Chats, notifications, live updates, search | [messaging.md](docs/messaging.md), [notifications.md](docs/notifications.md), [search.md](docs/search.md) |
| PDFs (invoices, statements, reports) | [documents.md](docs/documents.md) |
| The public website and its dashboard | [website.md](docs/website.md) |
| Proton, the assistant | [proton.md](docs/proton.md). The `proton-stage-*-plan.md` files are history. |
| Tables, migrations, seed data | [database.md](docs/database.md) |
| Local setup, deployment, the server | [local-development.md](docs/local-development.md), [deployment.md](docs/deployment.md), [server-setup.md](docs/server-setup.md) |
| Security | [security-runbook.md](docs/security-runbook.md), [security-audit.md](docs/security-audit.md) |
| Open questions and past rule decisions | [business-rules-review.md](docs/business-rules-review.md) |

3. Then the code of that area. Each area has the same layers:
   - `Controllers/<Area>Controller.cs`;
   - `Services/<Area>/{Abstraction,Implementation}`;
   - pure rules in `Business/<Area>/*Rules.cs`;
   - `Repositories/<Area>/{Abstraction,Implementation}`;
   - `Validators/<Area>`, `DTO/Request|Response/<Area>`, `Models/` with `Configuration/ModelConfig/`.

   The tests mirror it: `ID-Indetails.UnitTests/Business` and `ID-Indetails.IntegrationTests/Api/<Area>Tests.cs`.

### Business rules that are easy to get wrong

- **The API does every calculation.** The client never computes money, overtime or access.
- **Money:**
  - A Design project's revenue is typed in.
  - An open Execution project is priced live: its fee plus the client prices of materials sent on a statement. Completing it freezes that amount.
  - Execution cash profit = received + money from the box − cost.
- **Pay:**
  - The daily one-hour break is paid, so 7 hours of required work is an 8-hour paid day.
  - The hourly rate is the salary ÷ that month's scheduled paid hours.
  - Admins and super admins are on a fixed salary and never log hours. Super admins' salaries are a company cost.
- **Privacy:** employees never see their colleagues' hours or pay.
- **Site staff:** an Execution site has at least one staff member, an active Execution employee. Staff write daily logs, which they can edit until midnight. Execution employees are paid monthly only.

### Changes that always come together

- **A new permission:**
  - `Constants/PermissionCodes.cs`;
  - `Constants/PermissionCatalog.cs`, which is the seed and the role defaults;
  - `Business/Auth/PermissionExplanations.cs`;
  - a migration for the seed;
  - the client's `P.*` codes and personas.
- **A new write endpoint:** an entry in `Proton/Actions/ProtonCoverage.cs`, either `Action(...)` or `Never(...)` with a reason.
- **Pinned counts.** Update these on purpose when they change:
  - the Proton coverage counts (`ID-Indetails.UnitTests/Proton/ProtonCoverageTests.cs`);
  - the permission count (`ID-Indetails.UnitTests/Business/PermissionExplanationsTests.cs`);
  - the table count (`ID-Indetails.UnitTests/Data/DatabaseModelTests.cs`).
- **A schema change:** see rule 9. Ask first, then:
  - `dotnet ef migrations add <Name> --project ID-Indetails.api --output-dir Data/Migrations`;
  - write any backfill SQL into the migration itself;
  - never apply it to a database without being asked.

  Branches with migrations that build on each other are stacked (each off the previous one) and merged in order.
- **A response or request shape change:** the client regenerates `src/api/schema.d.ts` from the running API
  (`npm run api:types`).
- **Docs:** update the area's doc in `docs/` in the same PR.

### Working on the user's machines

- **Ask before running builds or tests.** The user works from two machines:
  - a laptop that shuts down under load;
  - a desktop that comfortably runs on several cores.

  Before the first build or test run in a session, ask whether to run on **one core at low priority** (the laptop) or normally (the desktop). Keep to the answer for the rest of the session.
- **On the laptop:** run one core at low priority, and other sessions may be running checks there too.
- **Integration tests need MySQL from `docker-compose.yml`.**
  - Always exclude the Proton evals: `--filter "<yours>&FullyQualifiedName!~ProtonEvalTests"`. They call a paid model.
  - Every test class shares one database, so never assert global totals; follow your own rows.
  - Run the classes you touched. CI runs the whole suite.
- **CI** (`.github/workflows/ci.yml`) builds Release with `-warnaserror`, then runs unit and integration tests and builds the Docker images.
- **Deploying** is by hand only (Actions → Deploy). Never start it unless asked.
- **Git:** never commit to `master`; use `MR-<Topic>` branches. Before renaming or moving a file, check the target doesn't exist: `git mv -f` silently overwrites it.

### Keeping this file current

This file is how every new session learns the project, so keep it true:
- **When your work changes something written here, propose an edit to this file.** That includes a rule, a workflow, a file or folder, a command, or a business rule.
- **When the user tells you a new rule or preference, also propose an edit** so later sessions follow it.
- **Ask the user's permission first, every time.** Show the exact change and wait for a yes. This applies above all to the rules sections. Never change this file on your own.

### Known quirks

- **Enums** are snake_case in JSON (`payment_voided`). A model binder (`Middlewares/SnakeCaseEnumBinder.cs`) makes query and route enums take the same spelling.
- **Rules are pure:** `Business/*Rules.cs` return a `RuleDenial`, and the service throws it.
- **The Execution box ledger is `box_entries`.** Its balance is the sum of the signed amounts. Payments, voids, transfers, removals and re-adds each write one row, and rows are never edited.

---

## 1. Core Development Principles

The backend must be developed with a strong focus on:

- Clear architecture and separation of responsibilities.
- Consistent folder and file organization.
- Secure-by-default implementation.
- Explicit business rules rather than hidden assumptions.
- Testability.
- Maintainability over unnecessary complexity.
- Small, focused changes.
- No unnecessary dependency upgrades.
- No unnecessary architectural refactors.
- Existing behavior must not be changed casually.

When requirements, business rules, existing behavior, or architecture are unclear, **ASK BEFORE IMPLEMENTING**.

Do not invent requirements or silently choose behavior that could affect the system.

### 1.1 Code style: simple and reviewable

Code is written to be read and reviewed by a person.

- **Proportional design.** Build what the requirement needs, nothing more. No speculative features, generic frameworks, extra layers or abstractions "for later". Plain, straightforward code beats clever code.
- **Reuse before adding.** Extend existing services, repositories, endpoints and patterns instead of creating parallel ones.
- **Small and focused.** Classes and methods have one clear responsibility. Split a method when it stops being easy to follow.
- **Strict typing.** Use DTOs, records and enums rather than loose dictionaries, `object` or magic strings. Nullable reference types stay enabled and warnings are fixed, not suppressed.
- **Clear naming** over explanatory comments.
- **Concise comments.** No comments that restate the code or the member name. Add a short summary only where non-trivial business logic, a security decision or a non-obvious constraint needs explaining.
- **No unrelated changes.** Don't refactor, reformat or rename code outside the task's scope.

### 1.2 Rate limiting and caching

- **Rate limiting.** A global limiter covers every endpoint automatically: per signed-in user, otherwise per IP (`Extensions/AuthServiceCollectionExtensions.cs`, limits in the `RateLimiting` configuration section). Endpoints that need a stricter limit (anything accepting passwords or tokens, heavy exports) also get a named policy from `RateLimiting/RateLimitPolicies.cs` via `[EnableRateLimiting]`. Rejections use the uniform error contract (`429 too_many_requests`).
- **Caching.** Cache on the server only where it clearly pays off: data that rarely changes, or expensive calculations that are read often. Use `IMemoryCache` with a short lifetime, and drop the entry whenever the underlying data changes. Never cache in a way that could show one user another user's data or keep a revoked permission alive. Client-side state caching is the frontend's job.

---

## 2. Mandatory Git Commit Rule

**Every file that is added, edited, deleted, or moved must be committed to Git as part of the change.**

Do not leave implementation changes uncommitted.

Every commit must clearly communicate:

1. What changed.
2. Why it changed.

### Good commit messages

```text
feat: add project timeline service

Adds the project timeline service and related business logic
so timeline processing remains outside the controller.
```

```text
fix: hide internal exception details from API responses

Updates centralized exception handling so unexpected server
exceptions return a safe generic server-error response.
```

```text
refactor: move project repository into data layer

Moves persistence logic into the agreed repository structure
to keep database concerns separated from business logic.
```

### Commit messages to avoid

```text
update
changes
fix
stuff
backend changes
misc
```

### Git rules

- Keep commits logically scoped.
- Do not mix unrelated changes into one commit.
- Multiple files may be included in one commit when they are part of the same logical change.
- The commit message must explain the overall reason and nature of the change.
- Never rewrite, squash, or alter existing commits unless explicitly requested.
- Do not leave unrelated working-tree changes behind.

Before finishing a task, inspect:

```bash
git status
git diff
```

Then commit the intended changes. After committing, verify:

```bash
git status
```

There should be no unintended uncommitted changes.

---

## 3. Required Backend Architecture

The backend follows this request flow:

```text
Request
   ↓
Middleware
   ↓
Controller
   ↓
FluentValidation
   ↓
Service
   ↓
Business Logic
   ↓
Repository
   ↓
Database
```

Each layer has a clearly defined responsibility. Do not bypass layers without a clear architectural reason.

### 3.1 Request

HTTP requests enter through the ASP.NET Core request pipeline.

- Request DTOs should contain only the information necessary for the operation.
- Do not put business logic inside request models.

Do not blindly trust client-provided values for:

- User identity.
- Roles.
- Permissions.
- Domain.
- Ownership.
- Financial calculations.
- Security-sensitive properties.

### 3.2 Middleware

Middleware is responsible for cross-cutting HTTP concerns such as:

- Centralized exception handling.
- Authentication-related pipeline behavior.
- Correlation/trace IDs.
- Request/response concerns where appropriate.
- Other genuinely cross-cutting functionality.

Do not put business/domain logic inside middleware.

### 3.3 Controllers

Controllers must remain thin. Controllers should primarily:

- Receive HTTP requests.
- Delegate validation appropriately.
- Call services.
- Return appropriate HTTP responses.

Controllers must **not** contain:

- Complex business logic.
- Business calculations.
- Direct database access.
- Repository implementation.
- Large workflows.
- Security decisions that belong in authorization/services.

If a controller becomes complex, move the logic into the appropriate service/business layer.

---

## 4. FluentValidation

Use FluentValidation for request/input validation. Validation should handle rules such as:

- Required fields.
- Length constraints.
- Formats.
- Valid ranges.
- Basic input consistency.
- Other validation that can be performed before executing the operation.

Do not use FluentValidation as a replacement for business logic. Rules that depend on domain state or require a business decision should generally be handled in the service/business layer.

| Rule | Where it belongs |
|---|---|
| "Name cannot be empty" | Validation |
| "Email must have a valid format" | Validation |
| "Project must have a valid status value" | Validation |
| "Project cannot be completed while required conditions are unmet" | Business logic |
| "Employee cannot modify a project belonging to another domain" | Authorization/business logic |

---

## 5. Services

Services coordinate application operations. A service should:

- Orchestrate a use case.
- Enforce application-level rules.
- Call repositories when persistence is required.
- Coordinate multiple operations.
- Keep controllers thin.
- Return appropriate application/domain results.

Rules:

- Services should not become generic dumping grounds.
- Keep reusable business calculations and domain rules separate from HTTP concerns.
- Services must not depend on UI behavior.

---

## 6. Business Logic

Business rules must be explicit, deterministic where possible, and testable.

Business logic must not depend directly on:

- HTTP controllers.
- HTTP response objects.
- UI behavior.
- Database implementation details.

Business calculations should be kept outside controllers and should be independently testable whenever practical.

Important calculations must be implemented server-side, including:

- Financial calculations.
- Project profitability.
- Permissions.
- Status transitions.
- Ownership rules.
- Domain-specific calculations.

Never trust the frontend to enforce important business rules.

---

## 7. Repositories

Repositories are responsible for persistence and database access. Repositories should:

- Query data.
- Persist data.
- Encapsulate EF Core/database-specific operations.
- Keep database implementation details out of controllers and business logic.

Rules:

- Repositories should **not** contain unrelated business decisions.
- Do not put complex domain workflows inside repositories.
- Follow the repository conventions already established in the project.
- Do not introduce repository abstractions purely for ceremony if the existing architecture already has an established pattern.

---

## 8. Database

The database is the persistence layer.

- Database-specific concerns should not leak throughout the application.
- EF Core entities, configurations, queries, relationships, indexes, and migrations must remain organized and consistent with the project's existing structure.

---

## 9. Database Changes Require Explicit Approval

> **THIS RULE IS MANDATORY.**
> Never make database/model/schema changes without explicit approval.

Before implementing **any** database-related change, stop and discuss it first. This includes:

- Adding entities.
- Removing entities.
- Renaming entities.
- Adding columns.
- Removing columns.
- Renaming columns.
- Changing column types.
- Changing nullable/required behavior.
- Changing relationships.
- Adding/removing foreign keys.
- Adding/removing indexes.
- Adding/removing constraints.
- Changing database-stored enums.
- Changing EF Core models in a way that affects the schema.
- Creating migrations.
- Applying migrations.
- Data migrations.
- Persistent seed-data changes.

### Required workflow

When a database change is necessary:

1. Identify the required change.
2. Explain why it is required.
3. Explain the affected tables/entities.
4. Explain the migration impact.
5. Explain potential risks.
6. Ask for explicit approval.
7. Wait for approval.
8. Only then implement the database change.
9. Only run/generate/apply migrations when explicitly authorized.

### Never automatically migrate

Do **not** run the following, or any equivalent migration/database-update command, unless explicitly approved:

```bash
dotnet ef database update
```

- Do not silently change the schema just to make implementation easier.
- If a requested feature appears to require a database change, stop and ask first.

---

## 10. Centralized Exception Handling

The backend must use a centralized exception-handling system.

- There must be a consistent mechanism for converting exceptions into API responses.
- Do not create custom exception-response behavior independently inside controllers.

### 10.1 Unexpected server exceptions

Unexpected server exceptions must **never** expose internal implementation details to clients. Examples:

- Database infrastructure failures.
- Null reference/unexpected programming errors.
- Framework exceptions.
- External service failures where internal details are unsafe.
- Unhandled exceptions.
- Configuration failures.

Clients should receive a safe generic server-error response. Do **not** expose:

- Stack traces.
- SQL queries.
- Connection strings.
- File paths.
- Internal class names.
- Internal method names.
- Framework exception details.
- Secrets.
- Configuration values.
- Sensitive exception messages.

Detailed information belongs in server-side logs.

### 10.2 Business logic errors

Business-rule failures may be returned to the client when the information is safe and useful to the client. Examples:

- Invalid state transition.
- Resource cannot be modified in its current state.
- Duplicate business entity.
- Operation violates a business rule.
- User cannot perform an operation because of a domain rule.

However, do not automatically expose every exception message. Only explicitly client-safe information should be returned.

### 10.3 Validation errors

- Validation errors should be returned with enough structured information for the client to correct the request.
- Validation responses should use the same general API error contract.

---

## 11. Uniform Error Contract

API errors must use a consistent response structure. The error contract should support, where appropriate:

- Machine-readable error/code.
- Safe human-readable message.
- Validation details.
- Trace/correlation ID.
- Additional structured metadata only when safe.

Rules:

- Do not allow different controllers to invent unrelated error response formats.
- If the project already has an established error contract, use it consistently.
- The client should never need to guess which error format an endpoint returns.

---

## 12. Security Rules

Security must be considered for every backend feature.

### 12.1 Authentication

- Never trust authentication information supplied by the client.
- Validate authentication tokens through the configured authentication system.
- Never accept user identity directly from request bodies when it should come from the authenticated context.
- Never expose access or refresh tokens unnecessarily.

### 12.2 Authorization

Frontend authorization is **not** sufficient. All protected operations must be authorized by the backend.

Always verify:

- Authenticated identity.
- Role.
- Domain.
- Permission.
- Resource ownership/access where applicable.

Rules:

- Authorization must fail closed.
- Never assume that hiding a button or route in React is sufficient security.

### 12.3 Client input

Treat all client input as untrusted. Never blindly trust:

- IDs.
- User IDs.
- Business IDs.
- Domain values.
- Roles.
- Permissions.
- Ownership.
- Prices.
- Financial values.
- Status values.
- File paths.
- URLs.
- Calculated totals.

Verify sensitive information server-side.

### 12.4 Mass assignment

- Do not directly bind client DTOs to sensitive database entities when this could allow clients to modify properties they should not control.
- Explicitly map allowed fields.
- Server-controlled properties should remain server-controlled.

### 12.5 Secrets

Never commit or hard-code:

- Passwords.
- JWT secrets.
- API keys.
- Database credentials.
- Refresh-token secrets.
- Third-party credentials.
- Encryption keys.

Rules:

- Do not log secrets or tokens.
- Use the project's approved configuration and secret-management system.

### 12.6 Data exposure

Return only the data the caller is authorized to receive. Be particularly careful with:

- Personal information.
- Credentials.
- Tokens.
- Financial information.
- Administrative information.
- Internal IDs.
- Cross-domain information.
- Internal system details.

Do not expose database entities directly when a DTO is more appropriate.

### 12.7 Logging

Logs must provide useful diagnostic information without exposing sensitive data. Never log:

- Passwords.
- Access tokens.
- Refresh tokens.
- API keys.
- Secrets.
- Sensitive personal information unless explicitly required.

Unexpected exceptions should be logged server-side with sufficient diagnostic information.

### 12.8 Database security

- Use EF Core/parameterized queries appropriately.
- Never construct SQL using unsafe string interpolation with untrusted input.
- Verify authorization before accessing protected resources.
- Avoid unnecessarily broad database queries.
- Do not expose database implementation details through API responses.

### 12.9 File uploads

For features involving file uploads:

- Validate file size.
- Validate file type.
- Do not trust client-provided MIME types.
- Do not trust client-provided filenames.
- Prevent path traversal.
- Use safe generated storage identifiers where appropriate.
- Verify authorization before accessing stored files.
- Ensure one domain/user cannot access another domain/user's files.

---

## 13. Testing Requirements

Every implemented feature must include appropriate tests. Before considering a feature complete, determine whether it requires:

- Unit tests.
- Integration tests.
- API/controller tests.
- Validation tests.
- Authorization tests.
- Repository/database tests.
- Regression tests.

If tests are applicable, they must be added as part of the implementation.

### 13.1 Unit tests

Use unit tests for:

- Business rules.
- Business calculations.
- Domain logic.
- Complex transformations.
- Important edge cases.

Important financial, authorization, and domain calculations should have direct test coverage.

### 13.2 Integration tests

Use integration tests when behavior depends on multiple backend layers, for example:

```text
Request
→ Middleware
→ Controller
→ Validation
→ Service
→ Repository
→ Database
```

Integration tests are especially important for:

- Authentication.
- Authorization.
- API contracts.
- Database interactions.
- Important workflows.
- Permission boundaries.
- Exception handling.
- End-to-end backend behavior.

### 13.3 Authorization tests

When adding protected functionality, test both:

```text
Authorized user   → allowed
Unauthorized user → denied
```

- Do not test only the successful path.
- For domain-separated functionality, verify that users cannot access another domain's resources.

### 13.4 Regression tests

When fixing a bug, add a regression test whenever practical. The test should reproduce the original failure and verify the fix.

### 13.5 Verification

Before completing a feature, run the appropriate:

- Build.
- Unit tests.
- Integration tests.
- Static analysis.
- Linters/analyzers if configured.

Do not claim that tests passed unless they were actually run.

---

## 14. Documentation Requirements

When implementing a feature, update the relevant documentation when the feature changes behavior that future developers or users need to understand. Documentation may include:

- API endpoints.
- Authentication/authorization requirements.
- Business rules.
- Important workflows.
- Configuration requirements.
- Integration behavior.
- Architectural decisions.
- Database considerations.

Rules:

- Do not create unnecessary documentation for trivial internal changes.
- If an appropriate documentation location already exists, use it instead of creating duplicate documentation.

---

## 15. API Design

Keep API conventions consistent across the backend. Follow existing project conventions for:

- Route naming.
- HTTP methods.
- Status codes.
- DTO naming.
- Request/response structure.
- Pagination.
- Filtering.
- Sorting.
- Authentication.
- Authorization.
- Error responses.

Rules:

- Do not introduce a new convention for a single endpoint when the project already has an established convention.
- Use appropriate HTTP status codes.
- Do not expose internal implementation details through status messages or response bodies.

---

## 16. Business Logic and Data Integrity

Important business rules must be enforced server-side. Never rely on:

- Frontend validation.
- Hidden UI controls.
- Disabled buttons.
- Client-provided calculated totals.
- Client-provided ownership.
- Client-provided roles.
- Client-provided permissions.

If a value can be safely derived on the server, prefer deriving it rather than trusting the client.

Important calculations must be performed or verified by the backend. This is especially important for:

- Financial calculations.
- Project profitability.
- Ownership.
- Authorization.
- Domain separation.
- Status transitions.
- Payment calculations.
- Resource access.

---

## 17. Folder Structure

Maintain a clear and consistent folder structure. Use the existing backend structure as the source of truth when extending the project. New files must be placed according to their responsibility, not simply where it is convenient.

A typical structure may resemble:

```text
Backend/
├── Controllers/
├── Middleware/
├── Validators/
├── Services/
│   └── Interfaces/
├── Business/
├── Repositories/
│   └── Interfaces/
├── Data/
│   ├── Entities/
│   ├── Configurations/
│   └── ...
├── DTOs/
├── Models/
├── Exceptions/
├── Security/
├── Extensions/
└── ...
```

**The actual project structure takes precedence over this example.**

Rules:

- Do not create arbitrary folders for one-off files.
- Do not mix unrelated responsibilities.
- Keep naming consistent.
- Follow existing conventions.
- Do not introduce a major folder-structure change without discussing it first.

---

## 18. Change Discipline

Follow these steps for every task.

### Step 1 — Inspect

Inspect the existing implementation before changing anything. Understand:

- Existing architecture.
- Existing conventions.
- Existing services.
- Existing repositories.
- Existing validation.
- Existing exception handling.
- Existing authorization.
- Existing tests.
- Existing documentation.

Do not immediately start modifying files.

### Step 2 — Determine scope

Identify:

- Files that need to change.
- New files required.
- Existing behavior that may be affected.
- Tests required.
- Documentation required.
- Whether a database change is necessary.

### Step 3 — Database check

If a database change is required: **STOP.** Explain the change and request explicit approval before continuing (see [section 9](#9-database-changes-require-explicit-approval)).

### Step 4 — Implement

- Implement the smallest appropriate change.
- Do not perform unrelated refactors.
- Do not upgrade dependencies unless explicitly requested or genuinely required and approved.

### Step 5 — Test

Add/update appropriate tests.

### Step 6 — Documentation

Update relevant documentation.

### Step 7 — Verify

Run the appropriate build/tests/analysis.

### Step 8 — Review

Review:

```bash
git diff
git status
```

Check for:

- Accidental changes.
- Debug code.
- Temporary code.
- Security issues.
- Incorrect error handling.
- Missing tests.
- Missing documentation.

### Step 9 — Commit

Commit all intended added/modified/deleted/moved files. The commit must explain:

- What changed.
- Why it changed.

### Step 10 — Final Git check

Run:

```bash
git status
```

Do not leave unintended changes behind.

---

## 19. Do Not Make Assumptions

When something is unclear, **ASK**. Ask before proceeding when:

- A business rule is ambiguous.
- Multiple implementations would produce different behavior.
- Database changes are required.
- Authorization behavior is unclear.
- Existing behavior conflicts with the requested behavior.
- A major architectural decision is required.
- A destructive operation is unclear.
- The API contract is unclear.
- Security implications are unclear.

Do not hide uncertainty behind an implementation decision. A clarification question is preferable to implementing the wrong behavior.

---

## 20. Definition of Done

A backend feature is complete only when, where applicable:

- [ ] The agreed architecture is followed.
- [ ] Controllers remain thin.
- [ ] The code is proportional to the requirement: no speculative abstractions, no comment clutter (section 1.1).
- [ ] Endpoints needing a stricter limit than the global one have a named rate-limit policy; any server cache is justified and invalidated on change (section 1.2).
- [ ] FluentValidation is used appropriately.
- [ ] Business logic is separated from HTTP/database concerns.
- [ ] Repository responsibilities are respected.
- [ ] Authorization is enforced server-side.
- [ ] Errors use the centralized exception-handling system.
- [ ] Unexpected server errors do not expose internal details.
- [ ] Client-safe business/validation errors use the uniform error contract.
- [ ] Required tests are added or updated.
- [ ] Relevant documentation is updated.
- [ ] Database changes have explicit approval.
- [ ] No unauthorized migration has been executed.
- [ ] Build/tests have been verified.
- [ ] Git diff has been reviewed.
- [ ] Every intended file change is committed.
- [ ] Commit messages explain both the nature and reason for the change.
- [ ] No unintended changes remain uncommitted.

---

## 21. Final Rule

When in doubt: **do not guess.** Inspect first, explain the issue, and ask for clarification or approval.

The priority is:

```text
Security
    ↓
Correctness
    ↓
Clear Architecture
    ↓
Testability
    ↓
Maintainability
    ↓
Implementation Speed
```

Do not sacrifice correctness, security, or architectural consistency merely to implement something faster.
