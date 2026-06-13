# ROTA Session Handoff — 2026-06-12 (Obsidian Gilt UI swap · prior batches COMMITTED · profile+shell UNCOMMITTED)

## TL;DR (resume here)
Two sessions on 2026-06-12. **Session A (Fable):** the three quest/UI batches were COMMITTED —
backend **`ba3dc62`** (push to origin BLOCKED by the auto-mode classifier on direct-to-`main`;
**owner must `git push origin main`**), client **`bc290c5`**. **Session B (Opus 4.8, ultracode;
Fable was disabled mid-session):** Profile rework + the Obsidian Gilt UI swap. Both UNCOMMITTED,
pending owner Unity playtest. **964 unit + 111 integration green; client scratch-compile 0 errors.**

- **➊ BACKEND:** `git push origin main` for `ba3dc62` (committed, not pushed) — AND there is NEW
  UNCOMMITTED backend gem-wiring on top (PlayerDTOs/PlayerService/PlayerServiceTests; 964 unit green)
  to commit with it.
- **➋ UNCOMMITTED, awaiting owner playtest → commit (client repo, on top of `bc290c5`):**
  1. **ProfileScreen REBUILT on the template** (Gauntlet-mirror, owner-approved shape): left HERO
     card (portrait + level/class + ATK/DEF/GOLD/AWARDS chips + INVESTMENTS sheet + LSI/BSI + one
     ALLOCATE `.btn-cta`); right rail = EQUIPPED slot-tile grid + 🎒 Bag / 🏅 Awards. EVERY action
     is an `OverlayPanel` pop-out (alloc steppers w/ live LSI/BSI; bag tabs whose rows open a
     detail pop-out carrying Equip/Use; equipped-tile→Unequip; achievements summary off the AWARDS
     chip). Portrait pre-wired to `Resources/UI/ClassIcons/<Class>` with a glyph fallback.
  2. **OBSIDIAN GILT shell slice 1** (the CHOSEN UI direction — warm Gilded-Codex palette kept +
     left icon RAIL + rounder gold-glass): **AppShell.cs** root → ROW `[rail | app-main(header+
     content)]`; **BottomNav.cs** = vertical rail + 👑 `.nav__crest` (orientation is pure USS, same
     6 destinations, API unchanged); **Theme.uss** v2 (`.nav` rail, `.app-main`, slim gold-edged
     `.header`, refined `.bar` track, + a "rounding pass" bumping `.card/.panel/.overlay-panel/
     .chip/...` radii). HeaderBar.cs UNCHANGED (re-skins via USS). Adversarially reviewed (3 lenses):
     USS-validity + blast-radius CLEAN; the one "login breaks" finding was a verified FALSE POSITIVE
     (login centres identically via `.screen--centered`). Known follow-up: wrap the rail in a
     ScrollView ONLY if landscape/desktop is targeted (portrait fits 6 tiles + crest fine).
  3. **Header + bars + rail polish (2026-06-13):** GEM balance wired end-to-end (backend
     `PlayerProfileResponse.Gems` ← `IGemService` ledger SUM; client mirror + `MockProfile.Gems`);
     header now shows GOLD + GEMS `.chip` plates (action buttons dropped to a row below); resource
     bars RECOLORED (energy green · stamina yellow · guild purple · **HP red**); rail REORDERED to
     Home·Profile·Quest·Raids·Guild·Legion + a ⚙ **Options** foot tile (pop-out: reward toggle +
     replay tutorial). Backend 964 unit green; client compiles. Optional extras mocked, owner to
     pick (crest level badge · active-tile accent · rail notif dots · XP sliver · daily-reward btn).
  4. **Rail/header EXTRAS (2026-06-13) — all 5 added (UNCOMMITTED, compiles):** active-tile gold
     accent · crest level badge (BottomNav now takes PlayerState) · rail notification dots +
     `SetBadge` (Raids/Guild, seeded in MOCK only — no live count endpoint; cleared on visit) · XP
     sliver under the bars · 🎁 daily-reward button → OverlayPanel STUB (no daily-claim backend yet).
     A 2-lens adversarial review caught + FIXED a blocker (UI-Toolkit Button can't reliably host child
     elements → each tile is now a `.nav__tile` wrapper; SetActive uses a button dict). Follow-ups:
     live notif-dot data + daily-reward claim backend.
  5. **CLASS EMBLEMS located (2026-06-13):** 8 GPT crests at `assets/classes/*.png` (GUID-named,
     white-bg, circular = a TIER LADDER, not per-class). Proposed rank order sent to owner for
     confirmation; once mapped, copy to client `Resources/UI/ClassIcons/` + wire crest/portrait by
     tier (already `Resources.Load`-wired with glyph fallback).
- **➌ NEXT (owner-declared):** Obsidian Gilt **slice 2** = screen-specific card polish (Quest/Raid/
  Gauntlet detail surfaces adopt the rounded template); **class-icon FILES** (owner has them but NOT
  READY yet — drop into `Resources/UI/ClassIcons/<ClassName>.png`, zero code); then **T5 battalion
  BACKEND** slice (spec §0b D8, formula LOCKED — below).
- **Owner standards are LAW** (memory `owner-ui-standards`): pop-outs not expansions; rewards in
  ONE fixed replace-only slot; action buttons never move; no grey buttons; locked = unselectable;
  Gauntlet = the template; **Obsidian Gilt = the chosen modern skin**.
- **Lore→items** still queued; overlaps the art/lore slots reserved across the new screens.

