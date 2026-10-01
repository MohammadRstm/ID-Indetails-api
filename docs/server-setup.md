# Setting up the production server

Once, before the first deployment. Afterwards every release is **Actions → Deploy** ([deployment.md](deployment.md)).

The steps marked **(server)** are typed on the server, in the Linux shell over SSH. The steps marked **(your PC)**
are typed in **cmd** on your Windows machine, which has `ssh`, `scp`, `ssh-keygen` and `curl` built in.

## What you need first

- **A server:** a Linux VPS running **Ubuntu 24.04**, with 2 vCPU, 4 GB RAM and 40 GB of disk. You need its public IP
  and the root (or sudo) login the provider gave you.
- **A domain:** add an **A record** for the app's name (e.g. `app.example.com`) pointing at the server's IP. Caddy
  can only get the certificate once this resolves.
- **An SMTP account** for outgoing mail. You need its host, port 587, username and password, and a sender address on
  your domain. Add the **SPF and DKIM** records the provider gives you (and ideally DMARC), or mail lands in spam.

## 1. Secure the server (server)

Log in as root (`ssh root@<server-ip>` from your PC), then:

```bash
# updates now, and security updates by themselves from now on
apt update && apt -y upgrade
apt -y install unattended-upgrades ufw
dpkg-reconfigure -f noninteractive unattended-upgrades

# a deploy user, who runs the stack; root won't log in over SSH any more
adduser --disabled-password --gecos "" deploy
mkdir -p /home/deploy/.ssh && cp ~/.ssh/authorized_keys /home/deploy/.ssh/ 2>/dev/null || true
chown -R deploy: /home/deploy/.ssh && chmod 700 /home/deploy/.ssh

# only SSH and the web are open
ufw default deny incoming
ufw default allow outgoing
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp
ufw --force enable
```

Then, in `/etc/ssh/sshd_config`, set `PasswordAuthentication no` and `PermitRootLogin no`, and run
`systemctl restart ssh`. **Before you close the root session**, check that `ssh deploy@<server-ip>` works from your
PC (step 3 adds the key).

> Docker publishes ports 80 and 443 itself, past ufw. That's intended here: only Caddy publishes any port, and
> MySQL, the API and nginx have none.

## 2. Install Docker (server)

```bash
curl -fsSL https://get.docker.com | sh
usermod -aG docker deploy
mkdir -p /opt/id-indetails && chown deploy: /opt/id-indetails
```

Membership of the `docker` group amounts to root on this machine, so keep the deploy user's key private.

## 3. Keys for the deployment (your PC)

The Deploy workflow logs in as `deploy` with a key used for nothing else:

```bat
ssh-keygen -t ed25519 -f "%USERPROFILE%\.ssh\idi_deploy" -N "" -C "id-indetails deploy"
type "%USERPROFILE%\.ssh\idi_deploy.pub"
```

Paste that public key as a new line in `/home/deploy/.ssh/authorized_keys` on the server **(server)**. Then check
the login, and record the server's host key for GitHub:

```bat
ssh -i "%USERPROFILE%\.ssh\idi_deploy" deploy@<server-ip> "docker --version"
ssh-keyscan -t ed25519 <server-ip>
```

Compare the `ssh-keyscan` fingerprint with the one the server shows (`ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub`
on the server). If they match, that line is `DEPLOY_KNOWN_HOSTS`.

## 4. The server's settings (server, as deploy)

```bash
cd /opt/id-indetails
nano .env          # paste deploy/.env.production.example from the repository and fill it in
chmod 600 .env
```

- **Every password and key:** long and random. Generate each with `openssl rand -base64 48`.
- **`SETUP_SUPER_ADMIN_KEY`:** set it now. You'll empty it again in step 7.
- **`PUBLIC_DOMAIN`:** the domain from the A record, without `https://`. Set `ACME_EMAIL` to your email.
- **The `SMTP_*` lines:** your mail provider's details.

This file is the only place the secrets live. Keep a copy in your password manager.

## 5. GitHub settings (browser)

In **ID-Indetails-api → Settings**:

