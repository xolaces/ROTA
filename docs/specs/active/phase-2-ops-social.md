# Phase 2 — Ops & Social (Tickets 30–40) — HANDOFF / SPEC

Status: **NOT STARTED** — handoff for a fresh Claude Code session. Nothing in this batch is built.
Owner: Nathan (a.k.a. Xolaces / DEV_Xolaces). Operator email: **rotadevteam@gmail.com**.

This batch adds an **operator email + dashboard backbone (T39)** that several tickets route through,
plus social systems (chat, friends/PM), pinnacle/level polish, and small client fixes. Read this with
`CLAUDE.md` (architecture + security rules — NON-NEGOTIABLE) and `docs/ROTA_Function_Reference.md`.

---

## 0. Build order & dependency graph

**T39 is the foundation — build it FIRST.** These route their payloads through it:
- **T33** Pinnacle First-Claim → `PinnacleFirstClaim` email
- **T37** Player Report → `PlayerReport` email
- **T38** Bug/Ticket submission → `BugReport` / `GeneralTicket` email
- **T40** Moderation/punishment → `ModerationAction` email

Independent of T39 (can be done in any order, even first if you want quick wins):
- **T30** SP spend → immediate resource delta (backend)
- **T31** Profile scroll bar behind equipment panel (client)
- **T32** Pinnacle gates: mandatory class select + gem rewards (backend + client)
- **T34** Raid screen layout restructure (client)