## WHAT'S IN THE COMMITTED BATCHES (backend `ba3dc62` · client `bc290c5`)
**Backend (ROTA, on top of `ecf9277`):** ItemDTOs.cs + ItemService.cs (sigil SummonRaidId/
SummonDifficulty hydration for the summon screen); QuestService.cs + QuestConfig.cs +
LootTableDefinition.cs + content/loot_tables.json (ZONE-scoped difficulty unlock · sigils only
from the zone-FINAL boss node · Pano chase curve `0.5% + 4.5%·d/(d+50k)` cap 5%, all 20 drops
flagged `rareScaling`); QuestServiceTests.cs (+7 tests, gate setups updated);
docs/PLAYTEST_TICKETS + spec system-24 (§0b D6-D9 + D8 amendment).
**Client (ROTA.Client6, on top of `6dbb9c5`):** Theme.uss (TEMPLATE classes + overlay system);
NEW Runtime/UI/OverlayPanel.cs (+meta); GauntletScreen.cs (v2 two-column template page: inline
single STRIKE @1 ticket, ticket rename, Ranks/Shop/Prizes POP-OUTS, BATTALION editor 6 generals
+ 20 troops, power = (pATK+ΣbATK)×4+(pDEF+ΣbDEF) — stat-inherent, Discernment crit passive/never
shown); RaidScreen.cs (summon tab on template: boss cards/lore slot/tier picker, loot only in
Completed); RaidCombatView.cs (VICTORY pop-out for the killer → confirm → raid list; no Loot
button; no chat on solo raids); QuestScreen.cs (template overhaul: attempt POP-OUT with fixed
⚔ ATTEMPT + replace-only REWARDS slot; T73 in-screen box REMOVED per owner rule); Dtos.cs
(NewStrikeBalance + sigil fields); MockRotaApi.cs (stateful inventory/sigils, gauntlet hits spend
tickets, battalion-power damage, live XP curve 30·level^0.7, Pano rare-rate, zone-scoped gates).

## BATTALION (D8) — LOCKED FORMULA (owner 2026-06-12, spec §0b amended)
`power = (playerATK + Σ battalionATK) × 4 + (playerDEF + Σ battalionDEF) × 1` — base stats
inherent; power IS the gauntlet strike damage basis (× raid ±15% RNG); Discernment raises crit
passively (NEVER shown in power); slots = 6 GENERALS + 20 TROOPS. Client editor + mock combat
implement it (PlayerPrefs `rota_gauntlet_battalion`, migrates from the old 1+4 layout). The
backend slice (entity/migration/endpoints/hit-fork + true 1-ticket strike API) must match.

READ IN ORDER for the new chat:
1. This file.
2. **`docs/PLAYTEST_TICKETS_2026-06-11.md`** (tickets + the three 2026-06-12 feedback batches).
3. `docs/CURRENT_TASK.md` (snapshot).
4. Memory: `owner-ui-standards` (the template + reward-slot rules), `tickets-playtest-061126`,
   `mock-fidelity-playtest`.
5. `docs/specs/active/system-24-gauntlet-event-experience.md` for the Gauntlet/battalion work.

## REPO STATE
- **Backend** `C:\Users\xolac\OneDrive\Documentos\Projects\ROTA`, branch `main`, last commit
  **`ecf9277`** (pushed to origin). **9 files modified, uncommitted** (list above). Migrations:
  none pending (through `AddGauntletEventIdentity`).
- **Unity client** `C:\Dev\ROTA.Client6`, branch `master`, last commit **`6dbb9c5`** (local — NO
  REMOTE). **7 modified + OverlayPanel.cs new, uncommitted.** Unity 6000.4.9f1; no asmdefs.
  Compile-check without the editor: `dotnet build %TEMP%\rota-client-check\check.csproj` (a scratch
  csproj over Runtime/**/*.cs against the Unity managed DLLs — rebuild it if missing; the owner's
  open editor recompiles on focus and holds the project lock for batchmode).
- **Ops dashboard** `C:\Dev\rota-ops-dashboard` — untouched.
- **LIVE SERVER likely running** on `http://localhost:5035` WITH the uncommitted quest-rule backend
  (Active Neck event seeded). Stop before rebuilding: `Get-Process ROTA.Api | Stop-Process`.
  Docker postgres+redis up; **don't `dotnet test`/rebuild while the server runs** (DLL lock).

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
0. **Commit the uncommitted batches** (owner review — see UNCOMMITTED WORK in the TL;DR block).
1. **PROFILE screen UI overhaul** (owner-declared next): rebuild ProfileScreen on the template
   (cards/kickers/pop-outs — the alloc modal becomes an OverlayPanel pop-out; identity card gets
   an art slot). Mock up first if the layout changes substantially (owner approves mockups).
2. **CLASS ICONS + resource-bar restyle (HeaderBar):** owner has GENERATED class-specific icons —
   ASK for the files; show them in the `.header__portrait` slot keyed off `Profile.Class`
   (suggested: `Resources/UI/ClassIcons/<ClassName>.png`); restyle the resource bars (stylistic
   only — keep the PlayerState.GetLiveResource single-source data flow from T4).
3. **T5 battalion BACKEND slice** (spec §0b D8 — formula locked, client/mock already match):
   loadout entity + migration, assign/read endpoints, server power computation, gauntlet hit-fork
   damage = power, true 1-ticket strike API (drop hitSize), replace the client PlayerPrefs preview.
4. **Lore → game asset items** (queued; owner: playtest before lore goes in): wire Master Canon
   names/descriptions into `content/items.json` + `gear.json`, fill the art/lore slots reserved
   across Gauntlet/summon/quest screens. Source: `docs/Lore/ROTA_Master_Canon.md`.
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
