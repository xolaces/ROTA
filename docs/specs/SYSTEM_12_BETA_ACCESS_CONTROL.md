# System 12 — Beta Access Control (RBAC + beta keys + admin tooling) — SPEC

*Drafted 2026-05-29 · Owner: Xolaces · Branch: `feature/system-12-beta-access-control`*

Server-only. No UI/Unity, no game-mechanic changes. Implement exactly as written; do not
redesign or add scope. If something is genuinely ambiguous or blocked, STOP and report —
never guess and never weaken a security rule.

## Read first
`CLAUDE.md` (architecture + security rules are NON-NEGOTIABLE), `docs/ROTA_Function_Reference.md`,
and `docs/DESIGN_NORTHSTAR.md` §6 (governance). Follow the existing style of files under
`src/ROTA.Infrastructure/Persistence/Configurations/` and `.../Repositories/`. Register new
services/repos where `AddRotaServices` lives.

## Baseline to preserve
156 unit + 1 integration test green, **0 warnings**. Architecture rules: private setters +
domain methods only (no EF attributes); Fluent API only; thin controllers; snake_case; every
table has id/created_at/updated_at/is_deleted; every FK indexed; audit_log on every state
change; PlayerId from the verified JWT `sub` claim; no silent stubs (`// PHASE-2` deferred bits).

## Locked decisions
1. Roles = `[Flags]` enum on `Player` (one int column, bitwise). NO join table.
2. Admin tooling = admin-only REST endpoints + a bootstrap CLI, BOTH calling ONE shared service layer.
3. Beta keys = SINGLE-USE, enforced atomically.
4. One conventional commit per component A–F. (The `v0.2.0` tag is created later by the operator, not you.)

---

## Component A — Role system
1. New `src/ROTA.Domain/Enums/PlayerRoles.cs`:
   ```csharp
   [Flags]
   public enum PlayerRoles { None = 0, Player = 1 << 0, Moderator = 1 << 1, Admin = 1 << 2 }
   ```
2. `Player` entity: add `public PlayerRoles Roles { get; private set; } = PlayerRoles.Player;` and
   `public string DisplayName { get; private set; } = string.Empty;`. In `Create`, set
   `Roles = PlayerRoles.Player` and `DisplayName = username`. Add methods `GrantRole`,
   `RevokeRole` (bump UpdatedAt), `bool HasRole`, `UpdateDisplayName`. `RevokeRole` must NEVER
   remove `PlayerRoles.Player`.
3. `PlayerConfiguration`: map `roles` (int, `HasDefaultValue(PlayerRoles.Player)`), `display_name`
   (varchar(48), required).
4. Migration `AddPlayerRolesAndDisplayName`: `roles int NOT NULL DEFAULT 1`, `display_name
   varchar(48) NOT NULL DEFAULT ''`; then raw SQL `UPDATE players SET display_name = username
   WHERE display_name = '';`. Create AND apply.
5. JWT `AuthService.GenerateAccessToken`: after existing claims, emit `new Claim(ClaimTypes.Role,
   flag.ToString())` for each set flag in `player.Roles` (skip `None`), plus `new Claim(
   "display_name", player.DisplayName)`.
6. `Program.cs`: change `AdminOnly` to `policy.RequireAuthenticatedUser().RequireAssertion(ctx =>
   ctx.User.IsInRole(nameof(PlayerRoles.Admin)) || adminPlayerIds.Contains(ctx.User.FindFirst(
   "sub")?.Value ?? "", StringComparer.OrdinalIgnoreCase))` — role claim primary, config allowlist
   kept as a documented break-glass fallback. Add a `ModeratorOrAdmin` policy (Moderator OR Admin).
7. Tests: role get/grant/revoke; `RevokeRole` won't strip `Player`; JWT carries expected role claims.

## Component B — Beta keys + registration gate
1. New entity `src/ROTA.Domain/Entities/BetaKey.cs`: `Id, Key, CreatedByPlayerId (Guid?),
   IsRedeemed, RedeemedByPlayerId (Guid?), RedeemedAt (DateTimeOffset?), CreatedAt, UpdatedAt,
   IsDeleted`. Factory `Create(string key, Guid? createdBy)`; `Redeem(Guid playerId)` throws if
   already redeemed.
2. EF config + migration `AddBetaKeys`: table `beta_keys`, UNIQUE index on `key`, index on
   `redeemed_by_player_id`. snake_case. Create AND apply.
3. `IBetaKeyRepository` + impl: `CreateAsync`, `GetByKeyAsync`, `ListAsync(int take)`, and the
   critical **`Task<bool> TryRedeemAsync(string key, Guid playerId)`** = ONE atomic conditional
   update `UPDATE beta_keys SET is_redeemed=true, redeemed_by_player_id=@p, redeemed_at=now(),
   updated_at=now() WHERE key=@k AND is_redeemed=false AND is_deleted=false` returning
   `rowsAffected == 1`. This is the single-use race guard.
4. `IBetaKeyService` + `BetaKeyService`: `GenerateAsync(Guid? actorPlayerId, int count)` — key
   format `ROTA-XXXX-XXXX-XXXX`, Crockford base32 (no 0/O/1/I/L) from `RandomNumberGenerator`;
   audited `BetaKeyGenerated`. `ValidateAndRedeemAsync(string key, Guid newPlayerId)` → `TryRedeemAsync`.
