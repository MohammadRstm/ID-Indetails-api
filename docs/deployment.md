# Deployment

Development doesn't change: the API runs from its launch profile with user secrets, and only MySQL and Mailpit run
in Docker (`docker-compose.yml`, see [local-development.md](local-development.md)). Everything below is production
only.

## What runs in production

One server running Docker, with the stack in `deploy/docker-compose.prod.yml`:

```
Visitor ──HTTPS :443──▶ caddy (certificate, HSTS) ──▶ web: the client's nginx ──/api, /hubs──▶ api :8080 ──▶ db (MySQL 8.4)
                        (proxy network 172.30.0.0/24)                                    (data network: no route outside)
```

| Service | Image | What it does |
|---|---|---|
| `caddy` | `caddy:2.11-alpine` | HTTPS for `PUBLIC_DOMAIN`. It gets and renews the certificate by itself, redirects HTTP to HTTPS, sends HSTS and keeps no access log. The only service with published ports (80 and 443). Config: `deploy/Caddyfile`. |
| `web` | `ghcr.io/mohammadrstm/id-indetails-client` | Serves the app and proxies `/api` and `/hubs` to the API, so the browser sees one origin (the refresh cookie is `SameSite=Strict`). |
| `api` | `ghcr.io/mohammadrstm/id-indetails-api` | The API: Production environment, plain HTTP on 8080, an unprivileged user, a read-only filesystem, uploads on the `files` volume. |
| `migrations` | `ghcr.io/mohammadrstm/id-indetails-migrations` | The EF Core migrations of that API version as one executable. The deploy runs it before starting the new API. |
| `db` | `mysql:8.4` | The database, on the `db_data` volume, reachable only from the API and the migrations. |

No access logs carry query strings: Caddy keeps none, and nginx logs paths only. The live connection carries the
access token as `?access_token=`, and sign-up and reset links carry theirs in the URL.

### Images (`Dockerfile`)

- `runtime`: `dotnet publish` on `mcr.microsoft.com/dotnet/sdk:10.0`, run on `aspnet:10.0` (Ubuntu 24.04, with the
  Beirut time zone).
  - It runs as the image's unprivileged user.
  - `ASPNETCORE_ENVIRONMENT=Production` is set, and diagnostics are off.
  - Its health check calls `GET /health`.
- `migrations`: `dotnet ef migrations bundle`. The bundle is built with a placeholder connection string because it
  needs only the model; when it runs, it reads the real one from `ConnectionStrings__Default`, like the API. That keeps the password off the
  command line.

### What production checks at start-up

The API refuses to start in Production with development settings:

- `Frontend:BaseUrl` must be a public https address, because emailed links use it.
- `Email:Host` must be a real SMTP server, and `Email:FromAddress` must not end in `.local`.
- `Cors:AllowedOrigins` may hold only public https origins. It's normally empty, since the browser never calls the
  API cross-origin.
- `Proxy:KnownNetworks` must be set. `X-Forwarded-For` and `X-Forwarded-Proto` are trusted only from those networks,
  so the rate limits and logs see the visitor, not nginx. The compose file sets the proxy network
  `172.30.0.0/24`.
- As in development, the connection string and a JWT signing key of at least 32 characters are required.

The refresh cookie is always `HttpOnly`, `Secure` and `SameSite=Strict`, on `/api/auth` only. `GET /health`
answers only `Healthy` or `Unhealthy` (whether the database answers). It isn't public, because nginx doesn't proxy it;
`GET /api/health` answers the same for anyone, for an uptime monitor (see Monitoring), under the anonymous rate limit.
Every API answer carries `Cache-Control: no-store` (unless an endpoint sets its own) and `X-Content-Type-Options:
nosniff`. Request bodies are limited to 1 MB (uploads to 101 MB, on `/api/files` only), in nginx and in the API.

## Preparing the server and GitHub (once)

Follow [server-setup.md](server-setup.md):
- the server, the firewall and Docker;
- the deploy key and the pinned host key;
- the server's `.env`;
- the `production` environment and its secrets, and `CLIENT_REPO_TOKEN`;
- the first deployment and super admin;
- nightly backups.