Chat cluster (shares a real-time-delivery decision — see Open Decision #3):
- **T35** Localized raid chat → **T36** World chat → **T37** Friends/PM/social (+report via T39)

**Recommended sequence:**
1. **T39** (email service + log table + admin API + React dashboard).
2. Quick independent wins to keep momentum: **T30**, **T31**.
3. **T32** (reconcile pinnacle levels first — Open Decision #4), then **T33** (needs T39).
4. **T34** (raid layout — touches `RaidCombatView`, same file as the chat work).
5. Chat cluster: decide delivery (Open Decision #3) → **T35** → **T36** → **T37**.
6. **T38**, **T40** (both need T39; T40 also needs the Mute/rollback decision — #5).

---

## 1. Current repo state (start of this batch)

- **Backend** (.NET 10): `C:\Users\xolac\OneDrive\Documentos\Projects\ROTA` — git `main` @ `c80927f`,
  remote `origin` = github.com/xolaces/ROTA, **all pushed**. Docker (pg+redis) up; all migrations
  applied (latest `AddQuestEverCleared`). **478 unit + 35 integration green.** Branch
  `chore/drift-control-tooling` still UNMERGED (drift tooling + `/audit-dtos`).
- **Unity** (6.4 / 6000.4.9f1): `C:\Dev\ROTA.Client6` — git `master` @ `ff90627`, **local-only**.
  Use `git -C`. NEVER commit `Main.unity`'s `useMock` toggle, and ignore `Assets/_Recovery/`.
- **MOCK SEED:** `MockRotaApi._mockProfile.Level = 2498` (so milestones fire in playtest). Revert for
  normal mock testing if needed.

### Discipline (carry over — see SESSION_HANDOFF.md for the full list)
- Backend: branch off `main`; commit, **no co-author**; JWT `MSB3277` warnings are pre-existing →
  ignore; `dotnet test` green with Docker up. Stop any running `ROTA.Api` before building (DLL lock).
- Unity: branch off `master`; **Editor must be CLOSED** to headless-compile
  (`& '...\6000.4.9f1\Editor\Unity.exe' -batchmode -quit -nographics -projectPath 'C:\Dev\ROTA.Client6'
  -logFile <log>` via `Start-Process -Wait`; rm stale `Temp\UnityLockfile`; grep log for `error CS`).
  Commit incl new `.meta`, no co-author.
- **DTO drift is the recurring failure:** every new endpoint needs the client `Dtos.cs` mirror
  (camelCase JSON, numeric enums) AND a stateful `MockRotaApi` path (owner playtests in mock).

---

## 2. T39 — Core Email Infrastructure & Classification (FOUNDATION)

Self-contained operator notification backbone. **Persist first, send second, never block the request.**

### 2a. Where the code goes (per CLAUDE.md)
- **Interface:** `Application/Interfaces/IEmailService.cs` — `Task SendAsync(EmailMessage msg, CancellationToken ct)`.
- **Provider impl:** `Infrastructure/Email/SendGridEmailService.cs` behind `IEmailService`. Uses
  **SendGrid** transactional API. Creds (`SendGrid:ApiKey`, `SendGrid:FromAddress`) in Secret Manager /
  user-secrets — **never hardcoded** (mirror the JWT key + `Seed:AdminPassword` pattern).
- **Outbound log table** (the dashboard's source of truth): `outbound_emails`. snake_case; every table
  rule from CLAUDE.md applies — `id uuid DEFAULT gen_random_uuid()`, `created_at`, `updated_at`,
  `is_deleted`; FKs indexed. Suggested columns:
  - `email_type` (enum/text), `subject`, `recipient` (default rotadevteam@gmail.com)
  - `triggering_player_id` (uuid, nullable, FK→players, indexed), `triggering_system` (text, nullable)
  - `summary` (text), `detail` (jsonb — the structured payload), `metadata` (jsonb)
  - `send_status` (queued | sent | failed), `send_attempts`, `last_send_error`
  - `review_status` (pending | approved | dismissed), `reviewed_by` (uuid FK→players, nullable),
    `reviewed_at`
- **Repository:** `IOutboundEmailRepository` (Application/Interfaces) + impl in Infrastructure
  (Append + Get/list with filters + UpdateReviewStatus). Follow `AuditLogRepository` style.
- **Service:** `EmailNotificationService` (Application/Services) orchestrates: build payload → persist
  `outbound_emails` row (source of truth) → enqueue/attempt `IEmailService.SendAsync` → update
  send_status. **Every write also goes to `audit_log`** (CLAUDE.md: every state change). Email-send
  failure must be swallowed + logged, never breaking the caller (mirror `AuditLogMiddleware`).

### 2b. Classification (extensible enum — design so new types are one-line additions)
`EmailType { BugReport, PlayerReport, ModerationAction, PunishmentLog, PinnacleFirstClaim, GeneralTicket }`.
- Type appears in BOTH the **subject line** (e.g. `[ROTA][PlayerReport] …`) and the **body metadata**.
- Keep a single `EmailPayload` DTO in `ROTA.Shared/DTOs` (Type, Subject, Summary, Detail dict/object,
  TriggeringPlayerId?, TriggeringSystem?) so producers (T33/T37/T38/T40) all build the same shape.
- Document the payload schema + the `outbound_emails` schema together (a `## Schema` section in this
  file or a sibling doc) so new types extend cleanly.

### 2c. Admin API for the dashboard
- `AdminController` (already `[AdminOnly]`) or a new `OpsController [AdminOnly]`:
  - `GET /api/admin/emails?type=&reviewStatus=&page=` → paged list grouped/filterable by type.
  - `GET /api/admin/emails/{id}` → full detail expansion.
  - `POST /api/admin/emails/{id}/approve` and `/dismiss` → manual triage (no automated action without
    this). Writes `review_status` + `reviewed_by` (from JWT) + audit_log.
- DTOs mirrored to client only if the game UI needs them; the dashboard is a separate React app.

### 2d. The React dashboard (separate repo)
- **Lives in its own repository** (not inside this ROTA repo). Name TBD; e.g. `rota-ops-dashboard`.
- Reads the admin API. Groups entries **by type**; each row: type **badge**, **timestamp**, triggering
  **player/system**, **summary**, and an **expand** for detail. Approve/Dismiss buttons → triage.
- Auth: admin login (reuse RS256 JWT + AdminOnly). This is Nathan's triage tool — read + manual approve.

### 2e. Security / hygiene
- Player-triggered emails (T37/T38) MUST be rate-limited per-player AND per-IP (RateLimitMiddleware +
  Redis already exist) to prevent email flooding. Validate all player input with FluentValidation
  BEFORE the service layer.
- The `outbound_emails` log is operator-visible only (AdminOnly). Don't leak PII beyond what's needed.

---

## 3. Email-dependent tickets (need T39)

### T33 — Placeholder Magics + Pinnacle First-Claim logging
- Create placeholder **magic** slots for each pinnacle level above 2500 (named-but-unimplemented;
  store in `content/magics.json` alongside existing magic defs — confirm format). Design is decided by
  the first player to reach that level post-launch.
- On a first-claimant event: record (player id, timestamp, pinnacle level) and route a
  `PinnacleFirstClaim` email via T39. "First" must be enforced server-side & idempotently (a unique
  claim per pinnacle level — partial unique index like the gem ledger's idempotency pattern).
- Ties into T32 (pinnacle detection). Keep placeholder magics inert until approved via the dashboard.

### T37 — Friends, PM, Social moderation
- Friends (request/accept), private messaging between friends, block/unblock + block-list mgmt.
- **Report** from any profile/context menu → `PlayerReport` email via T39 (reporter, reported, reason
  enum, description). FluentValidation before service layer. **Rate limit: 5 reports/hour per player,
  15/hour per IP** (Redis, same pattern as auth lockout). Visible in dashboard the moment it's filed.
- PM needs the chat/messaging delivery infra (Open Decision #3). Reserved-name/role coloring applies —
  read memory `chat-roles-and-reserved-naming` (mod orange `+*`, dev red `DEV_xxxx`).

### T38 — Beta in-game bug/ticket submission
- In-game panel to file Bug Report or General Feedback → `BugReport`/`GeneralTicket` email via T39.
- Carry the submitter identity + a **game-state snapshot** (current screen, level, relevant stats) +
  the written description. FluentValidation before service layer. **Rate limit: 5 submissions/hour per
  player, 15/hour per IP** (same Redis pattern). Surfaces in dashboard under the right type.

### T40 — Moderation / punishment action logging
- On any punitive action (ban, mute, stat rollback, etc.) by a mod/dev → auto-log + `ModerationAction`
  email via T39 (acting mod, target, action, reason, timestamp). Creates a dispute-review audit trail.
- **Mute is in scope for T40.** Add `Player.Mute(DateTime expiresAt)` / `IsMuted` / `MuteExpiresAt` to
  the domain + a migration. Wire into `AdminService` (`MutePlayerAsync`), enforce in middleware (muted
  players can't send chat messages). **Stat-rollback stays PHASE-2** (too complex for this batch).
- **Use `ModerationAction` email type** (not a separate `PunishmentLog` — one type is enough for MVP).
- Hook points: `AdminService` (exists: role grant/revoke; last-admin guard; session revocation) and
  `Player.Ban` (exists). Extend `AdminController [AdminOnly]` with `POST /api/admin/players/{id}/mute`.
- Note: `audit_log` already records state changes; T40 ADDS the email routing + a structured punishment
  view so disputes have a reviewable trail in the dashboard.

---

## 4. Independent tickets

### T30 — SP spend grants immediate resource delta (backend)
- Target: `StatService.AllocateStatPointAsync` (Energy/Stamina cases). Today it raises the max via
  `IEnergyService.UpdateMaxAsync` but does NOT credit the gained amount to current.
- Wanted: raising max by N grants **+N current** (the delta), NOT a full refill. `MaxEnergy = 10 +
  EnergyInvestment`, so investing N raises max by N → credit N to current (capped at new max).
- Use the EnergyService primitives added last session: `RefillEnergyAsync(playerId, type, amount)`
  caps at max and is exactly the delta-grant. Order: `UpdateMaxAsync(newMax)` then
  `RefillEnergyAsync(amount)`. Add a unit test. (Contrast: level-up uses `RefillToMaxAsync` = full.)

### T31 — Profile scroll bar hidden behind equipment panel (client)
- Follow-up to T25 (left column slimmed). The left `ScrollView`'s scrollbar is obscured by the right
  (equipment) panel. Fix in `ProfileScreen.cs`: give the scrollbar its own contained space — panel
  width / padding-right on the left scroll, a scoped scroll region, or z-order so the right panel
  doesn't bleed over it. Headless-compile + eyeball (owner playtests visuals).

### T32 — Pinnacle gates: mandatory class select + gem rewards (backend + client)

**Class-gate levels** (mandatory class-select overlay + gem reward) = **`ConvergenceLevels` exactly**:
`2000, 5000, 7500, 10000, 15000, 25000` — hook into `ClassConfig.ConvergenceLevels`; do NOT add a
separate pinnacle list. Gem amounts: `5000→1500, 7500→2000, 10000→2500` (carried from original spec);
`2000, 15000, 25000` gem amounts **need to be confirmed with owner before building those tiers**.

**Intermediate milestones** (gem reward only, no class-gate overlay): `1000→250`, `2500→500`.

- At class-gate levels the class-selection overlay MUST appear and be mandatory — even when only one
  class is available, the player makes an explicit pick (no skip/defer).
- Gem rewards granted simultaneously, credited BEFORE dismissal.
- `GemTransactionType.PinnacleReward` + `referenceId = "pinnacle:gems:{playerId}:{level}"` (idempotent,
  via `IGemService.GrantGemsAsync`).
- Build on: client `ClassGate` (T15, already a mandatory blocking overlay) + `ClassService`
  (GetAvailableChoices / ComputeAutoAdvance / AssignClassAsync) + `StatService.GrantLevelUpPointsAsync`.

### T34 — Raid screen layout restructure (client, `RaidCombatView.cs`)
- Consolidate the stamina + hit controls into a compact area (currently spread out).
- Move the **combat log to bottom-left**; **overall damage dealt to top-right**.
- Damage leaderboard shows **5 by default, expands to 10** on interaction.
- Tight, purposeful layout. Same file as T23 (timer bar) and the chat work (T35) — sequence to avoid
  conflicts.

---

## 5. Chat cluster (shared delivery decision — Open Decision #3)

### T35 — Localized raid chat
- Chat panel scoped to the active raid instance (coordination, e.g. calling magic). **Ephemeral** — no
  persistence after the raid ends. Lives in `RaidCombatView`.

### T36 — World chat
- Persistent open/close button; **exclamation indicator** on the button when a message arrives while
  closed. Visible to all players.
- **Retention: last 100 messages in a Redis ring buffer** (`LPUSH` + `LTRIM`). Ephemeral — does not
  survive a Redis restart. No DB table needed.
- Apply reserved-name/role coloring (memory `chat-roles-and-reserved-naming`: mod orange `+*`, dev red).

### Delivery (applies to T35/T36 and T37 PM) — **SignalR decided**
- **Use SignalR** (WebSocket, true push). Add `Microsoft.AspNetCore.SignalR` (built into ASP.NET Core —
  no extra package). Wire a Hub per chat scope: `RaidChatHub` (scoped per raid instance) and
  `WorldChatHub` (global). Redis pub/sub (`StackExchange.Redis`, already present) backs both hubs for
  multi-instance scale. No real-time infra exists today — T35 is the greenfield first pass.

---

## 6. DECISIONS — all resolved (2026-06-06)

| # | Topic | Decision |
|---|-------|----------|
| 1 | Email provider (T39) | **SendGrid** — API key in Secret Manager (`SendGrid:ApiKey`, `SendGrid:FromAddress`) |
| 2 | Dashboard hosting (T39) | **Separate repo** (e.g. `rota-ops-dashboard`); authenticates via RS256 JWT + AdminOnly |
| 3 | Chat delivery (T35/36/37) | **SignalR** (WebSocket). `RaidChatHub` + `WorldChatHub`; Redis pub/sub backplane |
| 4 | Pinnacle gates (T32/T33) | **ConvergenceLevels win** (2000/5000/7500/10000/15000/25000 = class gates). 1000/2500 = gem-only milestones. Gem amounts at 2000/15000/25000 still TBD — confirm before building those tiers |
| 5 | Mute/rollback (T40) | **Mute in scope** (`Player.Mute`, `MuteExpiresAt`, `AdminService.MutePlayerAsync`). Stat-rollback PHASE-2. Use `ModerationAction` email type (not separate `PunishmentLog`) |
| 6 | Rate limits (T37/T38) | **5/hour per player, 15/hour per IP** (Redis, same pattern as auth lockout) |
| 7 | World-chat retention (T36) | **100-message Redis ring buffer** (`LPUSH` + `LTRIM`). Ephemeral — no DB table |

---

## 7. What already exists to build on (don't reinvent)

- **Resources:** `IEnergyService` — `GetCurrentEnergyAsync / SpendEnergyAsync / RefillEnergyAsync
  (delta) / RefillToMaxAsync (full) / UpdateMaxAsync / GetRegenMinutesPerPoint`. (T30, T32.)
- **Stats/level-up:** `StatService.AllocateStatPointAsync`, `GrantLevelUpPointsAsync` (gem grants +
  refill + guild-stamina sync + class auto-advance chokepoint). (T30, T32.)
- **Gems:** `IGemService.GrantGemsAsync(playerId, amount, GemTransactionType, referenceId)` — append-
  only ledger, idempotent by referenceId. (T32, T33.)
- **Class:** client `ClassGate` (mandatory blocking overlay), `ClassService`, `ClassConfig`. (T32.)
- **Admin/audit:** `AdminService`, `IAdminService`, `AdminController [AdminOnly]`, `Player.Ban`,
  `AuditLog.Create` + `IAuditLogRepository.AppendAsync` (append-only table). (T39 dashboard, T40.)
- **Policies:** `AdminOnly` (role claim + config allowlist) and `ModeratorOrAdmin`. (T39, T40.)
- **Rate limit + validation:** `RateLimitMiddleware` (per-IP/per-player Redis), FluentValidation
  registered in `AddRotaServices()`. (T37, T38.)
- **Client overlay pattern:** `ClassGate` / `LevelUpOverlay` / `MilestoneBanner` mounted on the shell
  root (`AppBootstrap`); `PlayerState` event hub; `HeaderBar` (good home for the world-chat button +
  indicator). `MockRotaApi` + `Dtos.cs` must mirror every new endpoint.
- **Memory worth reading:** `chat-roles-and-reserved-naming` (chat colors/markers), `quest-reset-cycle`
  (T26), `bsi-battle-stat-index`, `mock-fidelity-playtest`, `drift-control-system`.

---

## 8. Definition of done (per ticket)
- Backend: branch off `main`; `dotnet build` 0 errors (ignore MSB3277); `dotnet test` green (add
  unit tests for new logic); migrations added + `database update` applied; commit (no co-author).
- Client: branch off `master`; headless-compile 0 `error CS`; DTOs mirrored; MockRotaApi stateful
  path added; commit incl `.meta` (never `Main.unity`/useMock).
- Update `CLAUDE.md` build-status + this spec's status; note any reversal of prior design; move to
  `docs/specs/shipped/` when complete.
- T39 specifically: schema documented (payload + table), dashboard runnable, an end-to-end test that a
  produced email lands a row in `outbound_emails` and surfaces in the admin list endpoint.
