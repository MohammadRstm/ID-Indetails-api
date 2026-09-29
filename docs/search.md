# Search

The command palette's search (`Services/Search`), as in the prototype.

| Method and path | Needs | Purpose |
|---|---|---|
| `GET /api/search?q=` | Signed in | Projects, clients and people matching `q`, 12 results in all |

- **Groups:** projects, then clients, then people, each found by its own list's search, so the results are capped at 12 in that order (as in the prototype, a query matching 12 projects shows no clients or people).
- **Who sees what:** each group needs the permission its list needs (`project:view`, `client:view`, `employee:view`) and covers the caller's visible domains; a group the caller may not list comes back empty. Employees hold only `project:view`, so they find projects only.
- **Matching:** every word of `q` must match, ignoring case: a project's title; a client's name, organisation or label (archived clients left out); a person's first or last name, email or position (deactivated people left out). Projects of every status are found.
- **Order:** as each list orders them: projects newest start first, clients A to Z, people by name.
- `q` is required and at most 100 characters (`400`).
- **Each result:** a project's domain, title, icon, tint and status; a client's domain, display name and secondary line (the person's name when the display name is the organisation, otherwise the label); a person's domain, name and position.
