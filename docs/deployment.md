# Deployment

Development doesn't change: the API runs from its launch profile with user secrets, and only MySQL and Mailpit run
in Docker (`docker-compose.yml`, see [local-development.md](local-development.md)). Everything below is production
only.

## What runs in production

One server running Docker, with the stack in `deploy/docker-compose.prod.yml`:

```
Visitor ──HTTPS──▶ TLS proxy on the server (Caddy, nginx, the host's load balancer…)
                     │  127.0.0.1:8080
                     ▼
                   web: the client's nginx ──/api, /hubs──▶ api :8080 ──▶ db (MySQL 8.4)
                   (proxy network 172.30.0.0/24)            (data network: no route outside)
```

| Service | Image | What it does |
|---|---|---|
| `web` | `ghcr.io/mohammadrstm/id-indetails-client` | Serves the app and proxies `/api` and `/hubs` to the API, so the browser sees one origin (the refresh cookie is `SameSite=Strict`). The only service reachable from outside, and only on the host's loopback by default. |
| `api` | `ghcr.io/mohammadrstm/id-indetails-api` | The API: Production environment, plain HTTP on 8080, an unprivileged user, a read-only filesystem, uploads on the `files` volume. |
| `migrations` | `ghcr.io/mohammadrstm/id-indetails-migrations` | The EF Core migrations of that API version as one executable. The deploy runs it before starting the new API. |
| `db` | `mysql:8.4` | The database, on the `db_data` volume, reachable only from the API and the migrations. |

TLS is set up on the server, outside this stack, because it depends on the domain and the host: a TLS proxy
forwards `https://<domain>` to `127.0.0.1:8080`, redirects HTTP to HTTPS, sends HSTS, and keeps query strings out of
its logs (the live connection carries the access token as `?access_token=`).

### Images (`Dockerfile`)

- `runtime`: `dotnet publish` on `mcr.microsoft.com/dotnet/sdk:10.0`, run on `aspnet:10.0` (Ubuntu 24.04, with the
  Beirut time zone).
  - It runs as the image's unprivileged user.
  - `ASPNETCORE_ENVIRONMENT=Production` is set, and diagnostics are off.
  - Its health check calls `GET /health`.
- `migrations`: `dotnet ef migrations bundle`. The bundle is built with a placeholder connection string because it
  needs only the model; the real one is given as `--connection` when it runs.

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
answers only `Healthy` or `Unhealthy` (whether the database answers). It isn't public, because nginx doesn't proxy it.

## Preparing the server (once)

1. A Linux server with Docker Engine and the compose plugin, and a deploy user in the `docker` group. Allow SSH
   logins for that user by key only.
2. `sudo mkdir -p /opt/id-indetails && sudo chown deploy: /opt/id-indetails` (or another folder: set the
   repository variable `DEPLOY_PATH`).
3. Copy `deploy/.env.production.example` to `/opt/id-indetails/.env`, fill it in and run `chmod 600 .env`. The secrets
   live only in this file, on the server, and are never kept in GitHub.
4. Set up the TLS proxy for the domain, pointing at `127.0.0.1:8080`.
5. Back up the `db_data` and `files` volumes regularly, for example a nightly `mysqldump` plus a copy of the files
   volume, and keep the copies off the server.

## GitHub settings (once)

- **Environment `production`** (Settings → Environments):
  - Required reviewers: you. The deploy waits for your approval.
  - Deployment branches: `master` only.
  - Secrets:
    - `DEPLOY_HOST` and `DEPLOY_USER`.
    - `DEPLOY_SSH_KEY`: the deploy user's private key, ideally a key used for nothing else.
    - `DEPLOY_KNOWN_HOSTS`: the server's host key line, from `ssh-keyscan <host>` checked against the server. A
      changed key stops the deployment.
- **Repository secret `CLIENT_REPO_TOKEN`**: a fine-grained token with read-only Contents access to
  `ID-Indetails-client` and nothing else. The client repository is private.
- **Packages:** the images are pushed to GHCR by this repository's workflow with its own `GITHUB_TOKEN`, so no other
  registry secret is needed. Keep the packages private.
- **Actions:** set the default workflow permissions to read. The workflows ask for more per job.
- **Branch protection on `master`:** require a pull request and the CI checks **Build and tests** and **Docker
  images**.

## Deploying

Actions → **Deploy** → Run workflow:

- `api_ref` and `client_ref` are the branch, tag or commit to build (default `master` and `main`).
- Approve the run when the `production` environment asks.

The workflow:

1. Builds the API, the migrations and the client, tags them `sha-<7>` of their commit, and pushes them to GHCR.
2. Copies `docker-compose.prod.yml` and `deploy.sh` to the server, and logs in to GHCR with the job's short-lived
   token, passed on stdin and logged out afterwards.
3. Runs `deploy.sh`: pull, migrate, then start the new API and client and wait until both are healthy.
4. Writes the deployed image tags in the run's summary.

Only one deployment runs at a time; a second one waits. Running it again with the same tags is safe.

## Rolling back

Run Deploy with `api_tag` and/or `client_tag` set to tags from an earlier run's summary, or from the package pages
in GHCR. Those images are deployed without building.

A migration isn't undone by deploying an older API. Migrations here only add, so an older API normally keeps working
on the newer schema. If one ever removes or renames something, roll the database back from a backup taken before the
deployment, and say so in that migration's pull request.

## Logs and status

- The run's log and summary in Actions show what was deployed and where it failed.
- On the server, `docker compose -f docker-compose.prod.yml ps` and `… logs api web`. API logs never contain tokens
  or passwords. nginx logs carry no query strings or referrers.
