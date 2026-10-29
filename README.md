# ID-Indetails

The backend of Hourline, the company's internal management system: an ASP.NET Core Web API (.NET 10) on MySQL 8.4. Its business rules come from the Hourline prototype.

- **Getting started:** [docs/local-development.md](docs/local-development.md)
- **Rules for working on this code:** [CLAUDE.md](CLAUDE.md)
- **What's built and what's left:** [docs/status.md](docs/status.md)
- **Decisions and open questions:** [docs/business-rules-review.md](docs/business-rules-review.md)

## Docs

| Topic | Doc |
|---|---|
| Project layout and request flow | [architecture.md](docs/architecture.md) |
| Schema, migrations and data rules | [database.md](docs/database.md) |
| Sign-in, invitations, password reset | [authentication.md](docs/authentication.md) |
| Roles, permissions and domains | [authorization.md](docs/authorization.md) |
| People and teams | [employees.md](docs/employees.md) |
| Clients | [clients.md](docs/clients.md) |
| Projects | [projects.md](docs/projects.md) |
| Invoices and payments | [billing.md](docs/billing.md) |
| Money summaries | [finance.md](docs/finance.md) |
| Execution sites | [execution.md](docs/execution.md) |
| Design work | [design.md](docs/design.md) |
| Company layer: settings, working time, salaries | [company.md](docs/company.md) |
| Self-service (`/api/me`) | [me.md](docs/me.md) |
| Notifications | [notifications.md](docs/notifications.md) |
| Messaging and live updates | [messaging.md](docs/messaging.md) |
| Uploads, photos and the logo | [files.md](docs/files.md) |
| PDFs and emailing invoices, quotes and BOQs | [documents.md](docs/documents.md) |
| Search | [search.md](docs/search.md) |
| Proton, the company assistant (reads, analyses, proposes changes) | [proton.md](docs/proton.md) |
| Production: Docker images, CI, the manual deploy and rollback | [deployment.md](docs/deployment.md) |
| What to do when someone can't sign in, a session or secret may be stolen, or Proton misbehaves | [security-runbook.md](docs/security-runbook.md) |
| Load and stress testing, in an environment isolated from production | [load-tests/README.md](load-tests/README.md) |
| Setting up the production server (once) | [server-setup.md](docs/server-setup.md) |
