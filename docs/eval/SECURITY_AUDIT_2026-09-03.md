# Security audit — 2026-09-03

Pre-beta review of the ROTA API. Two halves: reading the code, and attacking a running instance on
`localhost:5035`. The live half found the most severe issue, which the code review had walked past.

Scope: the HTTP API and SignalR hub. **Not covered** — see Not tested, at the bottom. This is not a
clean bill of health for anything in that list.

---

## Fixed in this pass

### 1. The API issued tokens it would then reject — `b8191c3`

**Severity: critical. The entire authenticated API was unusable.**

There was no `Jwt` section in `appsettings.json` at all, so `Jwt:Issuer` and `Jwt:Audience` were
undefined outside a deployment supplying them as environment variables.

Both sides read the same two settings, so a missing one does not *disagree* — it disables the claim
on both sides at once, in opposite directions:

| | reads | effect when unset |
|---|---|---|
| `AuthService` (signing) | `Jwt:Issuer` / `Jwt:Audience` | passes null → claim omitted from the token |
| `Program.cs` (validating) | same two keys | `ValidateIssuer`/`ValidateAudience` stay **true** |

Every login returned 200 with a real, correctly-signed token carrying `sub` and the right roles.
Every authenticated request that followed returned 401, with nothing logged to say why.

Code review missed this because both files are individually correct. It surfaced only when a decoded
live token turned out to have no `iss` and no `aud`.

Issuer and audience are identifiers rather than secrets, so they now ship with defaults and the API
works from a clean checkout with only the signing keys supplied. A startup guard rejects a blank
value for either.

**Verified:** before, `/api/players/me` and `/api/admin/beta-keys` both 401 with a fresh admin token;
after, both 200, absent token still 401, token carries `iss=rota-api aud=rota-client`.

> **Production is probably unaffected** — `appsettings.Production.json` documents `Jwt__Issuer` and
> `Jwt__Audience` as env-supplied, so a droplet that sets them was always fine. This bit local dev and
> would bite any deploy that missed them, silently.

### 2. No response security headers — `15daf86`

The API set none. HSTS was configured and CORS was a correct explicit allowlist, which is what made
the absence easy to miss: both govern who may *talk* to the API, neither says what a browser may *do*
with a response it already holds.

Now set on every response: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
`Referrer-Policy: no-referrer`, a `Permissions-Policy` denying camera/mic/geo/USB, and outside
Development a strict `Content-Security-Policy: default-src 'none'` with `frame-ancestors`, `base-uri`
and `form-action` all `'none'`.

The middleware runs early, so headers also land on the 401, the 429, the 403 from the ban gate and
the 500 from the global handler — the responses a scanner reaches first, and the ones headers applied
inside MVC would miss entirely.

Four integration tests assert against the **pipeline**, so deleting the registration fails them too.
Verified by neutering the registration and watching three of four fail.

### 3. ForwardedHeaders misconfiguration was silent — `a7abe05`

Both ways of getting it wrong were silent, which is why the pre-existing warning was not enough.

Unset behind a reverse proxy, every request carries the proxy's address: the per-IP rate-limit bucket
collapses into one bucket shared by the whole internet, and `audit_log.ip_address` stops identifying
anyone. Nothing errors — the limiter still returns 429s on schedule, so it reads as working while
protecting nobody and throttling everybody.

Set to `true` with an empty `TrustedProxies` is worse. `KnownProxies` and `KnownIPNetworks` are
deliberately cleared, so that combination trusts nothing and honours no header — behaving exactly
like disabled while reading as configured.

Both now throw at startup, outside Development, with a message naming the fix. An explicit `false`
still boots, so a genuinely direct-exposed deploy is unaffected.

---

## Tested and found sound

Each of these was probed, not assumed.

