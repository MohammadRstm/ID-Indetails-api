# Backend architecture and project layout

This describes where code lives in the ID-Indetails backend. The rules behind it are in [CLAUDE.md](../CLAUDE.md), which takes precedence if the two ever disagree.

## Solution

| Project | Purpose |
|---|---|
| `ID-Indetails.api` | The ASP.NET Core Web API (.NET 10). |
| `ID-Indetails.UnitTests` | Fast tests with no database: business rules, calculations, validators, authorization handlers. xUnit + Moq + Shouldly. |
| `ID-Indetails.IntegrationTests` | Tests that go through several layers, including the real MySQL provider: auth, permission and domain boundaries, repositories, API contracts. xUnit + Shouldly. |

Assertions use **Shouldly** (`result.ShouldBe(42)`), not FluentAssertions: FluentAssertions v8 requires a paid licence for commercial use.

## Request flow

```text
Request → Middleware → Controller → FluentValidation → Service → Business logic → Repository → Database
```

## Folders in `ID-Indetails.api`

| Folder | What goes in it |
|---|---|
| `Controllers/` | Thin HTTP endpoints. They receive requests, call a service and return a response. No business logic and no database access. |
| `Middlewares/` | Cross-cutting HTTP concerns: centralized exception handling, correlation IDs. |
| `Validators/` | FluentValidation validators for request DTOs (shape, format, ranges). Not for rules that depend on stored state. |
| `DTO/Request/`, `DTO/Response/` | What the API accepts and returns. Entities are never exposed directly. |
| `Services/<Feature>/Abstraction/`, `Services/<Feature>/Implementation/` | Use-case orchestration, with the interface and the implementation kept apart. |
| `Business/` | Pure business rules and calculations: finance and profitability, invoice state, timeline reconstruction, overtime, pay. No HTTP and no EF Core, so they can be unit tested directly. |
| `Repositories/<Feature>/Abstraction/`, `Repositories/<Feature>/Implementation/` | Persistence and EF Core queries. No business decisions. |
| `Data/` | `IdIndetailsDbContext` plus the EF value converters (snake_case enums, UTC datetimes). Migrations live in `Data/Migrations/` and are created only after approval (CLAUDE.md §9). See [database.md](database.md). |
| `Models/` | EF Core entities, one per table. |
| `Configuration/ModelConfig/` | `IEntityTypeConfiguration<T>` classes: tables, keys, indexes, constraints. |
| `Configuration/Options/` | Typed options bound from configuration (JWT, CORS, file storage). No secrets in `appsettings.json`. |
| `Constants/` | Role, domain and permission codes, and `PermissionCatalog` (which role holds which permission; seeded into the database). These strings must match the React client's `config/roles.ts` and `config/permissions.ts`. |
| `Enums/` | C# enums (statuses, kinds). They are stored as snake_case strings limited by CHECK constraints. |
| `Exceptions/` | Typed exceptions that centralized handling turns into the uniform error contract. The folder is plural on purpose: a folder named `Exception` creates a namespace that hides `System.Exception` (CS0118). |
| `Authorization/Handlers/`, `Authorization/Requirements/` | Policy requirements and handlers for permission and domain checks. |
| `Extensions/` | `IServiceCollection` and `WebApplication` extension methods, so `Program.cs` stays short. |
| `HealthChecks/`, `RateLimiting/`, `Jobs/` | Health endpoints, rate-limit policies and background jobs. |

## Conventions

- **Feature subfolders** (such as `Services/Auth/`) are created when that feature is built, not ahead of time.
- **Empty folders** are kept visible through `<Folder Include="…" />` entries in the `.csproj`, because Git does not track empty directories. Once a folder has files, its entry can be removed.
- **Root namespace** is `ID_Indetails.api`, so namespaces follow folders (for example `ID_Indetails.api.Services.Auth.Implementation`).