- **Environments → New environment `production`:**
  - Required reviewers: you.
  - Deployment branches and tags: selected branches, `master`.
  - Environment secrets:
    - `DEPLOY_HOST`: the server's IP or name.
    - `DEPLOY_USER`: `deploy`.
    - `DEPLOY_SSH_KEY`: the whole content of `%USERPROFILE%\.ssh\idi_deploy`, the private key. Delete it from
      your PC afterwards if you don't need it there.
    - `DEPLOY_KNOWN_HOSTS`: the line from step 3.
- **Secrets and variables → Actions → New repository secret `CLIENT_REPO_TOKEN`:** a fine-grained personal access
  token (GitHub → Settings → Developer settings) for **ID-Indetails-client** only, with Contents set to read-only.
- **Actions → General → Workflow permissions:** read repository contents.
- Optionally, **Variables → `DEPLOY_PATH`**, if you used a folder other than `/opt/id-indetails`.

## 6. The first deployment (browser)

**Actions → Deploy → Run workflow**, with `master` and `main`. Approve it when it asks.

When it's green, open `https://<your-domain>`. The sign-in page should load with a valid certificate. Caddy fetched
it on the first request, which can take a few seconds.

## 7. The first super admin (server, as deploy)

```bash
cd /opt/id-indetails
read -rp "Email: " EMAIL; read -rp "First name: " FIRST; read -rp "Last name: " LAST
KEY=$(grep '^SETUP_SUPER_ADMIN_KEY=' .env | cut -d= -f2-)
curl -fsS "https://$(grep '^PUBLIC_DOMAIN=' .env | cut -d= -f2-)/api/setup/super-admin" \
  -H "X-Setup-Key: $KEY" -H "Content-Type: application/json" \
  -d "{\"email\":\"$EMAIL\",\"firstName\":\"$FIRST\",\"lastName\":\"$LAST\"}"
unset KEY
```

The temporary password is emailed to that address. At the first sign-in the app asks for a new password.

Then **switch the setup endpoint off**: empty `SETUP_SUPER_ADMIN_KEY=` in `.env`, and restart the API with the same
images:

```bash
set -a; . ./deployed.env; set +a
docker compose -f docker-compose.prod.yml up -d api
```

With the key empty, `/api/setup` answers 404 to everyone.

In the app, then:
- add the company's details and logo (Settings → Company);
- set up invoicing for each business (Settings → Finance);
- invite the team.

## 8. Nightly backups (server, as deploy)

```bash
cd /opt/id-indetails
./backup.sh                      # once by hand, to check it works
crontab -e                       # and add:
# 30 2 * * * /opt/id-indetails/backup.sh >> /opt/id-indetails/backups/backup.log 2>&1
```

It keeps 14 days of database dumps and uploaded files in `/opt/id-indetails/backups`. **Copy that folder off the
server** regularly, for example with your provider's snapshots or object storage, or with `scp` to another machine.
A backup on the same disk is lost with it.

**Restoring** (server, into the running stack):

```bash
cd /opt/id-indetails && set -a; . ./.env; . ./deployed.env; set +a
gunzip -c backups/db-<stamp>.sql.gz | docker compose -f docker-compose.prod.yml exec -T -e MYSQL_PWD="$MYSQL_ROOT_PASSWORD" db mysql -uroot "$MYSQL_DATABASE"
docker run --rm -v id-indetails_files:/files -v "$PWD/backups:/in:ro" alpine:3 tar -xzf /in/files-<stamp>.tar.gz -C /files
docker compose -f docker-compose.prod.yml restart api
```

## Day to day (server, as deploy)

```bash
cd /opt/id-indetails && set -a; . ./deployed.env; set +a
docker compose -f docker-compose.prod.yml ps               # what runs, and whether it's healthy
docker compose -f docker-compose.prod.yml logs -f api web  # logs (no tokens or passwords in them)
```

The server takes its own security updates. Reboot it now and then, when `/var/run/reboot-required` exists: the
stack comes back by itself (`restart: unless-stopped`).
