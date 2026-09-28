# Local development

## Database (MySQL 8.4 in Docker)

1. Copy the example environment file and set real passwords:

   ```bat
   copy .env.example .env
   ```

   `.env` is gitignored. Never commit it.

2. Start the database from the repository root:

   ```bash
   docker compose up -d
   ```

3. Wait until `docker ps` shows `id-indetails-mysql` as `(healthy)`.

| Setting | Value |
|---|---|
| Host / port | `127.0.0.1:3308` (loopback only, not reachable from the network) |
| Database | `MYSQL_DATABASE` from `.env` (default `id_indetails`) |
| App user | `MYSQL_USER` / `MYSQL_PASSWORD` from `.env` |
| Server settings | `utf8mb4` / `utf8mb4_0900_ai_ci`, time zone UTC (`+00:00`) |

4. Give the API its secrets through .NET user secrets. They are stored in your Windows profile (`%APPDATA%\Microsoft\UserSecrets`), not in the repository, so every developer sets their own. From the repository root, in **cmd**:

   ```bat
   rem Database: use MYSQL_PASSWORD from your .env
   dotnet user-secrets set "ConnectionStrings:Default" "Server=127.0.0.1;Port=3308;Database=id_indetails;User=id_indetails_app;Password=<MYSQL_PASSWORD>;" --project ID-Indetails.api

   rem JWT signing key: generates a random 64-character key without printing it
   for /f %k in ('powershell -NoProfile -Command "$b=New-Object byte[] 48;[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b);[Convert]::ToBase64String($b)"') do @dotnet user-secrets set "Jwt:SigningKey" "%k" --project ID-Indetails.api

   rem Setup key for creating the first super admin: choose your own and keep it
   dotnet user-secrets set "Setup:SuperAdminKey" "<a long random key>" --project ID-Indetails.api

   rem Check that all three are set
   dotnet user-secrets list --project ID-Indetails.api
   ```

   In a `.bat` file, write `%%k` instead of `%k`. The API refuses to start without the connection string and the signing key (see [authentication.md](authentication.md) for all settings).

   User secrets only load in the `Development` environment, so `dotnet ef` needs `ASPNETCORE_ENVIRONMENT=Development` set in the same window. In cmd, run `set ASPNETCORE_ENVIRONMENT=Development` on its own line first, or put it on one line as `set ASPNETCORE_ENVIRONMENT=Development&& dotnet ef ...` (no space before `&&`). Without it, `dotnet ef` fails with "Connection string 'Default' is not configured". Schema and migration commands are described in [database.md](database.md).

### Why port 3308 and all the separate names

Other projects on this machine also run databases in Docker:

| Project | Compose project | Container | Host port |
|---|---|---|---|
| MerchForge | `merchforge` | `merchforge-db` | 3306 |
| Handliz | `handlizaiapi` | `handliz-mysql` | 3307 |
| **ID-Indetails** | `id-indetails` | `id-indetails-mysql` | **3308** |

The Compose project name is pinned (`name: id-indetails`), so this stack's container, volume (`id-indetails_mysql_data`) and network (`id-indetails_default`) are always separate from the other projects'. All three databases can run at the same time.

If 3308 is ever taken, change `MYSQL_HOST_PORT` in `.env`. Only the host-side port changes.

## Email (Mailpit)

`docker compose up -d` also starts **Mailpit**, a local mail catcher. The API sends invitation and password emails to it over SMTP on `127.0.0.1:1025`. They never reach real inboxes. Read them in the Mailpit web inbox at **http://127.0.0.1:8025**.

To change the ports, set `MAILPIT_SMTP_PORT` / `MAILPIT_UI_PORT` in `.env`, and update the API's `Email:Port` to match.

### Useful commands

```bash
docker compose stop            # stop, keep data
docker compose up -d           # start again
docker compose logs -f mysql   # follow server logs
```

`docker compose down -v` **deletes the database volume and all its data.** Only use it when you mean to start from empty.

Schema changes (EF Core migrations) are never applied without explicit approval. See CLAUDE.md §9.

## Tests

```bash
dotnet test ID-Indetails.slnx
```

- **Unit tests** need nothing running.
- **Integration tests need the Docker database running.** Each test class creates its own throwaway database (`idi_test_…`) on that server, applies the real migrations, and drops it at the end. Your `id_indetails` database is never touched.
- **Credentials:** locally, the tests read the root password from `.env`. In CI, set `ID_INDETAILS_TEST_MYSQL_ADMIN` to an admin connection string without a database, for example `Server=127.0.0.1;Port=3306;User=root;Password=…;`.
