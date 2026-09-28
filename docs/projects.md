# Projects

A project belongs to one domain. The rules live in `Business/Projects/`. A project of a domain the caller can't see reads as `404`, for every endpoint below.

## Endpoints

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects?search=&status=&clientId=&billingType=&domain=&page=1&pageSize=25` | `project:view` | Project list, newest first |
| `GET /api/projects/{id}` | `project:view` | A project. `money` is included only for callers with `financial:view` |
| `POST /api/projects` | `project:create` | Creates a project (below) |
| `PUT /api/projects/{id}` | `project:edit` | Replaces title, description, icon, tint, target date and owner |
| `PUT /api/projects/{id}/billing` | `project:edit` | Design only: client work or internal, the client and the revenue |
| `PUT /api/projects/{id}/pricing` | `project:pricing_manage` | Execution only: the project fee and the default material markup |
| `POST /api/projects/{id}/hold`, `/resume`, `/complete`, `/archive`, `/reopen` | `project:change_status` | Status changes (below) |

Employees hold `project:view`, so they see every project of their domain in any status, with the client by name and no money.

## Creating a project

- The start date is today (company time zone). The owner defaults to the caller.
- **Design:** `billingType` is `client` (a `clientId` is required, `revenue` optional) or `internal` (no client, no revenue). No fee or markup.
- **Execution:** always client work, with a `clientId`, a `projectFee` and a `materialMarkup` (a fraction: `0.10` is 10%, 0 to 5). `revenue` is never sent; it is calculated.
- The client must belong to the project's domain (`400`) and not be archived (`422 client_archived`).
- **Owner:** anyone of the project's domain who isn't deactivated, or a super admin (`422 owner_not_allowed`).

## Money

- **Design revenue** is typed in. **Execution revenue** is the agreed amount: material client prices plus the project fee. A material's client price is its manual price, otherwise cost × (1 + markup), rounded to cents. Ground labour is never billed. Only materials in the project's currency (USD) count.
- The Execution agreed amount is calculated live while the project is open, and frozen into `projects.revenue` when it is finalised.
- While a project is finalised (`money.isLocked`), billing, revenue, fee and markup can't change (`422 project_locked`).
- A project with any invoice or payment can't move to another client or become internal (`422 project_has_billing`), because invoices and payments keep the client they were made for.

## Status

`Business/Projects/ProjectStatusRules.cs`:

| From | Allowed |
|---|---|
| active | hold, complete, archive |
| hold | resume, complete, archive |
| completed | archive, reopen |
| archived | reopen |

Anything else is `422 invalid_status_change`, and asking for the current status is `422 status_unchanged`.

- **Finalising** happens when a project is completed, or archived without being completed first. Revenue is locked (`revenue_locked_at`, `finalized_at`, `completed_at`), the Execution agreed amount is frozen into `projects.revenue`, and every open requirement is ticked as done with `auto_done` set.
- **Reopening** clears those dates, returns Execution revenue to being calculated live, and reopens exactly the `auto_done` requirements.
- Archiving a completed project keeps its finalisation. Resuming from hold changes nothing else.

## Progress

`progress_pct` is a cache of done ÷ live requirements, rounded to a whole percentage (`Business/Projects/ProgressCalculator.cs`). A project with no requirements is 0% while open and 100% once completed or archived. It is recalculated in the same transaction as every requirement or status change, which writes a `progress_set` event (new value in `amount`, previous in `prev_amount`) whenever it changes.

## Activity

`project_created`, `project_updated`, `project_owner_set`, `project_client_set`, `revenue_set`, `revenue_changed`, `project_fee_set`, `material_markup_set`, `project_hold`, `project_resumed`, `project_completed`, `project_archived`, `project_reopened`, `project_finalized` (the frozen revenue in `amount`), `progress_set`. Amounts go in the event's `amount` and `prev_amount` columns, never in the payload.
