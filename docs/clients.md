# Clients

A client belongs to one domain. The rules live in `Business/Clients/ClientRules.cs`.

## Endpoints

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/clients?search=&archived=&domain=&filter=&sort=name&page=1&pageSize=25` | `client:view` | Client list, A to Z unless sorted. Archived clients only with `archived=true`. `search` matches name, organisation and label |
| `GET /api/clients/{id}` | `client:view` | A client with its contacts and project count, and its work for callers with `work_entry:view_all` |
| `POST /api/clients` | `client:create` | Adds a client. `domainCode` is needed only by callers who see both domains |
| `PUT /api/clients/{id}` | `client:manage` | Replaces every field and the whole contact list |
| `POST /api/clients/{id}/archive`, `/restore` | `client:manage` | Hides or shows the client. Its projects and history are unchanged |
| `DELETE /api/clients/{id}` | `client:manage` | Only while no project uses the client (`422 client_has_projects`); archive it instead |

With `financial:view`, list rows and the client carry `money`, the client's money across their projects, and the client also carries `projectMoney`, each project's money. Without it these fields are left out. See [finance.md](finance.md).

A client of a domain the caller can't see reads as `404`. Adding one in such a domain is `403`.

## The client list and page

- **`filter`:** `active` (with an active or on-hold project), `owing` (still owed on their invoices) or `empty` (no projects).
- **`sort`:** `name` (A to Z, the default), `projects`, `hours` and `activity` (most recent work first; a client without work counts from when it was added), or by money: `revenue`, `profit`, `margin` (unknown margins last) and `outstanding`, each highest first.
- **Money:** the money sorts and the `owing` filter need `financial:view` (`403` otherwise). They need every matching client's money, so those clients are loaded, sorted and paged together in one batched load.
- **`work`** on each row (`work_entry:view_all` only): the minutes logged on the client's projects and when the latest work started.
- **`work`** on the client page (`work_entry:view_all` only): all-time minutes, entries and contributors, the minutes of the last 12 months ending with this one, and the 8 people who logged the most time for the client. Only Design logs work, so an Execution client's are zero.

## Rules

- **Name** and **label** are required. The display name is the organisation when there is one, otherwise the name.
- **At least one contact** (email or phone), at most 20. Only phones can be WhatsApp contacts.
- **Primary contact:** exactly one. The one marked, or else the first.
- **A contact belongs to one client per domain**, archived clients included. Emails are compared ignoring case and spaces, phones by their digits only. A second client with the same contact is `409 contact_taken`.
- **The same name** is allowed (two people can share a name). The response's `nameSharedWithAnotherClient` flags it so the client can warn.

## Activity

`client_added`, `client_updated`, `client_archived`, `client_restored`, `client_deleted` (with the name in the payload). The client is the event's subject (`subject_type = 'client'`). `activity_events.client_id` is left empty, because it is a restricting foreign key and would stop a client with history from being deleted.
