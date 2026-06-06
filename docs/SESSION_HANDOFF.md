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

## NEXT BATCH → Phase 2 — Ops & Social (Tickets 30–40) — NOT STARTED
**Full spec + build order + open decisions:** `docs/specs/active/phase-2-ops-social.md` (READ IT FIRST).
- **T39 is the FOUNDATION — build it first.** Operator email service + classification + `outbound_emails`
  log table + admin API + a separate React triage dashboard (rotadevteam@gmail.com). T33/T37/T38/T40
  route their payloads through it.
- Independent: **T30** (SP spend → immediate resource delta), **T31** (profile scrollbar behind
  equipment panel), **T32** (pinnacle gates: mandatory class select + gem rewards), **T34** (raid
  layout restructure).
- Chat cluster (shared real-time-delivery decision): **T35** raid chat → **T36** world chat →
  **T37** friends/PM/social + report.
- Email-dependent: **T33** pinnacle first-claim log, **T37** player report, **T38** bug/ticket, **T40**
  moderation/punishment log.
- **All 7 design decisions are resolved** (SendGrid, separate dashboard repo, SignalR chat,
  ConvergenceLevels win for class gates, Mute in T40 scope, 5/hr player rate limits, 100-msg Redis
  ring buffer) — see §6 of the spec for the full table. No unresolved decisions block any ticket.

## CARRY-OVER BACKLOG (pre-existing, owner's call)
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