| Area | Result |
|---|---|
| **Class-level authorization** | Every controller carries `[Authorize]`. Only `AuthController` (register/login/refresh) and `LegalController` (terms/privacy) are open, both correctly. |
| **Admin/mod role gating** | `AdminOnly` on Admin/Dev/Ops/GauntletAdmin/MasteryAdmin, `ModeratorOrAdmin` on Moderation. |
| **Privilege escalation** | A real low-privilege account (`roles: Player`) got **403** on all four admin/mod endpoints tried. |
| **IDOR — caller identity** | No controller accepts a player id from route, query or body. Identity is always the JWT `sub`. |
| **IDOR — SignalR** | `JoinRaid` and `SendRaidMessage` are participant-gated; guild chat resolves the guild server-side from verified identity and fans out to the current roster. |
| **SQL injection** | No interpolated or concatenated SQL anywhere. The one `$"..."` SQL string is `FromSqlInterpolated`, which parameterises; its inputs are a Guid and an enum. No user-controlled `ORDER BY`. |
| **JWT handling** | RS256 explicitly whitelisted (blocks algorithm-confusion and `alg:none`), `ClockSkew` zero, issuer/audience/lifetime/signing key all validated, `MapInboundClaims` off deliberately. |
| **Mass assignment** | `RegisterRequest` carries only username/email/password/betaKey/termsVersion. `Player.Roles` has a private setter and `Create` hardcodes `PlayerRoles.Player`. No path to self-granting a role. |
| **CORS** | Explicit origin allowlist, no `AllowAnyOrigin`, methods restricted outside Development. Fails closed on an empty list. |
| **User enumeration** | Unknown email and known-email-wrong-password both return **401** with an identical `{"message":"Invalid credentials."}`. |
| **Rate limiting** | Auth bucket (10/60s per IP) verified live: nine through, then 429 with a correct `Retry-After: 35`. It works well enough that it locked the audit out of its own test account. |
| **Registration hardening** | Beta key required, username charset restricted, reserved staff handles blocked, password complexity enforced server-side, current terms version required. |
| **Password reset** | CSPRNG code over a 30-char confusable-free alphabet (~2^39), hashed at rest, 15-minute TTL, one live code at a time, audit-logged. Brute force is infeasible inside the window. |
| **Stat allocation abuse** | Negative → 400. Zero → 400. `int.MaxValue` → 400 (capped at 100,000,000). More than owned → refused with the balance. Unknown stat type → 400. |

---

## Not tested — do not read this document as covering them

Ranked by how much they would worry me before opening a beta.

1. **Concurrent gem double-spend.** Your own notes carry this as an open defect. It needs a funded
   account and parallel in-flight requests; the audit ran out of budget before setting that up. The
   tri-state spend (Charged / AlreadyProcessed / InsufficientBalance) and the advisory-lock discipline
   suggest it is handled, but *suggests* is not *tested*. **This is the highest-value remaining test.**
2. **Idempotency replay across every economy seam.** Raid loot, quest rewards and shop purchases each
   have a referenceId scheme. Only the zone-rerun one was examined this session, and it was found
   broken (fixed separately in `17bbc50`) — which is reason to check the rest rather than assume.
3. **The Gauntlet shop gem bundle.** Known contradiction: its idempotency reference is constant per
   account, so a second purchase charges nothing, grants nothing and returns SUCCESS. A fix was
   drafted and deliberately reverted because the spec and the enum disagree. Still an open owner
   decision, and it is an economy exploit until it is settled.
4. **Ban and mute enforcement end to end.** The gate exists in the pipeline and the derived
   `IsBanned` check was previously confirmed correct, but no live test drove a banned account against
   a protected endpoint.
5. **Refresh token rotation and replay detection.** The repository and audit trail exist. Not probed:
   whether a used refresh token is genuinely rejected, and whether reuse revokes the family.
6. **Oversized and malformed payloads.** No test of request body limits, deep JSON nesting, or
   duplicate keys.
7. **The Unity client.** Entirely out of scope. It runs in MOCK mode by default, which is its own
   finding (backlog R0) but not a security one.
8. **`Server: Kestrel`** is disclosed on every response. Minor — no version leaks — but free to remove.

---

## Left behind on the local instance

- Account `pentest_low` / `pentest_low@rota.local`, role Player, created via a minted beta key.
- Two unredeemed beta keys.
- Roughly twenty failed logins in `audit_log` against `admin@rota.local` and a probe address, from
  the enumeration and rate-limit tests.

All of it is local-only, on a Postgres volume created fresh today. Delete the account before beta or
wipe the volume; either is fine.
