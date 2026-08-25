# ROTA — Deployment (T66)


> **Three ops documents, three audiences.** Keep them that way -- merging them would help nobody.
> - [`OPERATIONS.md`](OPERATIONS.md) -- working on ROTA locally: services, secrets, build/test, CLI.
> - [`DEPLOYMENT.md`](DEPLOYMENT.md) -- how a deployment is configured, host-agnostic. **Canonical for
>   production migrations.**
> - [`BETA_DEPLOY.md`](BETA_DEPLOY.md) -- the concrete walkthrough for the live VPS + Docker + Caddy.
>
> Where they overlap, one of them is canonical and the others link to it. Duplicated procedure is how
> these three drifted apart in the first place.

Host-agnostic production artifacts. Hosting provider is deliberately undecided; everything here
works on any Docker host (VPS, Fly.io, Railway, Azure/AWS container services) or bare `dotnet`.

## Artifacts

| File | Purpose |
|---|---|
| `Dockerfile` | Multi-stage image for ROTA.Api (SDK build → slim aspnet runtime, non-root, port 8080) |
| `.dockerignore` | Keeps tests/docs/secrets out of the build context |
| `src/ROTA.Api/appsettings.Production.json` | Production logging + safe defaults — **no secrets** |
| `docker-compose.prod.yml` | Reference single-host stack (api + postgres + redis) |
| `.github/workflows/ci.yml` | CI: build + unit tests + migration gate (T67) |

## Configuration model

All secrets are **environment variables** (ASP.NET `__` form). Nothing secret is in the repo or image.

Required:

| Env var | Meaning |
|---|---|
| `ConnectionStrings__DefaultConnection` | Postgres connection string |
| `ConnectionStrings__Redis` | Redis connection string (`host:6379,password=...`) |
| `Jwt__PrivateKey` | RS256 private key PEM (token signing) |
| `Jwt__PublicKey` | RS256 public key PEM (token verification) |
| `Jwt__Issuer` / `Jwt__Audience` | Token issuer/audience strings |
| `Seed__AdminPassword` | First-boot admin seed (required by `EnsureAdminAsync`) |

Optional:

| Env var | Default | Meaning |
|---|---|---|
| `Email__Username` / `Email__Password` | empty | SMTP creds (Gmail app password). Unset → sends skip, rows still persist |
| `Email__Enabled` | `true` | Master email switch |
| `BetaGate__Enabled` | `true` | Beta-key requirement on registration |
| `Seed__AdminEmail` | `admin@rota.local` | Seeded admin email |
| `Admin__PlayerIds__0...` | — | Break-glass admin allowlist |
| `Auth__PasswordResetTokenMinutes` | `15` | T65 reset-code TTL |
| `ForwardedHeaders__Enabled` | `false` | Honour `X-Forwarded-For/-Proto` from trusted proxies |
| `ForwardedHeaders__TrustedProxies__0...` | — | Proxy IPs allowed to set forwarded headers |

Generate an RS256 key pair (PowerShell/openssl):

```
openssl genrsa -out jwt_private.pem 2048
openssl rsa -in jwt_private.pem -pubout -out jwt_public.pem
```

## Migrations — ALWAYS BEFORE the app starts

The app auto-migrates **only in Development**. For production, apply migrations explicitly per release:

```
# Option A — from a checkout with the SDK:
dotnet ef database update --project src/ROTA.Infrastructure --startup-project src/ROTA.Api
# (ConnectionStrings__DefaultConnection env var pointed at the prod DB)

# Option B — generate an idempotent SQL script in CI and run it with psql:
dotnet ef migrations script --idempotent -o migrate.sql --project src/ROTA.Infrastructure --startup-project src/ROTA.Api
psql "$PROD_CONNECTION" -f migrate.sql
```

### Verifying what production actually has

Applying migrations is documented above; knowing whether they *landed* was not. Run this before any
release, and any time a doc and the database seem to disagree:

```
psql "$PROD_CONNECTION" -f scripts/verify-prod-schema.sql
```

or, against the compose stack on the droplet:

```
docker exec -i rota-postgres-prod psql -U rota_user -d rota < scripts/verify-prod-schema.sql
```

It is read-only. Every section prints a `verdict` column; anything that is not `OK` means the database
is behind this repository. It checks migration history **and** the actual column types, because the
history table only records what EF was told to do -- a schema edited by hand can pass the first check
and fail the second.

The int -> bigint widenings are the ones that matter most. If any of those report
`NOT WIDENED -- OVERFLOW RISK`, section 5 shows how close the largest live value is to the int32
ceiling, which is the difference between a theoretical problem and an imminent one.

## Reference single-host deploy

```
cp .env.example .env        # fill in the production values (see the compose file header)
docker compose -f docker-compose.prod.yml up -d --build
```

- The API listens on **8080** (HTTP). Terminate TLS at a proxy (Caddy/nginx/cloud LB) in front of it,
  then set `ForwardedHeaders__Enabled=true` + the proxy's IP in `ForwardedHeaders__TrustedProxies__0`
  so per-IP rate limiting and audit IPs see the real client.
- Postgres/Redis have **no host port mappings** — only the API reaches them on the compose network.
- Health endpoint: `GET /health`.

## First-boot order (mirrors docs/OPERATIONS.md)

1. Apply migrations (above).
2. Start the API — it seeds the admin (`Seed__AdminPassword`) and the dev guild idempotently.
3. `dotnet run --project src/ROTA.Api -- gen-beta-key 25` (or via a one-off container) for beta keys.
4. Swagger is **dev-only**; production exposes only the API + `/health`.
