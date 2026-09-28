# Clients

A client belongs to one domain. The rules live in `Business/Clients/ClientRules.cs`.

## Endpoints

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/clients?search=&archived=&domain=&page=1&pageSize=25` | `client:view` | Client list. Archived clients only with `archived=true`. `search` matches name and organisation |
| `GET /api/clients/{id}` | `client:view` | A client with its contacts and project count |
| `POST /api/clients` | `client:create` | Adds a client. `domainCode` is needed only by callers who see both domains |
| `PUT /api/clients/{id}` | `client:manage` | Replaces every field and the whole contact list |
| `POST /api/clients/{id}/archive`, `/restore` | `client:manage` | Hides or shows the client. Its projects and history are unchanged |
| `DELETE /api/clients/{id}` | `client:manage` | Only while no project uses the client (`422 client_has_projects`); archive it instead |

A client of a domain the caller can't see reads as `404`. Adding one in such a domain is `403`.

## Rules

- **Name** and **label** are required. The display name is the organisation when there is one, otherwise the name.
- **At least one contact** (email or phone), at most 20. Only phones can be WhatsApp contacts.
- **Primary contact:** exactly one. The one marked, or else the first.
- **A contact belongs to one client per domain**, archived clients included. Emails are compared ignoring case and spaces, phones by their digits only. A second client with the same contact is `409 contact_taken`.
- **The same name** is allowed (two people can share a name). The response's `nameSharedWithAnotherClient` flags it so the client can warn.

## Activity

`client_added`, `client_updated`, `client_archived`, `client_restored`, `client_deleted` (with the name in the payload). The client is the event's subject (`subject_type = 'client'`). `activity_events.client_id` is left empty, because it is a restricting foreign key and would stop a client with history from being deleted.