Afterwards, protect `master`: require a pull request and the CI checks **Build and tests** and **Docker images**.

## Deploying

Actions → **Deploy** → Run workflow:

- `api_ref` and `client_ref` are the branch, tag or commit to build (default `master` and `main`).
- Approve the run when the `production` environment asks.

The workflow:

1. Builds the API, the migrations and the client, tags them `sha-<7>` of their commit, and pushes them to GHCR.
2. Copies `docker-compose.prod.yml`, `Caddyfile`, `deploy.sh`, `backup.sh` and `api-db-user.sh` to the server, and logs in to
   GHCR with the job's short-lived token, passed on stdin and logged out afterwards.
3. Runs `deploy.sh`: pull, start the database, make sure the API's own database user exists (`api-db-user.sh`), migrate,
   replace the API and the client and wait until both are healthy, then start and reload Caddy (so a changed
   `Caddyfile` applies). It records the deployed tags in `deployed.env`.

**This is an in-place deployment, not blue-green.** There is one API and one client container; Compose stops each
and starts its replacement under the same name. While the new API starts and passes its health check, `/api` and
`/hubs` answer 502 (half a minute or so), and while the client is replaced the site does too. The browser client
retries and the live connection reconnects, so people mostly see a short pause. If the new API never becomes
healthy, the deployment fails and the broken version stays up until the previous tags are deployed again (below).

The running API connects to MySQL as `MYSQL_API_USER`, which may read and write rows but not change the schema. Only
the migrations connect as the schema owner (`MYSQL_USER`).
4. Writes the deployed image tags in the run's summary.

Only one deployment runs at a time; a second one waits. Running it again with the same tags is safe.

## Rolling back

Run Deploy with **both** `api_tag` and `client_tag` set to tags from an earlier run's summary (or the package pages
in GHCR); for the side that doesn't change, give the tag that runs now (`deployed.env` on the server). A rollback
builds nothing: it deploys published images only, so no new code or migration slips in.

A migration isn't undone by deploying an older API: the older API runs on the newer schema. That works when every
migration since that version only added things. Some past migrations did remove or rename (`ExecutionPricingAndWorkingTime`,
`DropProjectMembers`, `RemoveSeededTeams`, `OverheadGroups`), so rolling back across them needs the database
restored from a backup taken before the deployment (docs/server-setup.md), losing what was written since. Write new
migrations to add first and remove only in a later release, and say so in a migration's pull request when it can't.
No backup is taken automatically before migrating yet; take one by hand (`./backup.sh`) before a deployment with a
migration that removes anything.

## Logs and status

- The run's log and summary in Actions show what was deployed and where it failed.
- On the server, `docker compose -f docker-compose.prod.yml ps` and `… logs api web`. API logs never contain tokens
  or passwords. nginx logs carry no query strings or referrers. Every container's log is rotated (5 × 10 MB,
  compressed), so logs can't fill the disk.
- Security events (refused sign-ins, rate limits reached, replayed refresh tokens) are warnings under the
  `ID_Indetails.api.Security` category, with the user id or the visitor's IP: `… logs api | grep Security`.

## Monitoring

Nothing on the server tells anyone when something breaks; set these up outside it:

- **Uptime:** point a monitor (UptimeRobot, Better Stack or similar; their free plans do) at
  `https://<PUBLIC_DOMAIN>/api/health` every 5 minutes. It answers `200 Healthy` when the API and the database
  respond, `503 Unhealthy` otherwise, and nothing else. Alert by email after two failures. The same monitor can
  watch the certificate's expiry (Let's Encrypt no longer emails about it).
- **Backups:** have the nightly cron ping a heartbeat (healthchecks.io or similar) after `backup.sh` succeeds, so a
  missed or failed backup is noticed.
- **Disk:** an alert at 80% full (most VPS providers offer one), since MySQL stops when the disk is full.
- **Deploys:** GitHub emails whoever ran Deploy when it fails.
- **Errors:** what to alert on in the logs, once they're shipped somewhere: `Error` level from the API (unexpected
  errors, failed jobs and emails), repeated 5xx, and bursts of `ID_Indetails.api.Security` warnings.
