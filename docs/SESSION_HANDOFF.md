# ROTA Session Handoff — 2026-06-11 (waves COMMITTED + PUSHED · live playtest → 5 new tickets)

## TL;DR (resume here)
The 3.5 waves are now **committed and pushed**, the **lore Master Canon** is merged + committed, a
**live server playtest** was run, and the owner filed **5 new tickets**. That ticket batch is the
**priority backlog** — everything below it is history.

- **NEW WORK → [docs/PLAYTEST_TICKETS_2026-06-11.md](docs/PLAYTEST_TICKETS_2026-06-11.md)** — 5
  triaged, pointer-grounded tickets from the live playtest (T1 difficulty-gate, T4 energy/stamina
  delta + HUD↔profile, T3 raid-loot enforcement, T5 Gauntlet overhaul epic, T2 raid-summon remodel).
  **Start here.** T1/T4/T3 are correctness bugs (server is fine on T1/T4 — the bugs are client-side);
  T5 extends the system-24 spec; T2+T5 are Fable UI passes that cluster with lore→items.
- **⚠️ OPEN DECISION (unresolved):** `appsettings.json` shipped `Developer.Usernames:["Xolaces"]`
  (committed in the push), which on the live boot **flagged Xolaces as Developer (`roles=13`) and
  auto-created the "The Dev Coffee Shop" guild**. This contradicts CLAUDE.md T43 ("allowlist EMPTY;
  never flag Xolaces"). Likely a stray test value. **Revert** = empty the list, commit, run
  `unflag-dev Xolaces` to clean the DB. Awaiting owner's keep-or-revert call.
- **Lore→items** integration is still **queued** (owner: playtest before lore goes in) and overlaps
  T2/T5 art/lore slots. Source of truth: [docs/Lore/ROTA_Master_Canon.md](docs/Lore/ROTA_Master_Canon.md).

### What landed this session (now committed)
1–4. The four waves (Wave 2 T65–T70 · UX T72–T75 · T76 Gauntlet foundation + S2 page slice) — detail
preserved under "WHAT WAS BUILT" below. **All green: 956 unit + 111 integration = 1067; client
compiles clean.** Migrations `AddPasswordResetTokens`, `AddTermsAcceptance`, `AddGauntletEventIdentity`
created + applied to dev DB.
5. **Lore Master Canon** — merged First+Second canon into `docs/Lore/ROTA_Master_Canon.md` (four
overrides applied silently). Inert reference doc, separate commit.

READ IN ORDER for the new chat:
1. This file.
2. **`docs/PLAYTEST_TICKETS_2026-06-11.md`** (the active backlog).
3. `docs/CURRENT_TASK.md` (snapshot).
4. Memory: `owner-ui-standards`, `mock-fidelity-playtest`, `t76-gauntlet-foundation`,
   `tickets-playtest-061126` (the new batch).
5. `docs/specs/active/system-24-gauntlet-event-experience.md` if touching the Gauntlet (T5 extends it).

## REPO STATE (post-push)
- **Backend** `C:\Users\xolac\OneDrive\Documentos\Projects\ROTA`, branch `main`,
  **last commit `39d21d4`** (lore) on top of `4bd9850` (the 3.5 waves, 101 files). **PUSHED to
  `origin/main`** (github.com/xolaces/ROTA — remote was behind at `630b2e7`, now current). All
  migrations applied to dev DB; none pending. Working tree clean.
- **Unity client** `C:\Dev\ROTA.Client6`, branch `master`, **last commit `d7f9609`** (the waves'
  client mirror). **Committed locally — NO REMOTE** (can't push). Unity 6000.4.9f1; no asmdefs.
- **Ops dashboard** `C:\Dev\rota-ops-dashboard` — untouched.
- **LIVE SERVER may still be running:** `dotnet run` on `http://localhost:5035` (http profile),
  fresh build, DB migrated, with an **Active Neck event** open ("The Gauntlet of the Sunken Spire",
  run #1) seeded for the playtest. To stop: `Get-Process ROTA.Api | Stop-Process`. To run CLI
  against the built exe while it's NOT running: set `$env:ASPNETCORE_ENVIRONMENT=Development` and run
  from `src/ROTA.Api` (content root). Docker postgres+redis up; **don't `dotnet test`/rebuild while a
  server runs** (locks the Api DLL).

## WHAT WAS BUILT (by ticket)

### Wave 2 — T65–T70 (public-beta blockers)
- **T65 password reset** (owner-locked: opaque code, 15-min TTL, revoke all sessions, always-202):
  `PasswordResetToken` entity/repo (SHA256-hashed code, single-use atomic conditional UPDATE,
  concurrency-proven), `Auth:PasswordResetTokenMinutes` (15). POST /api/auth/password-reset/
  request|confirm, rate-limited per-email (SHA-derived pseudo-Guid) + per-IP via
  ISubmissionRateLimiter. **`EmailPayload.RecipientOverride`** is the switch that makes the T39
  operator-email pipeline deliver PLAYER-facing mail (raw subject + dedicated body in
  EmailNotificationService.BuildBody). Accepted tradeoff: plaintext code sits in the outbound
  email row's detail jsonb (single-use, 15-min, admin-only dashboard).
- **T66 deploy artifacts** (host-agnostic): multi-stage `Dockerfile` (VERIFIED: builds, content/
  ships, non-root, :8080), `.dockerignore`, `appsettings.Production.json` (zero secrets — all
  env), `docker-compose.prod.yml`, config-gated ForwardedHeaders (trusted proxies only),
  **docs/DEPLOYMENT.md** (env-var table + migrate-BEFORE-start runbook). Gotchas fixed:
  EFCore.Relational pin 9.0.16→9.* (NU1605 on clean restore); Web SDK does NOT publish .md by
  default (legal files need explicit Content Include).
- **T67 CI**: migration gate = hermetic unit test `MigrationSnapshotTests`
  (`Database.HasPendingModelChanges()`, no DB/dotnet-ef/env needed — the `dotnet ef` CLI would
  execute Program.cs top-level and throw without JWT keys in CI). ci.yml also gained a
  docker-image build job. NOTE: raw-SQL-only schema (friendships LEAST/GREATEST index) is
  invisible to the gate by design.
- **T68 terms/privacy** (owner-locked: versioned, SOFT gate, markdown in repo):
  `Legal:CurrentTermsVersion` (=1); `Player.AcceptedTermsVersion/TermsAcceptedAt` + monotonic
  `AcceptTerms()`; register validator requires the EXACT current version
  (`RegisterRequest.AcceptedTermsVersion` — **pre-T68 clients now 400 on register**);
  `AuthResponse.{RequiresTermsAcceptance,CurrentTermsVersion}` on every token issue;
  GET /api/legal/terms|privacy (anonymous, serves content/legal/*.md via boot-validated
  LegalTextProvider) + POST /api/legal/accept (409 stale, idempotent re-accept).
  **Legal text is PLACEHOLDER — owner must replace before launch.** Existing accounts backfill
  to version 0 → re-accept overlay on next login (incl. the seeded admin — expected).
- **T69 onboarding-lite** (owner picked client-only): `TutorialOverlay.cs` — 5-step tap-through
  tour, once per machine (PlayerPrefs `rota_tutorial_done_v1`), shown post-login after the
  class gate. Resettable from the dev tools System tab.
- **T70 build pipeline** (owner picked local script): client `tools/build-client.ps1` →
  batchmode `-executeMethod RotaClient.EditorTools.BuildPlayer.BuildWindows` (scene
  Assets/Scenes/Main.unity, `-version` stamps bundleVersion, zips dist/ROTA-win64-*.zip).
  **Compile-verified only — the first real build run is an owner step (Unity license).**
- Client mirrors: Dtos/IRotaApi/Http/Mock + LoginScreen rebuilt (forgot-password 2-step panel,
  terms checkbox + viewer on register, blocking re-accept overlay, decline = logout). Mock is
  stateful: single-use reset code `ABCD-2345` printed to console; terms version flip testable.

### UX wave — T72–T75 (owner feedback; see memory `owner-ui-standards`)
- **T72 readability:** TWO root causes in Theme.uss — `.btn-link` was used by code but NEVER
  DEFINED (login toggle / forgot password / View terms / Skip tour rendered Unity-grey), and no
  base `Button` type rule existed (any unclassed button → grey). Fixed with a base `Button` type
  selector (gold-on-dark, readable dimmed-gold `:disabled`, lifted opacity), the `.btn-link`
  rule, `.btn-primary:disabled` re-color, themed Toggle checkmark/label. Grey buttons are now
  impossible by construction. Theme.uss convention: NO shorthand properties (per-side borders).
- **T73 quest rewards:** persistent "LATEST REWARDS — <node>" box inside QuestScreen (gold/xp/
  gems/items + level-up/zone notes); item-drop pop-up OPTIONAL via "Reward pop-ups" toggle
  (PlayerPrefs `rota_reward_popups`, default OFF). Level-up overlay stays mandatory (T20 rule).
- **T74 difficulty gating:** backend `QuestAvailabilityResponse.HighestUnlockedDifficulty`
  (gate-chain walk over PlayerQuestDifficultyProgress; new repo `GetAllForPlayerAsync` — one
  extra query in availability). Client renders "🔒 <tier> locked / Clear <prev> first" INSTEAD
  of an attempt button — locked tiers are unclickable. Mock difficulty-gates attempts + tracks
  per-node tier completions.
- **T75 dev tools:** new `[AdminOnly]` **DevController** /api/dev/grant|grant-item|refill →
  audited DevService (gold/gems[AdminGrant ledger]/SP/XP — XP runs through the T59
  MutateWithRetryAsync chokepoint and fires REAL level-ups; items validated vs items.json;
  refill = all 4 pools). Client DevToolsScreen gained **Player tab** (grants/item/refill,
  stateful in mock) + **System tab** (JWT claims decode, client info, tutorial reset,
  PlayerPrefs wipe). DEV-TOOLS BACKLOG (second wave): summon-any-raid, gauntlet
  open/close/settle from client, zone/progress reset, mock latency + error injection.

### T76 — Gauntlet event-experience foundation (System 24; spec §0 = locked decisions, §6 = state)
Owner-locked: the shipped SOLO AUTO-SUMMON LADDER **is** the DotD shape (no shared raids).
- **Brackets:** `GauntletLeague` += `Ancient=3`; bounds → 1–999 / 1000–2499 / 2500–4999 / 5000+
  (code defaults + appsettings + provider validation + client mock).
- **Ranking = highest stage completed:** `GauntletEntry.HighestStage` (atomic SQL GREATEST via
  `RecordStageDefeatAsync`, wired in the RaidService kill block via new
  `GetGauntletRaidByDefinitionId` reverse lookup); rank ORDER BY highest_stage DESC, score DESC,
  tie_break_at ASC; DTOs/leaderboard/caller standing carry the stage ("Stg N · damage").
- **Late HP ramp:** `LateRampStartStage`/`LateRampFinalGrowth` — growth interpolates 1.0493 →
  ×2.0 between stages 200 and 250 (DotD feel: "brutal ~230, doubling at 250"). OFF by default
  in code (fixtures untouched), ON in appsettings. Curve tests assert continuity at the
  boundary, monotonicity, 1.95–2.05 final growth.
- **Neck/Ring event families:** `GauntletEvent.Kind/RunNumber/LoreBlurb/BannerKey`; RunNumber
  counts per kind; rank-MAGIC hand-off is Neck→Neck ONLY; settle uses kind-aware bands
  (`RingBands` in gauntlet_prizes.json optional — until authored, Ring falls back to Neck bands
  with MagicId STRIPPED); CLI `gauntlet-open <name> <start> <end> [neck|ring]`; admin endpoint +
  validator accept kind/lore/banner.
- **Event page first pass (client):** GauntletScreen header → identity card: gold NECK / purple
  RING badge, "name — Run #N", lore blurb, live 1-second countdown anchored on server
  `SecondsRemaining`; standing = "Highest stage · Damage · Rank".
- **REMAINING T76 SLICES:** seasonal rank-GEAR grant/removal (BLOCKED on T77 neck/ring gear
  content; needs a PlayerEventGear-style ledger mirroring PlayerEventMagic), settlement summary
  screen ("you placed #N — won/lost"), Home CTA Coming-Soon/Settled states, prize preview table
  on the page, banner art slot.

### Staleness sweep (fixed this session)
- `docs/CURRENT_TASK.md` REWRITTEN (pointer + snapshot; was 2026-06-02 / 434 tests).
- `docs/PROJECT_STATE.md` banner'd HISTORICAL (claimed "no game client", 592 tests).
- Specs reorganized: system-16-gauntlet, system-22-masteries-core, phase-2-ops-social,
  system-23 → `shipped/`; system-24 → `active/`; specs README table refreshed.
- changelog.md: superseded-journal note. IPlayerLegionSlotRepository: obsolete BETA-PLACEHOLDER
  header corrected.
- **NEW pre-beta items surfaced:** (a) client `TokenStore` persists refresh tokens as PLAINTEXT
  JSON under persistentDataPath — encrypt-at-rest before public beta; (b) Bazaar magic shop has
  NO backend catalogue endpoint (client BETA-PLACEHOLDER is CORRECT, shows owned-only).
  Function Reference last refreshed T46-era (known to lag).

## NEXT (priority order)
0. **⚠️ RESOLVE FIRST — Xolaces dev-flag** (see TL;DR): keep or revert the committed
   `Developer.Usernames:["Xolaces"]`. Revert = empty list + commit + `unflag-dev Xolaces`.
1. **PLAYTEST TICKETS → [docs/PLAYTEST_TICKETS_2026-06-11.md](docs/PLAYTEST_TICKETS_2026-06-11.md)**
   — the live-playtest backlog, recommended order T1 → T4 → T3 → T5 → T2. T1 (difficulty-gate
   client selectability) and T4 (energy/stamina delta + HUD↔profile SSOT) are quick-ish client
   bugs with the backend already correct; T3 (raid-loot enforcement); T5 (Gauntlet overhaul epic,
   extends system-24); T2 (raid-summon remodel, Fable UI pass).
2. **Lore → game asset items** (queued; owner: playtest before lore goes in): wire Master Canon
   names/descriptions into `content/items.json` + `gear.json`, surface lore on Home. Overlaps
   T2/T5 art+lore slots. Source: `docs/Lore/ROTA_Master_Canon.md`.
3. **OWNER housekeeping:** replace placeholder legal text (`src/ROTA.Api/content/legal/`); first
   real `tools/build-client.ps1` run; client has no git remote (add one to push client work).
4. **T77 content wave (owner-led, lore-gated):** Ch4–6 loot tables (~60 empty nodes), raid pool
   2→6-8 bosses, pinnacle magics → real effects, Pano set bonus, Gauntlet Neck/Ring rank-gear
   sets (unblocks T76's seasonal-gear slice) + ring prize bands.
5. **T76 remaining slices** (after/with T77 gear): rank-GEAR seasonal grant/removal; token-bonus
   parity at settle; banner art slot. (Settlement screen, CTA states, prize table already shipped.)
6. **Pre-beta hardening:** TokenStore encryption; magic-shop catalogue endpoint; BanGateMiddleware
   HTTP-pipeline test; global soft-delete query-filter sweep. Plus dev-tools second wave + Wave 4
   depth (mastery threshold-drop Hoard scaling, achievement raid Collector hooks, guild succession
   auto-driver, guild-raid item loot).

## STANDING OWNER RULES (do not violate)
- No commits without owner review/say-so. Run game-changing design questions by the owner.
- UI: no grey/unreadable buttons EVER; locked options must be UNSELECTABLE (not click-then-409);
  interrupting pop-ups optional/inline; marquee features get full event-page treatment
  (memory `owner-ui-standards`).
- Server is always authoritative; mocks must be STATEFUL (memory `mock-fidelity-playtest`).
- Content is lore-gated + owner-reviewed (docs/LORE_HANDOFF.md; tone: dark mythic, Ancients canon).

## VERIFY (fresh session)
- `dotnet build ROTA.slnx` → 0 errors (4 pre-existing MSB3277 warnings in IntegrationTests OK).
- `dotnet test tests/ROTA.UnitTests` → **956**.
- `dotnet test tests/ROTA.IntegrationTests` → **111** (needs Docker; Testcontainers spins its own
  Postgres; ~3 min).
- Client: clear `Library/ScriptAssemblies`, batchmode compile (Unity 6000.4.9f1), grep
  `error CS` → 0.
- `dotnet ef migrations list` → none pending (through `AddGauntletEventIdentity`).
