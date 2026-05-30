# ROTA — Operations & Tooling Runbook

*Last updated 2026-05-29 · Covers Systems 1–12 (v0.2.0)*

Everything an operator/developer needs to build, test, run, migrate, and administer the
ROTA backend. All commands are run from the **repository root** unless noted.

---

## 1. Local development environment

### 1.1 Backing services (PostgreSQL + Redis)
```bash
docker-compose up -d        # start postgres + redis (+ pgAdmin)
docker ps                   # verify: rota-postgres (healthy), rota-redis (healthy)
docker-compose down         # stop (add -v to also drop volumes / wipe data)
```
- PostgreSQL → `localhost:5432`, db `rota_dev`
- Redis → `localhost:6379` (password-protected)
- pgAdmin → `localhost:5050`

### 1.2 Secrets (ASP.NET user-secrets, never committed)
The API reads secrets from the user-secrets store (project `src/ROTA.Api`). Set them once:
```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=rota_dev;Username=rota_user;Password=<pw>" --project src/ROTA.Api
dotnet user-secrets set "ConnectionStrings:Redis"             "localhost:6379,password=<pw>" --project src/ROTA.Api
dotnet user-secrets set "Jwt:PrivateKey"                      "<RS256 private key PEM>" --project src/ROTA.Api
dotnet user-secrets set "Jwt:PublicKey"                       "<RS256 public key PEM>" --project src/ROTA.Api
dotnet user-secrets set "Seed:AdminPassword"                  "<admin password>" --project src/ROTA.Api
dotnet user-secrets list --project src/ROTA.Api               # review
```
> **Note:** integration tests are hermetic against `Seed:AdminPassword` — having it set will
> not break `dotnet test` (the test fixtures neutralize the startup seeder).

---

## 2. Build & test

```bash
dotnet build                              # whole solution; must be 0 warnings, 0 errors
dotnet test                               # all tests (unit + integration)
dotnet test tests/ROTA.UnitTests          # unit only (fast, no Docker)
dotnet test tests/ROTA.IntegrationTests   # integration only (needs Docker; uses Testcontainers)
```
- Current baseline: **186 unit + 7 integration = 193 passing, 0 warnings.**
- Integration tests spin their **own** ephemeral Postgres/Redis via Testcontainers — they do
  **not** use the docker-compose dev services, but Docker must be running.

---

## 3. Database migrations

EF Core migrations live in `src/ROTA.Infrastructure`. Always pass both `--project` (where
migrations live) and `--startup-project` (the API, which has the connection string).

```bash
# Create a new migration
dotnet ef migrations add <Name> --project src/ROTA.Infrastructure --startup-project src/ROTA.Api

# Apply pending migrations to the configured database
dotnet ef database update --project src/ROTA.Infrastructure --startup-project src/ROTA.Api

# Inspect
dotnet ef migrations list --project src/ROTA.Infrastructure --startup-project src/ROTA.Api
```
Applied to date: `InitialCreate → … → AddRaidSize → AddPlayerRolesAndDisplayName → AddBetaKeys`.

> **DEPLOYMENT ORDER:**
> - **Development:** `dotnet run` auto-migrates on startup (`db.Database.MigrateAsync()` runs before
>   seeding). A fresh local DB bootstraps itself — no manual migration step required.
> - **Production:** migrations are **not** applied automatically. Run
>   `dotnet ef database update --project src/ROTA.Infrastructure --startup-project src/ROTA.Api`
>   (or the CLI `-- seed-admin`, which also auto-migrates) **before** starting the app.
>   If migrations are missing, startup fails with `42P01 relation "players" does not exist`.

---

## 4. Running the server

```bash
dotnet run --project src/ROTA.Api          # starts Kestrel (HTTP API + Swagger in Development)
```
- Swagger UI (Development only): `https://localhost:<port>/swagger`
- Health check: `GET /health`
- **Development:** pending EF Core migrations are applied automatically at startup before the
  seeder runs. A fresh local DB is fully self-bootstrapping — no manual migration step needed.
- **Production:** migrations must be applied manually before starting (see §3).
- On startup the app seeds the admin account (§6) if `Seed:AdminPassword` is set and the account
  doesn't already exist (idempotent).

---

## 5. Admin CLI

