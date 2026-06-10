# ROTA Session Handoff — 2026-06-10 (Wave 1 client catch-up DONE → backend wave T59 DONE)

## TL;DR (resume here)
**COMMITTED & APPLIED this session:** backend `bde33f5` (T59 + index-hardening + mod polish + T71),
client `e0bdbfe` (Wave 1). Both migrations APPLIED to the dev DB. Tree is clean. Audit fixes were
already `30e2eaa`. **Wave 1 (T60–T64) is DONE** — key discovery: T60/T62/T63
were ALREADY shipped in earlier client commits (this file's old Wave-1 list was stale); this session
built T61 (AchievementsScreen + Home tile), fixed T63 gaps (RaidChatUnavailable handler, raid/guild
send gating on IsConnected, stale tooltip), and ran the never-run T64 contract+mock-fidelity audit
(profile ActivePledge/MasteryRatingActive + InventoryItemResponse wire-shape fixed; mock LSI cap /
Health-alloc credit / per-difficulty health drain fixed). Client compiles clean, **UNCOMMITTED** in
C:\Dev\ROTA.Client6. See memory `wave1-client-catchup.md` for the accepted mock simplifications.
**T59 (players-row optimistic concurrency) is BUILT this session** — xmin token on players
(PlayerConfiguration), `IPlayerRepository.MutateWithRetryAsync` chokepoint wired at the 3 reward
writes (quest rewards, raid on-hit, kill-loop = the last-write-wins fix), EMPTY migration
**AddPlayerXminConcurrency** (xmin is a system column — snapshot-sync only; owner applies), and
PlayerConcurrencyTests (3 tests: stale-save throws, 8-writer no-loss, missing-player).
**Also built this session (all backend, UNCOMMITTED, owner reviews → commits):**
- **Index-hardening migration `IndexHardening`** (NOT applied to dev DB): players email/username
  partial uniques (soft-deleted row no longer blocks re-registration), guild_join_requests pending
  partial unique (guild,player,kind WHERE status=0), active_raids `ix_active_raids_lootable`
  (T57 hot query), friendships UNDIRECTED pair unique via raw SQL LEAST/GREATEST (hand-added to
  the migration — EF can't model expression indexes; safe because declines/unfriends soft-delete).
- **Moderation polish:** AdminService.ActorMayModerateAsync — a Moderator can no longer ban/mute a
  Moderator/Developer (admin or CLI only); unmute intentionally ungated. +3 unit tests.
- **T71 ops hardening:** EmailConfig.MaxSendAttempts(5)/RetrySendDelaySeconds(60);
  IEmailNotificationService.ProcessSendAsync → Task<bool> (settled vs retryable);
  EmailSendBackgroundService startup sweep re-enqueues stranded Queued + retryable Failed rows
  (IOutboundEmailRepository.GetPendingSendIdsAsync) and re-enqueues retryable failures after a
  delay; already-Sent rows skip without re-sending (sweep/live-enqueue overlap). +3 unit +1
  integration tests. The BanGateMiddleware HTTP-pipeline test remains OPEN (needs first HTTP-level
  harness — pattern exists in BetaKeyConcurrencyTests' WebApplicationFactory).
**Final green: 905 unit + 106 integration = 1011** (pending the last in-flight integration run).
TWO new migrations for owner to apply: `AddPlayerXminConcurrency` (empty, snapshot-only) +
`IndexHardening`. Next wave: Wave 2 public-beta blockers (T65 password reset → T66 deploy
artifacts → T67 CI → T68 terms/privacy → T69 onboarding-lite → T70 Windows build pipeline).

READ IN ORDER:
1. This file.
2. `docs/audit/AUDIT_2026-06-09.md` — every finding, fix, refutation, and ticket (the audit record).
3. Memory: `security-audit-2026-06-09.md` + `tickets-53-58-batch.md`.
4. CLAUDE.md (note the AMENDED resource formula: MaxEnergy = **25**+inv / MaxStamina = **5**+inv).

## REPO STATE
- **Backend** `C:\Users\xolac\OneDrive\Documentos\Projects\ROTA`, branch `main`:
  - Last commit `d275efb` (T53–58 batch). On top: **UNCOMMITTED audit fixes** (~25 files: AuthService,
    RaidService, GauntletService, ClassService, EnergyService, GemService+repo, ItemService+validator,
    RateLimitMiddleware, NEW BanGateMiddleware, ChatHub, AdminService, PlayerStats/Player,
    Program.cs, repos + tests). `git status` shows the full set. Owner reviews → commits.
  - **All 25 EF migrations APPLIED** to the dev DB (verified via `dotnet ef migrations list` — the
    integration suite migrated it). No pending migrations.
- **Unity client** `C:\Dev\ROTA.Client6`, branch `master`, last commit `9eab087` (T53–58 client),
  tree CLEAN, compile-verified (Unity 6000.4.9f1 headless, 0 `error CS`). No remote.
- **Ops dashboard** `C:\Dev\rota-ops-dashboard` — untouched.
- Docker postgres+redis up; **don't run the dev server while testing** (locks the Api DLL).

## DECISIONS LOCKED (owner, this session)
- Roadmap target: **PUBLIC BETA**. Client weighting: **catch-up wave first**, then lockstep.
- Hosting: **decide later** — build host-agnostic artifacts (Dockerfile, appsettings.Production,
  secrets-via-env, CI) when Wave 2 arrives. Client platform: **Windows standalone**.
- **SignalR Unity client: wire it now** (in Wave 1).
- Resource pools: **keep the 25/5 playtested feel** — `PlayerStats.BaseMaxEnergy=25/BaseMaxStamina=5`
  + investment; seeds reference the constants; CLAUDE.md amended (was 10+inv, which destroyed
  14 energy on first allocation).
- Players-row concurrency (lost XP/gold on simultaneous quest+raid): **T59, first ticket of the next
  BACKEND wave** — xmin token + retry on Quest/Raid player writes + concurrency integration tests.
  Fold in: kill-reward last-write-wins; optional per-player strike-spend lock.
- No commits without owner review.

## WAVE 1 — CLIENT CATCH-UP (the next work; all in C:\Dev\ROTA.Client6)
Client code lives under `Assets/ROTA.Client/Runtime/` (Api/Dtos.cs, Api/IRotaApi.cs + HttpRotaApi.cs
+ MockRotaApi.cs, Screens/, UI/, State/PlayerState.cs, App/AppBootstrap.cs). No asmdefs — everything
compiles into Assembly-CSharp. Verify with a headless compile (Unity 6000.4.9f1 batchmode, grep
`error CS` → 0; clear Library/ScriptAssemblies first for a clean check). Mock playtest = owner.
- **T60 — System 23 mirror:** visibility tiers (Private/Public/GuildOnly/FriendsOnly) on the share
  panel (server: POST /api/raids/{id}/share body `{Visibility}`), LifecycleState surfacing;
  reconcile with the T57 loot UI (already shipped). Server kept derived `IsPublic` for back-compat.
- **T61 — Achievements screen:** GET /api/achievements → browse gallery + AP total (AP already on
  profile DTO). Mirror DTOs; stateful mock.
- **T62 — Subjects:** GET /api/subjects → dropdowns on bug-report/player-report dialogs (server
  REJECTS off-list subjects with 400 since T52 — the client currently free-types, so this is a bug
  fix, not just polish).
- **T63 — SignalR chat send:** add the Microsoft SignalR client package to Unity; wire /hubs/chat
  (JWT over querystring is already how the server expects it): SendWorldMessage / JoinRaid+
  SendRaidMessage (NOTE: server now PARTICIPANT-GATES raid join/send — audit fix) / JoinGuildChannel+
  SendGuildMessage; live PM push (Clients.User). Un-disable the "Live chat coming soon" send boxes.
- **T64 — Mock fidelity batch:** **the audit's client-drift lens NEVER RAN (usage limit) — the
  client↔server contract is the one UNAUDITED surface.** Start T64 by running that comparison
  (client Dtos.cs/HttpRotaApi vs src/ROTA.Shared/DTOs + controllers; mock vs live behavior), then fix
  what it finds. Known class: stateless mocks masquerade as backend bugs (see memory
  mock-fidelity-playtest).

## BACKEND TICKETS QUEUED (after Wave 1 / parallel if capacity)
- **T59** players-row optimistic concurrency (above).
- **Index-hardening migration** (one migration): players email/username partial uniques (soft-deleted
  row currently blocks re-registration with a 500), friendship ordered-pair unique, guild_join_requests
  pending-uniqueness, lifecycle index covering Lootable rows (T57 hot query).
- **T71 ops hardening:** outbound-email queue startup recovery + send retry (rows currently strand on
  restart); BanGateMiddleware HTTP-pipeline integration test (the suite never exercises middleware —
  first HTTP-level harness is its own small ticket).
- **Moderation polish:** mod-cannot-act-on-mod rank check.
- **Balance/tuning notes for owner:** off-cap aura ownership mult applies to chance AND amount
  (spec-faithful but ×1.56 EV vs the ×1.25 the name suggests); strike balance can dip briefly
  negative cross-raid (self-corrects).

## WAVES 2–4 (public-beta path, from the locked roadmap)
- **Wave 2 blockers:** T65 password reset (rides T39 email backbone); T66 deploy artifacts
  (Dockerfile/prod config/secrets); T67 CI; T68 terms/privacy; T69 onboarding-lite; T70 Windows
  build pipeline.
- **Wave 3 content:** T71→ loot tables for ~60 empty Ch4–6 nodes; raid pool 2→6-8 bosses; 5 inert
  pinnacle magics → real effects; Pano set bonus.
- **Wave 4 depth:** masteries raid threshold-drop Hoard scaling + quality upgrades; achievements raid
  Collector hooks; guild succession driver + guild-raid item loot.

## VERIFY (fresh session)
`dotnet build ROTA.slnx` → 0 errors (4 pre-existing MSB3277 warnings in IntegrationTests are fine).
`dotnet test tests/ROTA.UnitTests` → 899. `dotnet test tests/ROTA.IntegrationTests` → 102 (needs
docker-compose postgres+redis). Unity: batchmode compile as above.
