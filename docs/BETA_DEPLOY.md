# ROTA — Beta Deploy Runbook (Linux VPS + Docker + Caddy)

End-to-end, copy-paste steps to put a ROTA beta online: **server backend** on an Ubuntu VPS behind
HTTPS, and the **Windows client** built to point at it and shared as a direct download.

This is the hands-on runbook. For *what each artifact is* see [DEPLOYMENT.md](DEPLOYMENT.md); for the full
admin CLI / beta onboarding see [OPERATIONS.md](OPERATIONS.md). It does not duplicate them.

**Target stack:** Ubuntu 24.04 VPS · Docker Compose (`api` + `postgres` + `redis`) · Caddy reverse proxy
with automatic Let's Encrypt TLS · a domain you'll register below. Fresh DB — all migrations apply from
scratch.

> Conventions: replace `api.example.com` with your real subdomain and `SERVER_IP` with the VPS IP.
> Commands marked **[local]** run on your Windows dev machine (has the .NET SDK + Unity); everything else
> runs on the VPS over SSH.

---

## 0. What you need (≈ 20 min + ~$6/mo)

| Thing | Where | Cost |
|---|---|---|
| A VPS (2 GB RAM, Ubuntu 24.04) | Hetzner Cloud (CX22), DigitalOcean, or Linode | ~$5–7/mo |
| A domain | Porkbun, Cloudflare, or Namecheap | ~$10/yr |
| The .NET 10 SDK | already on your dev machine | — |
| Unity 6000.4.9f1 | already on your dev machine | — |

---

## 1. Register a domain (you don't have one yet)

