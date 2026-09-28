# Execution sites

What site staff and admins record on an Execution project, and how it is priced for the client. Execution projects themselves, their fee and markup, and their cost, profit and margin are in [projects.md](projects.md). A project of a domain the caller can't see reads as `404`, and Design projects have none of this (`422 not_for_this_domain`).

**A finalised project is locked.** Once a site is completed or archived, nobody (admins included) adds, changes or removes its materials until it is reopened (`422 project_locked`). Projects on hold stay open. This lock, and the rule that people change only what they recorded unless they hold the manage permission, are in `Business/Execution/SiteRules.cs`.

## Materials

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/projects/{id}/materials?type=&supplier=&addedById=&from=&to=&search=&page=1&pageSize=30` | `project:view` | Newest first, with totals, types, suppliers and who added them |
| `POST /api/projects/{id}/materials` | `material:create` | `{ items: [{ name, quantity, unit, unitCost, amount, supplier, note, clientPrice }] }`, 1–100 at a time |
| `PUT /api/projects/{id}/materials/{materialId}` | `material:create` (own) or `material:manage` (anyone's) | Replaces name, quantity, unit, unit cost, amount, supplier and note |
| `DELETE /api/projects/{id}/materials/{materialId}` | `material:create` (own) or `material:manage` (anyone's) | Soft delete (`deleted_at`) |
| `PUT /api/projects/{id}/materials/{materialId}/client-price` | `project:pricing_manage` | `{ clientPrice }`; `null` returns it to the project's markup |

Rules (`Business/Materials/MaterialRules.cs`):

- A material needs a **type** (`name`) and a **cost**: quantity × unit cost when both are given, otherwise the total (`amount`). Quantity (3 decimals), unit and unit cost (4 decimals) are optional. A total that doesn't match quantity × unit cost sets the unit cost to total ÷ quantity. Materials are in the project's currency (USD).
- People change and remove only what they recorded; `material:manage` changes anyone's (`403 forbidden`). `canEdit` on each row says whether the caller may.
- **Client price:** a material's own price when set, otherwise cost × (1 + the project's markup). Setting one, on adding or afterwards, needs `project:pricing_manage`. A price equal to the automatic one is stored as automatic, so it keeps following the markup.
- **Stated materials** (on a client statement) keep their type, quantity, unit, cost and price, and can't be removed (`422 material_stated`). Their supplier and note can still change. `priceSource` is `auto`, `manual` or `stated`.
- **Who sees what:** everyone who can see the project sees materials with their costs (site staff type them). Client price, price source, markup, charge totals and the default markup are only returned to callers with `financial:view` or `project:pricing_manage`.
- **Grouping:** types and suppliers are grouped by their text trimmed, with single spaces, in lower case (`type` and `supplier` filters take these keys). A type is labelled as most recently typed. The `from`/`to` filters are days in the company time zone. `matching` totals cover everything the filters match, `all` the whole project.
- Events: `material_added`, `material_updated`, `material_removed` (cost in the `amount` columns) and `material_priced` (new and previous client price), with the name in the payload.
- Adding materials sends one `materials_added` notification (the count in the payload) to every active person of the domain holding `material:manage`, except the person who added them.

Material cost and client prices feed the shared finance calculator (`Business/Finance/ProjectFinance.cs`): cost is converted at the rate of the day a material was added.