The API binary doubles as an admin CLI. When the first argument is a known command, it runs that
command against the service layer and exits **without starting Kestrel** (it auto-applies pending
migrations first). Pass CLI args after `--`.

```bash
# Create / ensure the bootstrap admin account (reads Seed:AdminPassword from secrets)
dotnet run --project src/ROTA.Api -- seed-admin

# Generate N single-use beta keys (default 1, max 100); prints each key
dotnet run --project src/ROTA.Api -- gen-beta-key 5

# Grant a role to a player (by username or GUID). Roles: Admin, Moderator
dotnet run --project src/ROTA.Api -- promote Xolaces Moderator
dotnet run --project src/ROTA.Api -- promote 3f2b...-guid Admin

# Revoke a role (last-admin demotion is blocked; demoting Admin/Moderator revokes their sessions)
dotnet run --project src/ROTA.Api -- demote SomeUser Moderator
```
- CLI actions run as the **system actor** (`Guid.Empty`) — no JWT needed, audited as such.
- Every role change and key generation writes to `audit_log`.

---

## 6. The admin / dev account

| Field | Value |
|---|---|
| Username | `Xolaces` (fixed) |
| Display name | `DEV_Xolaces` (fixed) |
| Email | `Seed:AdminEmail` (default `xolaces@rota.dev`) |
| Password | `Seed:AdminPassword` user-secret (BCrypt(12)-hashed; never hardcoded) |
| Roles | `Player | Admin` |

Created automatically on first server start, or on demand via `dotnet run --project src/ROTA.Api -- seed-admin`.
If `Seed:AdminPassword` is unset, seeding logs a warning and is skipped (never uses a default).

---

## 7. Admin REST API

All endpoints require a JWT whose player has the **Admin** role (policy `AdminOnly`; the
`Admin:PlayerIds` config list remains as a break-glass fallback). Roles ride in the JWT as
`role` claims, so a promoted player gets new privileges on their next token refresh (≤15 min)
or re-login; demotion of Admin/Moderator revokes the target's refresh tokens immediately.

| Method & path | Body | Purpose |
|---|---|---|
| `POST /api/admin/players/{idOrUsername}/roles/grant` | `{ "role": "Moderator" }` | Grant a role |
| `POST /api/admin/players/{idOrUsername}/roles/revoke` | `{ "role": "Moderator" }` | Revoke a role |
| `POST /api/admin/beta-keys` | `{ "count": 5 }` | Mint N single-use beta keys |
| `GET  /api/admin/beta-keys` | — | List keys with redemption status |

---

## 8. Configuration flags

| Key | Default | Effect |
|---|---|---|
| `BetaGate:Enabled` | `true` | When true, registration requires a valid unredeemed beta key. Set `false` for open registration at public launch (no code change). |
| `Seed:AdminPassword` | _(unset)_ | Required to create the admin account; unset ⇒ seeding skipped. |
| `Seed:AdminEmail` | `xolaces@rota.dev` | Email used for the seeded admin. |
| `Admin:PlayerIds` | _(empty)_ | Break-glass admin allowlist (GUIDs); grants `AdminOnly` even without the role claim. |

> **Migration policy (resolved):** the web host auto-migrates in Development only. Production
> keeps explicit operator-run migrations so multi-instance deployments can gate the schema update
> before rolling out new app instances. The CLI (`-- seed-admin`, `-- gen-beta-key`, etc.) also
> auto-migrates regardless of environment, as it is always a single-instance operator tool.

---

## 9. Beta onboarding workflow

1. **Admin** mints keys: `dotnet run --project src/ROTA.Api -- gen-beta-key 10` (or `POST /api/admin/beta-keys`).
2. **Distribute** one key per invitee (single-use).
3. **Player** registers: `POST /api/auth/register` with `{ username, email, password, betaKey }`.
   - Invalid/used key ⇒ rejected with zero side effects (the key is **not** consumed on a
     duplicate-username/email failure).
4. **Admin** elevates trusted players: `promote <username> Moderator`.

---

## 10. Release / versioning

- Conventional commits; one logical change per commit.
- Annotated, dated tags: `git tag -a v0.2.0 -m "v0.2.0 — Beta Access Control (2026-05-29)"`.
- `changelog.md` carries a dated entry per release; `CLAUDE.md` "Current build status" is the
  living state-of-the-world. This runbook documents the *how*; those document the *what/when*.
