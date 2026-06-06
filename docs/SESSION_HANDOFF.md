# ROTA — SESSION HANDOFF → next chat

You are the Opus orchestrator/auditor on ROTA: a server-authoritative .NET 10 backend
(Dawn-of-the-Dragons-style async RPG) + a Unity 6.4 UI Toolkit client. The OWNER drives the Unity
Editor + Play (you can't see the Game view). Aggressive autonomy is authorized: use subagents,
parallelize, run any git / merge / dotnet (incl. `dotnet ef database update`) without asking.

## REPOS & GIT STATE
- **Backend** (.NET 10): `C:\Users\xolac\OneDrive\Documentos\Projects\ROTA` — git `main`, remote
  `origin` = github.com/xolaces/ROTA. `main` @ `87ec8e9`, **all pushed**. Docker (pg+redis) up; all
  migrations applied (latest `AddQuestEverCleared`). **478 unit + 35 integration green.**
  **Branch `chore/drift-control-tooling` is still UNMERGED** (drift tooling + the `/audit-dtos`
  command + `audit/` ledgers live there — merge to `main` when ready).
- **Unity** (6.4 / 6000.4.9f1): `C:\Dev\ROTA.Client6` — git `master` @ `ff90627`, **local-only (no
  remote)**. Use `git -C`. `Main.unity`'s `useMock` toggle — never commit it.

## READ FIRST
- `docs/ROTA_Function_Reference.md` (API/DTO contract), `CLAUDE.md` (architecture + security rules),
  `docs/specs/shipped/` (system specs). Confirm BOTH repos' `git log` before trusting anything.

## SHIPPED THIS SESSION (T19–T29, all verified + merged)
- **T20/T21/T22/T24 (level-up cluster):** backend full resource refill + GuildStamina-1:1-to-level
  on level-up (new `IEnergyService.RefillToMaxAsync`; GuildStamina pool was stuck at max 1). Client
  `LevelUpOverlay` (tap-to-dismiss) + `MilestoneBanner` (sweep every 2500 levels) via
  `PlayerState.NotifyLevelUp`.
- **T26/T29 (correctness):** T29 — `HeaderBar` regen ticker (server regen was correct; the header
  just never advanced between fetches). T26 — chapter-boss RESET CYCLE: clearing a node now LOCKS it
  (server `NodeCleared`→409); a boss clear resets the whole chapter. Split `IsCleared` (resettable) /
  `HasEverCleared` (permanent unlock latch). Migration `AddQuestEverCleared`. **Reverses System 20's
  "replayable".**
- **T19/T23/T25 (UI polish):** compact share button; raid countdown timer bar under the HP bar;
  slimmer profile left column.
- **T28:** alloc window shows live landing values + live LSI/BSI (BSI = (ATK+DEF)/Level); pool maxima
  removed from profile (they're in the header), profile shows INDICES instead.
- **T27:** legion tab selection-first; capacity-matched Generals/Troops slot segments; stub Equipment.
- **MOCK NOTE:** `MockRotaApi._mockProfile.Level = 2498` (was 67) so milestones are reachable in
  mock playtest — revert for general mock testing. Mock quest/raid now level up + refill statefully.

## SHIPPED PRIOR SESSIONS (all verified, all merged)
Playtest batch T1–T8 (System 19 client + System 20 + class preview) and T9–T18 below.
- **T9** compact Share pill · **T10** on-hit gold roll 3-8/stamina + combat-log XP/gold + running
  totals (XP was always firing — confirmed) · **T11** fresh-account quests (was mock seeding) ·
  **T12** themed dropdowns · **T14** stat-alloc now refreshes header+identity (real fix) ·
  **T15** global mandatory class gate (`ClassGate`, blocks nav, re-prompts on login) · **T16** raid
  background layer (placeholder) · **T17** full-background nav tabs.
- **T13** was a mislabel (its screenshot was really T14) — no separate work.
- System 20: quest node depletion (100→0, battle −5/boss −2.5, deplete-to-clear), Discernment-scaled
  drops, Pano Orange 8-piece set. System: class `ChoicePreviews` for the gate.

## RECURRING LESSON (important)
Owner playtests in **mock mode** (`useMock=true`). Several "bugs" (stamina, legion-active,
leaderboard tabs, stat-alloc, fresh quest node) were **stateless-mock artifacts** — the live backend
was correct. **Mock fidelity is part of every ticket**: a new feature's `MockRotaApi` path must
mirror live (mutable state), or playtest feedback is noise. Live mode is the real validation.

## Phase 2 — Ops & Social (T30–T40) — BACKEND + CLIENT SHIPPED (2026-06-06)
**Spec:** `docs/specs/active/phase-2-ops-social.md`. CLAUDE.md has the full shipped summary.
- **Backend: ALL 8 tickets done on branch `feat/phase2-ops-social`** (off main, UNMERGED, **pushed to
  origin**) — T39 email backbone, T40 ban/mute, T30 SP delta, T32 pinnacle gems, T33 first-claim +
  placeholder magics, T38 feedback, T37 friends/PM/report, T35/36 SignalR chat. **526 unit + 35
  integration green** (+ adversarial multi-agent review pass + fixes). Migrations AddOutboundEmails/
  AddPlayerMute/AddPinnacleFirstClaims/AddSocialSystem/FriendshipPartialUniqueIndex applied. Merge to main when ready.
- **React ops dashboard SHIPPED** at **`C:\Dev\rota-ops-dashboard`** (separate local git repo, no remote):
  Vite+React+TS, mission-control UI, demo-mode default, admin-JWT login, `npm run build` passes. Run
  `npm install && npm run dev`. Point at the live API in Settings or `.env.local` (VITE_API_BASE).
- **Email provider deviation:** working provider is **Gmail SMTP** (`SmtpEmailService`; creds in API
  user-secrets `Email:Username`/`Email:Password` — temporary, owner to rotate), NOT SendGrid. Same
  `IEmailService` interface keeps SendGrid as the documented swap.
- **CLIENT (Unity) — BUILT + VERIFIED-COMPILING** on branch `feat/phase2-client-plumbing` (local-only,
  off master, UNMERGED; headless compile exit 0, zero `error CS`, confirmed twice + independently).
  Shipped: plumbing/DTO mirror (b49d955) + UI (c4f9882) — T31 scrollbar, T38 bug panel (HeaderBar 🐞),
  T37 SocialScreen (friends / PM-over-REST / blocks / report; nav entry), T34 raid layout restructure,
  T32 pinnacle gem callout, T36 world-chat read-only panel + HeaderBar 💬 unread dot. Mocks are stateful.
  **Merge to master when ready** (close the Editor first; `git -C C:\Dev\ROTA.Client6`). Note: untracked
  `Assets/_Recovery/` is Unity crash cruft — ignore, don't commit.
- **CLIENT follow-up (one task): wire a Unity SignalR client to `/hubs/chat`** (events
  WorldMessage / RaidMessage / PrivateMessage / Muted) to light up **T35 raid chat** + public
  **world/raid chat SEND** (currently a disabled "Live chat coming soon" box) + live PM push. Private
  messaging already works over REST. No backend change needed — the hub is live.
- Backend open follow-up: confirm gem amounts for pinnacle tiers **2000 / 15000 / 25000** (omitted from
  `LevelingConfig.PinnacleGemRewards` until set); stat-rollback is PHASE-2.

## ⏭️ REMAINING WORK → next session (Phase 2 tail) — START HERE
Backend + dashboard + most client UI shipped. What's left, in priority order:

### A. OPEN PLAYTEST BUGS — client/mock fidelity (owner-reported 2026-06-06)
All three are **client + MockRotaApi** issues. The **live backend is server-authoritative and unit-tested**
(T30 credit, EnergyService stamina guard, raid stamina spend) — these are mock-fidelity / client-display
gaps, the recurring lesson (owner playtests in `useMock=true`). Fix in `C:\Dev\ROTA.Client6` (close the
Editor, branch off `feat/phase2-client-plumbing` or `master`, headless-compile to verify).

1. **Allocating Energy/Stamina doesn't move the *current* bar (no immediate reward).**
   - Root cause: `Assets/ROTA.Client/Runtime/Api/MockRotaApi.cs` → `AllocateStatAsync` (~L145–146) raises
     `MaxEnergy/MaxStamina` + `SetResourceMax(...)` but never bumps the resource **`LiveValue`**. T30
     credits the delta to *current* on the live server (via `RefillEnergyAsync`, tested) — the mock omits it.
   - Fix: in `AllocateStatAsync`, for Energy/Stamina also raise that resource's `LiveValue` by `amount`
     (cap at new max) — add a `BumpResourceLive(type, amount)` helper mirroring `SetResourceMax`.
   - Live path is fine: `ProfileScreen` re-fetches the profile after alloc (`GetProfileAsync()` → `_state.Set`,
     ~L955), so the credited current shows once the mock returns it. `AllocateStatResponse` carries new MAX
     only (no current) — the re-fetch is what surfaces the credit; keep that re-fetch.

2. **Top-left HeaderBar resource bars don't match the true backend values (display drift).**
   - Root cause: the T29 per-second regen ticker in `UI/HeaderBar.cs` extrapolates display values and must
     **snap to server truth on every `PlayerState.Changed`**. It drifts when a spend isn't reconciled into
     `PlayerState` (so the ticker keeps counting from a stale base), and #1's max-without-current skews the
     fill ratio.
   - Fix: make the header derive purely from `PlayerState` (server truth) with the ticker as cosmetic
     extrapolation that resets to the server value on each fetch; ensure every spend reconciles —
     after a raid hit patch stamina from the authoritative `RaidHitResponse` (`PlayerState.PatchStamina`
     exists) and/or re-fetch; same for quest energy. Audit `HeaderBar.RenderResources` + the spend paths.

3. **Hit ×20 was allowed with only 10 stamina.**
   - Root cause: `Screens/RaidCombatView.cs` gates the hit buttons on `!raid.IsDefeated` only
     (`_hitRow.SetEnabled(...)`, ~L503) — NOT on available stamina, so ×5/×20 are clickable below cost.
     On live the server rejects (EnergyService → 422); the client let the click through; the mock raid-hit
     doesn't enforce stamina either.
   - Fix (client gate): track current stamina; enable each `Hit ×N` button only when `stamina >= N`; refresh
     gating on the per-second tick, after each hit, and on profile change; surface a clear "not enough
     stamina" message on a 422. Fix (mock fidelity): `MockRotaApi` raid-hit must reject when current
     stamina < hitSize and **deduct** stamina on success (mirror the backend) so mock enforces the rule.

### B. Real-time chat — ONE task: a Unity SignalR client → `/hubs/chat`
Lights up **T35 raid chat** + public **world/raid chat SEND** (today a disabled "Live chat coming soon"
box) + **live PM push**. PM already works over REST; the hub + auth (JWT-over-querystring) are live on the
backend — purely a client-side addition. Events to handle: `WorldMessage`, `RaidMessage`, `PrivateMessage`,
`Muted`. Add a SignalR client (BestHTTP or Microsoft SignalR client DLLs — verify Unity/IL2CPP compatibility).

### C. DECISIONS needed from owner
- **Pinnacle gem amounts for levels 2000 / 15000 / 25000** — omitted from
  `appsettings.json` → `LevelingConfig.PinnacleGemRewards` until set (1000/2500/5000/7500/10000 are live).
- Whether **mute** should cover **PMs** (currently it does — gated in `SocialService.SendMessageAsync`).

### D. MERGES (all green, owner's call)
- Backend: **`feat/phase2-ops-social`** pushed to origin — open a PR / merge to `main`.
- Unity client: **`feat/phase2-client-plumbing`** (local; verified-compiling) — eyeball in Editor, merge to `master`.
- Dashboard: `C:\Dev\rota-ops-dashboard` (local git, no remote) — add a remote if you want it off-machine.
- Pre-existing: **`chore/drift-control-tooling`** still unmerged.


- **T18** character vector models — DEFERRED (no vector-art pipeline; nav icons are Unicode emoji).
- **Merge `chore/drift-control-tooling` → `main`** (additive, green).
- **Content depth** — still 5 quests / 2 raids. Expand the questline.
- **Malachar raid size** — both raids summon `Small`; bump per-sigil in `content/items.json`.
- **Gear set bonuses** (PHASE-2); **System 16 Gauntlet** (spec in `docs/specs/active/`, unbuilt).
- **Live-mode validation pass** of the T19–T29 batch (owner tests in mock; verify server-authoritative).

## DISCIPLINE / GOTCHAS
- Backend: branch off `main`; JWT `MSB3277` warnings in the test project are pre-existing — IGNORE.
  `dotnet test` green (Docker up); commit, NO co-author. May `dotnet ef migrations add` + `database update`.
- A running `ROTA.Api` locks build DLLs (MSB3021/3027) — stop the `dotnet.exe` whose CommandLine
  matches `ROTA.Api` before building, then the owner can re-run it.
- Unity: branch off `master`; **Editor MUST be closed** to headless-compile (it conflicts otherwise)
  — check `Get-Process Unity`, ask the owner to close it. Compile:
  `& '...\6000.4.9f1\Editor\Unity.exe' -batchmode -quit -nographics -projectPath 'C:\Dev\ROTA.Client6'
  -logFile <log>` via `Start-Process -Wait`; rm stale `Temp\UnityLockfile` first; grep log for
  `error CS` (expect 0). Commit incl new `.meta`, NO co-author, never commit `Main.unity`'s `useMock`.
- **DTO drift** is the recurring failure: client `Assets/ROTA.Client/Runtime/Api/Dtos.cs` must mirror
  `src/ROTA.Shared/DTOs/*` (camelCase JSON, numeric enums).
- PowerShell wraps native-exe stderr as `NativeCommandError` even on success — check actual output.
