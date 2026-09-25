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
