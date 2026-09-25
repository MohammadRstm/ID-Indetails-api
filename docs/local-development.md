# Local development

## Database (MySQL 8.4 in Docker)

1. Copy the example environment file and set real passwords:

   ```bash
   cp .env.example .env
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

4. Give the API its connection string through .NET user secrets. They are stored in your Windows profile, not in the repository. Use the values from your `.env`:

   ```bash
   dotnet user-secrets set "ConnectionStrings:Default" "Server=127.0.0.1;Port=3308;Database=id_indetails;User=id_indetails_app;Password=<MYSQL_PASSWORD>;" --project ID-Indetails.api
   ```

   The API refuses to start without it. User secrets only load in the `Development` environment, so `dotnet ef` commands need `ASPNETCORE_ENVIRONMENT=Development`. Schema and migration commands are described in [database.md](database.md).

### Why port 3308 and all the separate names

Other projects on this machine also run databases in Docker:

| Project | Compose project | Container | Host port |
|---|---|---|---|
| MerchForge | `merchforge` | `merchforge-db` | 3306 |
| Handliz | `handlizaiapi` | `handliz-mysql` | 3307 |
| **ID-Indetails** | `id-indetails` | `id-indetails-mysql` | **3308** |

The Compose project name is pinned (`name: id-indetails`), so this stack's container, volume (`id-indetails_mysql_data`) and network (`id-indetails_default`) are always separate from the other projects'. All three databases can run at the same time.

If 3308 is ever taken, change `MYSQL_HOST_PORT` in `.env`. Only the host-side port changes.

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