5. `RegisterRequest`: add `public string BetaKey { get; set; } = string.Empty;`. Validator: NotEmpty
   when the gate is enabled.
6. `AuthService.RegisterAsync`, ZERO side effects on rejection: read `BetaGate:Enabled` (default
   `true`). When enabled, wrap registration in a DB transaction — claim the key via
   `TryRedeemAsync(key, newPlayerId)` FIRST; if false → audit `RegisterFailed`, roll back, return
   null. Then create the player and commit. On any exception, roll back so the key is freed. When
   disabled, behave exactly as today.
7. Tests: invalid key → null + no player created + key not consumed; valid key → success + redeemed;
   double-redeem (mock true then false) → exactly one success. ONE integration test (Testcontainers):
   two concurrent `RegisterAsync` with the same key → exactly one created player.

## Component C — Admin seed
1. New `src/ROTA.Infrastructure/Seeding/SeedData.cs`, `static Task EnsureAdminAsync(IServiceProvider
   sp)`: if no non-deleted player named `Xolaces`, read `Seed:AdminPassword` (REQUIRED — if missing,
   log a warning and return; NEVER hardcode a default) and `Seed:AdminEmail` (default
   `xolaces@rota.dev`); create username `Xolaces`, `DisplayName = "DEV_Xolaces"`, `Roles = Player |
   Admin`, BCrypt(12) password. Bypass the beta gate. Idempotent.
2. `Program.cs`: after `var app = builder.Build();`, before `app.Run();`, scope + `await
   SeedData.EnsureAdminAsync(...)`.
3. Tests: creates once; second call no-op; missing password → no creation, no throw.

## Component D — Admin service + REST endpoints
1. `IPlayerRepository`: add `Task<Player?> FindByUsernameAsync(string)` and `Task<int>
   CountByRoleAsync(PlayerRoles role)` (`WHERE (roles & @r) = @r AND NOT is_deleted`). Implement.
2. `AdminActionResult` (Success, FailureReason?). `IAdminService` + `AdminService`:
   - `GrantRoleAsync(Guid actorId, string targetUsernameOrId, PlayerRoles role)`, `RevokeRoleAsync(...)`.
   - Resolve target by GUID if parseable else username.
   - Re-verify actor is Admin from the DB — EXCEPT `actorId == Guid.Empty` (system/CLI actor).
   - Guards: target exists; cannot grant/revoke `Player`; cannot revoke `Admin` from the last admin
     (`CountByRoleAsync(Admin) <= 1` → fail "cannot demote the last admin").
   - On successful revoke of `Admin`/`Moderator`: revoke target's active refresh tokens
     (`IRefreshTokenRepository`).
   - Audit `RoleGranted`/`RoleRevoked`, ResultSummary `actor=… target=… role=… before=… after=…`.
3. `AdminController` `[Authorize(Policy="AdminOnly")]`, thin, actor id from JWT `sub`:
   - `POST /api/admin/players/{idOrUsername}/roles/grant`  `{ "role": "Moderator" }` → 200/400/403/404
   - `POST /api/admin/players/{idOrUsername}/roles/revoke` → same
   - `POST /api/admin/beta-keys` `{ "count": 1 }` → 200 `{ "keys": [...] }`
   - `GET  /api/admin/beta-keys` → list with redeemed status
   FluentValidation (role parses to a defined enum value; count 1–100).
4. Tests: grant adds flag + audits; revoke removes flag + audits + revokes sessions; last-admin guard;
   non-admin actor fails; username AND GUID resolution; beta-key gen returns N + audits.

## Component E — Bootstrap CLI
1. New `src/ROTA.Api/AdminCli.cs`: `static bool IsCommand(string)` + `static Task<int>
   RunAsync(string[] args, WebApplicationBuilder builder)`. In `Program.cs`, after services are
   registered but before building the web host: `if (args.Length > 0 && AdminCli.IsCommand(args[0]))
   return await AdminCli.RunAsync(args, builder);` — scope, execute, return exit code, do NOT start
   Kestrel.
2. Commands (reuse `IAdminService`/`IBetaKeyService`/`SeedData`; NO duplicated logic):
   `seed-admin`; `gen-beta-key [count]` (actor `Guid.Empty`, print each key); `promote <user|uid>
   <Role>`; `demote <user|uid> <Role>`. Non-zero exit on failure. Last-admin guard still applies.
3. Document the CLI in `CLAUDE.md` run commands.

## Component F — Docs + version
1. XML `///` docs on every new public type/member.
2. `CLAUDE.md`: add "System 12 — Beta Access Control (BETA)" (migrations, new test count,
   `BetaGate:Enabled`, `Seed:AdminPassword`/`Seed:AdminEmail`, CLI commands). Remove "Player.IsAdmin
   DB column" from the Phase-2 backlog (superseded).
3. `changelog.md`: dated entry (2026-05-29), A–E, in the file's existing teaching-note voice.
4. `docs/ROTA_Function_Reference.md`: add the new interfaces, controller, entity, enum, endpoints.

## Definition of done
- `dotnet build` 0 errors / **0 warnings**; `dotnet test` all prior 156 unit + 1 integration green
  plus the ~20+ new unit tests and the new beta-key concurrency integration test.
- Six commits A–F, conventional style, each trailer:
  `Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>`
- Migrations `AddPlayerRolesAndDisplayName` and `AddBetaKeys` created AND applied to the dev DB.
- No tag, no push.