1. Go to a registrar — **[Porkbun](https://porkbun.com)** or **[Cloudflare](https://dash.cloudflare.com)**
   are cheap with free WHOIS privacy. Search a name (anything you like, e.g. `playrota.com`), add to cart,
   pay. ~5 minutes.
2. You don't need anything fancy — just the domain. You'll add one DNS record in Step 5 once the VPS exists.

> A domain is what lets Caddy fetch a free, real TLS certificate automatically. Without it, login passwords
> and tokens would cross the wire unencrypted.

---

## 2. Create the VPS

1. Create an account at **[Hetzner Cloud](https://www.hetzner.com/cloud)** (cheapest) or DigitalOcean.
2. New project → new server: **Ubuntu 24.04**, smallest 2 GB plan (Hetzner CX22). Add your SSH public key
   during creation (paste `~/.ssh/id_ed25519.pub`; create one **[local]** with `ssh-keygen -t ed25519` if
   you don't have it). Create — note the public IP = `SERVER_IP`.
3. SSH in: `ssh root@SERVER_IP`.

### 2a. Minimal hardening (5 min, do it once)

```bash
# Create a non-root sudo user and copy your SSH key to it
adduser rota && usermod -aG sudo rota
rsync --archive --chown=rota:rota ~/.ssh /home/rota
# Firewall: allow SSH + HTTP + HTTPS only
ufw allow OpenSSH && ufw allow 80 && ufw allow 443 && ufw --force enable
```

Log out and back in as the new user: `ssh rota@SERVER_IP`. (Optional but recommended: disable root SSH +
password auth in `/etc/ssh/sshd_config` → `PermitRootLogin no`, `PasswordAuthentication no`, then
`sudo systemctl restart ssh`.)

---

## 3. Install Docker

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER && newgrp docker   # run docker without sudo
docker --version && docker compose version        # sanity check
```

---

## 4. Get the code onto the server

```bash
sudo mkdir -p /opt/rota && sudo chown $USER /opt/rota && cd /opt/rota
git clone <your-rota-repo-url> .          # or: scp the repo up if it's not on a remote
# You need at least: Dockerfile, docker-compose.prod.yml, src/  (the build context)
```

> If the repo has no remote yet, from your dev machine **[local]**:
> `scp -r C:\Users\xolac\OneDrive\Documentos\Projects\ROTA\* rota@SERVER_IP:/opt/rota/` (slow but fine once).

---

## 5. Point the domain at the server

In your registrar's DNS panel, add a single **A record**:

| Type | Host/Name | Value | TTL |
|---|---|---|---|
| A | `api` | `SERVER_IP` | default |

That maps `api.example.com → SERVER_IP`. DNS takes a few minutes to propagate; verify with
`dig +short api.example.com` (should print `SERVER_IP`). **Caddy can't get a certificate until this
resolves**, so do this before Step 8.

---

## 6. Secrets

The API normalizes single-line PEM keys (literal `\n` → real newlines, via `PemKey.Normalize`), so **every
secret — including the RS256 keys — fits in one `.env` file**. Nothing secret is committed (`.env` is
gitignored).

### 6a. Generate the RS256 key pair (on the VPS)

```bash
cd /opt/rota && mkdir -p keys && chmod 700 keys
openssl genrsa -out keys/jwt_private.pem 2048
openssl rsa -in keys/jwt_private.pem -pubout -out keys/jwt_public.pem
chmod 600 keys/*.pem
```

### 6b. Create `.env` from the template

```bash
cd /opt/rota && cp .env.prod.example .env && chmod 600 .env
# Append the keys as single lines (awk turns each multi-line .pem into the \n form the API un-escapes):
{ printf 'JWT_PRIVATE_KEY='; awk 'NR>1{printf "\\n"}{printf "%s",$0}' keys/jwt_private.pem; echo;
  printf 'JWT_PUBLIC_KEY=';  awk 'NR>1{printf "\\n"}{printf "%s",$0}' keys/jwt_public.pem;  echo; } >> .env
```

Then edit `.env` and set `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `SEED_ADMIN_PASSWORD` (strong values via
`openssl rand -base64 24`). `SEED_ADMIN_PASSWORD` is your first admin login — username `Owner`, email
`admin@rota.local` by default (both overridable via config).

> Prefer not to single-line the keys? The file approach also works: leave them as `.pem` files and
> `export JWT_PRIVATE_KEY="$(cat keys/jwt_private.pem)"` (+ the public key) in a `source`-d script before each
> compose command. The single-line `.env` is simpler and is what the rest of this runbook assumes.

---

## 7. Add Caddy + TLS to the stack

Caddy fronts the API on 80/443 and auto-provisions the certificate. The API stays internal (no public port).

### 7a. Caddyfile

```bash
cat > /opt/rota/Caddyfile <<'EOF'
api.example.com {
    reverse_proxy api:8080
}
EOF
```

### 7b. Caddy compose override

```bash
cat > /opt/rota/docker-compose.caddy.yml <<'EOF'
services:
  caddy:
    image: caddy:2-alpine
    container_name: rota-caddy
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data
      - caddy_config:/config
    depends_on:
      - api
  api:
    environment:
      ForwardedHeaders__Enabled: "true"
volumes:
  caddy_data:
  caddy_config:
EOF
```

### 7c. Stop the API publishing a public port

Edit `docker-compose.prod.yml` and **delete the api `ports:` block** (the two lines `ports:` and
`- "8080:8080"`). Caddy reaches the API over the compose network as `api:8080`; it no longer needs a host
port. (Leaving it exposed would let people hit plain HTTP and bypass TLS.)

> The `api:8080` you'll launch with `-f docker-compose.prod.yml -f docker-compose.caddy.yml` so both files
> merge. Per-IP rate-limiting/audit IPs: with Caddy in front, enable forwarded headers (done in 7b). If audit
> logs later show the proxy IP instead of the real client, add Caddy's container IP to
> `ForwardedHeaders__TrustedProxies__0` (find it with `docker inspect rota-caddy | grep IPAddress`). Not a
> launch blocker.

---

## 8. Migrate the database (fresh DB → full history)

The app only auto-migrates in Development, so apply migrations explicitly. Generate an idempotent SQL script
on your dev machine (offline, no DB needed) and apply it on the server.

**[local]** generate + upload:
```powershell
dotnet ef migrations script --idempotent -o migrate.sql --project src/ROTA.Infrastructure --startup-project src/ROTA.Api
scp migrate.sql rota@SERVER_IP:/opt/rota/
```

On the VPS — start only the data stores, then apply:
```bash
cd /opt/rota
docker compose -f docker-compose.prod.yml up -d postgres redis
sleep 5   # let postgres pass its healthcheck
docker compose -f docker-compose.prod.yml exec -T postgres \
  psql -U rota_user -d rota < migrate.sql
```

Re-run anytime for future releases (idempotent = safe to re-apply).

---

## 9. Launch + seed

```bash
cd /opt/rota
docker compose -f docker-compose.prod.yml -f docker-compose.caddy.yml up -d --build
```

- The API seeds the admin (`SEED_ADMIN_PASSWORD`) and the dev guild on first boot, automatically.
- Generate beta keys (the CLI runs as a one-off container):
  ```bash
  docker compose -f docker-compose.prod.yml run --rm api dotnet ROTA.Api.dll gen-beta-key 25
  ```
  Each prints as `ROTA-XXXX-XXXX-XXXX` — hand these to testers.
- Logs: `docker compose -f docker-compose.prod.yml logs -f api`

---

## 10. Smoke test

```bash
curl https://api.example.com/health           # → healthy; confirms TLS + API + DNS all work
```
Then register a player against a beta key:
```bash
curl -X POST https://api.example.com/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"t@t.com","username":"tester1","password":"Passw0rd!","betaKey":"ROTA-...","acceptedTermsVersion":"<current>"}'
```
(If it complains about the terms version, GET `https://api.example.com/api/legal/terms` for the current one.)

---

## 11. Build & ship the Windows client

The client picks its backend at startup with this precedence: **`ROTA_BASE_URL`/`ROTA_USE_MOCK` env vars →
`rota-config.json` next to the `.exe` → the `AppBootstrap` Inspector defaults.** So you can either bake the
target into the build, or ship a default build and point it with a config file (no rebuild to retarget).

1. **[local]** Set the target one of two ways:
   - *Bake it in:* in Unity, open the main scene → select the `AppBootstrap` GameObject → Inspector →
     uncheck **Use Mock**, set **Base Url** = `https://api.example.com`.
   - *Or ship a config file:* leave the build as-is and drop a `rota-config.json` next to `ROTA.exe`:
     ```json
     { "useMock": false, "baseUrl": "https://api.example.com" }
     ```
     (chat/SignalR derives `wss://` from the scheme automatically.)
2. Build the standalone:
   ```powershell
   cd C:\Dev\ROTA.Client6
   .\tools\build-client.ps1 -Version 0.4.0      # → dist\ROTA-win64-0.4.0.zip
   ```
3. **Distribute**: upload the zip anywhere with a direct link (Google Drive, itch.io, a release asset).
   Testers unzip and run `ROTA.exe`. (If you went the config-file route, include `rota-config.json` in the zip.)
   - **Windows SmartScreen** will warn that the `.exe` is from an unknown publisher (it's unsigned). Testers
     click *More info → Run anyway*. Tell them this up front. (Code-signing removes the warning later — a
     paid cert, post-beta.)
4. **Tester flow**: launch → Register with the email + a beta key you gave them → play.

---

## 12. Day-2 operations

- **Update to a new release:**
  ```bash
  cd /opt/rota && git pull
  # if the release added migrations: regenerate migrate.sql [local], scp, re-apply (Step 8)
  docker compose -f docker-compose.prod.yml -f docker-compose.caddy.yml up -d --build
  ```
- **Backup the database (do this before each release + on a daily cron):**
  ```bash
  docker compose -f docker-compose.prod.yml exec -T postgres \
    pg_dump -U rota_user rota | gzip > ~/rota-backup-$(date +%F).sql.gz
  ```
- **Restart / stop:** `docker compose -f docker-compose.prod.yml -f docker-compose.caddy.yml restart`
  (or `down` to stop; data persists in named volumes).

---

## Pre-beta checklist

- [ ] DNS A record resolves; `https://api.example.com/health` returns healthy with a valid cert.
- [ ] `SEED_ADMIN_PASSWORD`, DB + Redis passwords are strong and only in `/opt/rota/.env` (chmod 600).
- [ ] Beta keys generated and distributed; `BETA_GATE_ENABLED=true` so registration is gated.
- [ ] A database backup taken (Step 12) and a daily cron set.
- [ ] **Legal text** (`content/legal/*.md`, T68) replaced — it's placeholder. Required before real signups.
- [ ] **Known beta-grade item (acceptable for a trusted direct-download beta, fix before public launch):**
      the `.exe` is unsigned (SmartScreen warning). (Client tokens are now AES-encrypted at rest under a
      device-bound key — `TokenStore` — so plaintext storage is no longer a gap; a platform keystore is the
      eventual hardening.)
- [ ] Client points at the live server — **Use Mock OFF** + correct `https://` Base Url (Inspector or
      `rota-config.json`). Hit `/health` from the built client, or check the startup log line.
