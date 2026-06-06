# ROTA — Rise of the Ancients

## Stack
- ASP.NET Core 10 / C#
- PostgreSQL 16 (EF Core 9, Npgsql)
- Redis 7 (StackExchange.Redis)
- JWT RS256 auth
- xUnit + Moq + FluentAssertions

## Project structure
src/ROTA.Api            ← Controllers, middleware, Program.cs
src/ROTA.Application    ← ALL business logic, service interfaces
src/ROTA.Domain         ← Entities only, no EF attributes
src/ROTA.Infrastructure ← EF Core configs, migrations, Redis
src/ROTA.Shared         ← DTOs, constants, enums

## Architecture rules (non-negotiable)
- Domain entities: private setters, no EF attributes, changes via methods only
- EF mapping: Fluent API only, never data annotations, configs in Infrastructure/Persistence/Configurations/
- Services: interface in Application/Interfaces/, implementation in Application/Services/
- Controllers: thin, delegate only, zero business logic
- snake_case for ALL PostgreSQL table names, column names, index names
- Every table has: id (UUID gen_random_uuid()), created_at, updated_at, is_deleted
- Every FK has an index

## Security rules (non-negotiable)
- Server is always authoritative — client sends intent only, server resolves
- RS256 JWT only — never HS256 ever
- Access tokens: 15 min expiry, zero clock skew
- Refresh tokens: 7 day expiry, rotate on every use, revoke old immediately
- Max 3 concurrent sessions per player — 4th login revokes oldest
- Failed login: 5 attempts → 15 min lockout → written to audit_log
- All inputs validated with FluentValidation BEFORE service layer
- Every state change writes to audit_log (PlayerId, Action, Timestamp, InputHash, ResultSummary, IpAddress)
- Audit log is append-only — no UPDATE/DELETE permission on that table ever
- Rate limit per-player AND per-IP via Redis

## Code labeling rules
- // BETA — current implementation, known limitations
- // PHASE-2 — deliberately deferred
- // FINAL — complete and hardened
- // BETA-PLACEHOLDER — stub that must be replaced before ship
- Never leave silent stubs that look complete

## Current build status
Phase 0: COMPLETE
- Solution scaffold, docker-compose, all 7 projects
- Domain entities: Player, PlayerStats, PlayerResource, RefreshToken, AuditLog
- EF Core configs + InitialCreate migration (applied)
- API pipeline: Program.cs with correct middleware order, JWT RS256 config
- Middleware stubs: RequestLoggingMiddleware, RateLimitMiddleware, AuditLogMiddleware
- IAuthService interface defined, AuthDTOs defined
- Keys: RS256 key pair generated and stored in Secret Manager

Phase 1 — Systems 1-9: COMPLETE (2026-05-25)
Build: 0 errors, 0 warnings. Tests: 60/60 passing.

System 1 — AuthService (BETA)
- AuthService: register/login/refresh/logout with BCrypt(12), RS256 JWT, token rotation
- Redis lockout: 5 failed logins → 15-min lockout per email (auth:lockout:{email})
- Max 3 concurrent sessions enforced; oldest revoked on 4th login
- Audit log written on every operation (success and failure)
- New interfaces: IAuditLogRepository, IAuthLockoutService
- New infra: AuditLogRepository, AuthLockoutService (Redis)
- Domain additions: AuditLog.Create(), Player.Ban(), Player.SoftDelete(), PlayerResource.SaveCheckpoint()
- Tests: 12 unit tests covering all success and security-failure paths

System 2 — AuthController (BETA)
- POST /api/auth/register → 201/409/400
- POST /api/auth/login    → 200/401/400
- POST /api/auth/refresh  → 200/401/400
- POST /api/auth/logout   → 204/401/400  [Authorize] required
- FluentValidation wired via injected IValidator<T>; validators registered in AddRotaServices()

System 3 — RateLimitMiddleware (BETA)
- Per-IP on /api/auth/**:  10 req/min (ratelimit:ip:{ip}:{path})
- Per-player on all else:  60 req/min (ratelimit:player:{playerId})
- Player ID extracted from JWT without signature verification (auth middleware enforces validity)
- 429 + Retry-After header; breaches written to audit_log

System 4 — AuditLogMiddleware (BETA)
- POST/PUT/DELETE requests: SHA256 hash of body, PlayerId from verified JWT, HTTP status
- Runs after authentication so PlayerId is always the verified identity
- Audit failure is swallowed — never breaks the response pipeline

System 5 — EnergyService (BETA)
- Interface: IEnergyService (GetCurrentEnergyAsync, SpendEnergyAsync, RefillEnergyAsync)
- New interface: IPlayerResourceRepository (GetAsync, AtomicUpdateAsync)
- New infra: PlayerResourceRepository — AtomicUpdateAsync uses PostgreSQL FOR UPDATE
- Live value computed from checkpoint + elapsed × regenPerMinute, capped at MaxValue
- SpendEnergy uses row-level lock; rejects if live value < amount after lock acquired
- All spends written to audit_log
- Tests: 7 unit tests covering regen, cap, spend, double-spend guard

System 6 — Player Profile (COMPLETE)
- GET /api/players/me returns full profile with live resource values; PUT /api/players/me updates username only
- Live values always computed via IEnergyService — stored checkpoint never returned directly
- PlayerId sourced from JWT on every call; soft-deleted players return 404 not 401
- Tests: 8 unit tests (live energy, update success/taken/notfound, validator accept/reject)

System 7 — Gem Ledger (COMPLETE)
- Append-only gem_transactions ledger; balance is always SUM(amount) — never a stored field
- Idempotency enforced via unique partial index on (player_id, transaction_type, reference_id)
- DailyRefill once-per-day enforced through referenceId = "daily:{yyyy-MM-dd}" — no extra timestamp column
- Tests: 5 unit tests (balance sum, duplicate ref rejection, insufficient balance, daily grant/reject)

System 8 — Quest System + Difficulty (BETA)
- Static quest definitions from content/quests.json loaded once at startup; no DB table for definitions
- Quest attempt: energy spent first; if insufficient, returns failure with zero side effects guaranteed
- Gem rewards idempotent via referenceId = "quest:{questId}:{playerId}:{completionCount}:{difficulty}"
- Difficulty system: Normal/Hard/Legendary/Nightmare with energy multipliers ×1.0/1.5/2.0/3.0 and reward multipliers ×1.0/1.5/2.0/3.5
- Difficulty gates server-enforced; PlayerQuestDifficultyProgress tracks per-difficulty completion
- Boss nodes drop Sigils: guaranteed on first per-difficulty completion, chance-based after (sigilDropChance)
- Loot table processing: guaranteedDrops + chanceDrops per difficulty (from content/loot_tables.json)
- Tests: 11 unit tests (prereq filter, unlock, success, multipliers, difficulty gate, energy fail, prereq fail, bad id, level up, sigil boss drop, loot table, gem key)

System 9 — Combat / Raid Engine + Difficulty (BETA)
- GET /api/raids → list all active raids with caller's damage/hit stats
- POST /api/raids/{id}/summon → 201/404 (accepts { "difficulty": "Normal" } body)
- POST /api/raids/{id}/hit → 200/400/404/409/410/422
- Server-seeded RNG damage formula: base = (ATK×4 + DEF) × hitSize × RNG(0.85..1.15); hitSize ∈ {1,5,20}
- Redis idempotency cache (raidhit:{key}, 24h TTL): duplicate submissions return cached response with zero reprocessing
- Difficulty HP multipliers: Normal×1.0, Hard×1.4, Legendary×2.0, Nightmare×3.6
- Contribution tiers on kill: Legendary1/2/3 (top 3), Epic (top 10% when >30 players), Rare (≥minContributionPercent%), Participant
- Tier multipliers applied to all rewards; gems to Rare+ only
- Cumulative threshold loot from content/loot_tables.json; Attack/Defense/Discernment SP for World/Event raids only
- Gem rewards idempotent via referenceId = "raid:{raidId}:{playerId}"; denormalized ParticipantCount for O(1) read
- Static raid definitions from content/raids.json (two bosses: raid_ironcolossus, raid_malachar, Tier=World)
- New entities: ActiveRaid (with Difficulty), RaidParticipant; migrations: AddRaidSystem, AddRaidDifficulty
- Tests: 13 unit tests (summon×4 difficulties, hit×3 sizes, damage formula, idempotency, expired, defeated, stamina fail, kill rewards tier mult, participant no gems, cumulative thresholds, gem key, list filtering, damage tracking)

System 10 — Item System (BETA)
- GET /api/items → returns authenticated player's inventory (hydrated with definition data)
- POST /api/items/{itemDefinitionId}/use → 200/404/422
- StatBag: grants unassigned SkillPoints (StatPointsOnUse × quantity); no LSI check on grant
- Sigil: summons raid at configured difficulty, consumes sigil from inventory on success
- Materials/Equipment: ItemNotUsable (Phase 2)
- content/items.json: 12 items (2 materials, 2 stat bags, 8 sigils — 4 difficulties × 2 raids)
- PlayerInventoryItem entity; unique index on (player_id, item_definition_id)
- Migrations: AddItemSystem
- Tests: 8 unit tests (inventory list, zero-quantity filter, stat bag SP, stat bag stack, sigil summon+consume, insufficient quantity, not usable, not found)

System 11 — Stat Allocation (BETA)
- POST /api/stats/allocate → 200/400/422 — FluentValidation then allocates SkillPoints to a stat type
- GET /api/stats/me → 200/404 — returns full stat sheet (investments, computed caps, LSI, health)
- AllocateStatPointAsync: validates SkillPoints, LSI cap check for Energy/Stamina (cap=9.0), updates max resource values
- GetStatsAsync: returns PlayerStatsResponse with all investment fields, computed MaxEnergy/MaxStamina/MaxGuildStamina, LSI
- GrantLevelUpPointsAsync: +10 SkillPoints per level; +5 gems at every multiple of 5 levels
- AddUnassignedPointsAsync: direct SkillPoint grant from items/raids (no LSI cap check)
- PlayerStats extended: EnergyInvestment, StaminaInvestment, DiscernmentInvestment, SkillPoints
- AllocateStatResponse: Success/FailureReason + New* prefixed fields (NewSkillPointsRemaining, NewEnergyInvestment, etc.)
- PHASE-2: DiscernmentInvestment effects (quest drop quality, raid crit bonus) — not yet applied
- Migrations: AddStatInvestmentFields
- Tests: 7 unit tests (allocate Attack/Defense/Health/Discernment, LSI cap enforce/allow, insufficient SP, level-up gems at 5/10/15 and not at 3, AddUnassigned no LSI check)

## System 12 — Beta Access Control (BETA) — COMPLETE (2026-05-29)
Build: 0 errors, 0 warnings. Tests: 185 unit + 3 integration = 188 total, all passing.
Migrations applied: AddPlayerRolesAndDisplayName (Component A), AddBetaKeys (Component B)
- Component A: PlayerRoles [Flags] enum (None/Player/Moderator/Admin), Player.Roles column,
  GrantRole/RevokeRole/HasRole/UpdateDisplayName methods, JWT role + display_name claims,
  AdminOnly (role claim + config allowlist break-glass) + ModeratorOrAdmin policies
- Component B: BetaKey entity, IBetaKeyRepository (TryRedeemAsync atomic conditional UPDATE),
  IBetaKeyService (Crockford base32 keygen), RegisterRequest.BetaKey + validator,
  AuthService transactional beta-gate (claim key, create player, rollback on failure),
  Player.CreateWithId factory, 18 unit tests + 1 concurrency integration test
- Component C: SeedData.EnsureAdminAsync (idempotent, reads Seed:AdminPassword — REQUIRED,
  never hardcoded; default email xolaces@rota.dev), wired in Program.cs pre-Run,
  4 unit-style tests; Seed:AdminEmail optional config
- Component D: IPlayerRepository.FindByUsernameAsync + CountByRoleAsync,
  IRefreshTokenRepository.RevokeAllActiveAsync, IAdminService + AdminService
  (GrantRoleAsync/RevokeRoleAsync with DB actor re-verify, last-admin guard, session revocation),
  AdminDTOs, RoleChangeRequestValidator + GenerateBetaKeysRequestValidator,
  AdminController [AdminOnly]: /api/admin/players/{id}/roles/grant|revoke,
  /api/admin/beta-keys (POST gen + GET list), 11 unit tests
- Component E: AdminCli.cs (seed-admin / gen-beta-key / promote / demote),
  Program.cs early-return before Kestrel; CLI commands in CLAUDE.md
- Component F: XML docs, changelog entry, CLAUDE.md updated, function reference updated
Configuration keys (set via user-secrets or env vars):
  BetaGate:Enabled          default true — set to false to open registration
  Seed:AdminPassword        REQUIRED for seed-admin (never has a default)
  Seed:AdminEmail           default xolaces@rota.dev

## Phase 1 BACKEND COMPLETE
## Phase 1 Extensions COMPLETE (2026-05-28)
Build: 0 errors, 0 warnings. Tests: 148/148 unit + 1/1 integration = 149 total, all passing.
Migrations applied: AddRaidSystem, AddStatInvestmentFields, AddQuestDifficultySystem, AddItemSystem, AddRaidDifficulty, AddPlayerClass

## Session 2026-05-28 — XP Formula + Class System (Sections A+B)
Section A — XP Formula:
- Player.AddExperience: new carry-over signature (long, Func<int,int>) returns level list
- LevelingConfig: 30×level^0.7 formula with milestone floors; registered from appsettings.json
- XpToNextLevel(int) on IStatService/StatService; floors only constrain at L500+ and L1000+
- QuestService + RaidService: level-up firing per returned level via AddExperience
- XP progress fields added to QuestResultResponse and RaidRewards DTOs
- 105 tests (was 96, +9 XpFormula + 4 QuestLevelUp)
Section B — Class System:
- PlayerClass enum: Tier 1-5 (Conscript, Tier2 paths, Tier3 specs, Legendary, Ascendant)
  + Tier 6-11 convergence: Luminary(L2000), Immortal(L5000), Archon(L7500),
    Ancient(L10000), ElderAncient(L15000), Eternal(L25000)
- Player.Class + SetClass; migration AddPlayerClass applied
- ClassConfig: regen lookup (strips Legendary/Ascendant prefix), convergence level map
- IClassService + ClassService: GetAvailableChoices, ComputeAutoAdvance, AssignClassAsync,
  GetRegenRates, IsConvergedClass; auto-advance fires in StatService.GrantLevelUpPointsAsync
- ClassDTOs: ClassRegenRates, ChooseClassRequest
- StatController: GET /api/stats/class, POST /api/stats/class/choose
- 149 tests (was 105, +43 ClassServiceTests +1 StatServiceTests mock update)
Pre-build tasks:
- docs/ROTA_Function_Reference.md generated (all interfaces, controllers, entities, enums, P2 backlog)
- Verbose comment labels stripped from all 101 src/ .cs files (excl. migrations)
- MilestoneFloors updated with convergence gates: 2000, 7500, 15000, 25000

## v0.2.5 — Conditional / Stacking Bonuses (2026-05-30)
Build: 0 errors, 0 warnings. Tests: 232 unit + 7 integration = 239 total, all passing.
- ConditionalBonus model + ConditionType/BonusType enums (JSON-only, no C# to add bonuses)
- ConditionalBonusEvaluator static evaluator (shared by gear + future legions)
- 5 bonus types: FlatAttack, FlatDefense, ProcChanceFlat (≤1.0), ProcAmountFlat, FlatDamagePercent
- 3 condition types: OwnedUnitCount, OwnedTypeCount, EquippedSlot
- GearDefinition.ConditionalBonuses; ItemDefinition.Tags
- EffectiveCombatData gains FlatDamagePercent field (applied after crit)
- EquipmentService: loads inventory per-hit, evaluates all equipped gear's conditional bonuses
- Reward atomicity: stamina spend moved inside advisory-lock tx (atomic with hit; no refund path)
- ProcBonus type: double → long in RaidHitResponse
- Function Reference fully refreshed; spec in docs/specs/shipped/system-13-stacking-bonuses.md

## System 19 — Raid Sharing (2026-06-04) — COMPLETE (3 slices)
Raids are PRIVATE until shared to the public list. Spec: docs/specs/shipped/system-19-raid-sharing.md
- Backend: ActiveRaid.IsPublic (default false) + Share() domain method; migration AddRaidVisibility.
  GET /api/raids/{id} (join-by-UID; 404 hides others' Personal raids); POST /api/raids/{id}/share
  (summoner-only, audited; 403 not-summoner / 404 not-found / 409 Personal). List filter =
  (IsPublic && Size != Personal) || own. Hit-access gate unchanged. Sigils summon Small (shareable).
- DTOs: ActiveRaidResponse.IsPublic; ShareRaidResult + ShareRaidFailureCode {None,NotFound,
  NotSummoner,CannotSharePersonal}.
- Client (ROTA.Client6 master): Share panel in RaidCombatView (summoner-only, non-Personal) —
  UID copy + "Share to public list" → flips to "Shared ✓"; Join-by-UID card on the Public tab;
  PRIVATE badge on own unshared cards.

## System 20 — Quest Node Depletion + Discernment Drops (2026-06-05) — COMPLETE (4 slices)
Spec: docs/specs/shipped/system-20-quest-depletion-drops.md. 476 unit + 35 integration green.
- Node depletion: PlayerQuestProgress.Progress (starts 100) + IsCleared + Deplete(); each attempt
  drains the node (battle −5, boss −2.5, QuestConfig-driven). Reaching 0 latches IsCleared.
  Migration AddQuestNodeProgress auto-clears already-completed nodes. NOTE: T26 (2026-06-06) later
  reversed "cleared nodes stay replayable" → cleared nodes now LOCK until a chapter-boss reset; unlock
  gating moved to the permanent HasEverCleared latch. See the "Level-up cluster + correctness" entry.
- Discernment drops: the dormant quest loot pipeline is now wired (lootTableId on all 5 quests + 5
  type:"Quest" loot tables). ProcessQuestLootAsync scales each chance drop by Discernment
  (base × (1 + Disc×0.03), cap 0.95 that never lowers a high base); guaranteed drops unaffected.
- Pano set: 8 Orange "Pano's …" questing-set gear pieces (gear.json) distributed across the quest
  loot tables at difficulty-scaled rates. Set bonus stays PHASE-2.
- DTOs: QuestAvailabilityResponse.{Progress,IsCleared}; QuestResultResponse.{NodeProgress,
  NodeCleared,NodeJustCleared}.
- Client (ROTA.Client6 master): quest cards show a depletion bar (amber→green "CLEARED ✓"/"LOCKED")
  + "node cleared" callout; mock quests made stateful so the bar moves in mock mode.

## Level-up cluster + correctness bugs (T20-T29, 2026-06-06)
- T22/T24: GrantLevelUpPointsAsync (the quest+raid chokepoint) now fully refills all resource pools
  (new IEnergyService.RefillToMaxAsync) and syncs GuildStamina max 1:1 to the new level on each
  level-up (GuildStamina was seeded at max 1 and never updated — DTO showed level, pool stayed 1).
- T20/T21 (client): LevelUpOverlay (tap-to-dismiss congrats) + MilestoneBanner (sweep every 2500
  levels), driven by PlayerState.NotifyLevelUp; mock seeded near a milestone (Level 2498) for testing.
- T29 (client): HeaderBar regen ticker — server regen was always correct, but the header only updated
  on profile re-fetch, so bars looked frozen. Now advances displayed values per-second from the
  server's RegenMinutesPerPoint/SecondsToNextPoint, reconciling on each fetch. Mock regenerates too.
- T26: chapter-boss RESET CYCLE — REVERSES System 20's "cleared nodes stay replayable". Clearing a
  node now LOCKS it (server rejects with QuestFailureCode.NodeCleared → 409); completing a chapter
  boss resets that whole chapter's nodes to fresh (deplete→clear→boss→reset). PlayerQuestProgress
  split into resettable IsCleared (attemptability) + permanent HasEverCleared (unlock gating, so a
  reset never re-locks earned progression). Migration AddQuestEverCleared (backfill = is_cleared).
  DTOs: QuestResultResponse.ChapterReset; QuestFailureCode.NodeCleared. Client: Attempt disabled on
  cleared nodes + "⟳ CHAPTER RESET" callout.

## Class regen preview (T7, 2026-06-05)
ClassRegenRates gains ChoicePreviews (per-available-class regen rates) so the client's auto-triggered
class-unlock overlay shows each option's benefit. Client: class selection removed from the Profile
screen (inline card) and now surfaces as an overlay when a new tier unlocks.

## Phase 2 — Ops & Social (T30–T40) — BACKEND COMPLETE (2026-06-06)
Build: 0 errors. Tests: **526 unit + 35 integration = 561, all green** (incl. an adversarial
multi-agent review pass + fixes). Spec: docs/specs/active/phase-2-ops-social.md. Branch:
feat/phase2-ops-social (off main). Migrations applied: AddOutboundEmails, AddPlayerMute,
AddPinnacleFirstClaims, AddSocialSystem, FriendshipPartialUniqueIndex. All 7 design decisions
were resolved up-front (see spec §6). Review hardening: friendships use a partial unique index
(re-friend after unfriend) + conflict-safe insert; muted/banned players can't PM or chat; a block
hides PM history. Accepted-as-noted: chat mute is a per-message DB hit (PHASE-2 Redis cache);
report rate-limit consumed before checks (anti-abuse); Email:Enabled defaults true (owner has creds).

- **T39 — Operator email backbone (FOUNDATION):** `IEmailService`/`SmtpEmailService` (Gmail SMTP;
  creds in user-secrets `Email:Username`/`Email:Password` — NOTE: decision #1 said SendGrid, but the
  owner supplied Gmail SMTP creds, so the working provider is SMTP/Gmail behind the same interface;
  SendGrid remains the documented swap). `outbound_emails` table (jsonb detail/metadata, send+review
  status) is the dashboard's source of truth. `EmailNotificationService.QueueAsync` = persist-first +
  audit + enqueue; `EmailSendQueue` (Channel) + `EmailSendBackgroundService` (hosted) send out-of-band
  and swallow failures (never blocks gameplay). `EmailType {BugReport,PlayerReport,ModerationAction,
  PinnacleFirstClaim,GeneralTicket}`; `EmailPayload` is the one shape all producers build.
  `OpsController [AdminOnly]`: GET /api/admin/emails (list+filter), /stats, /{id}, POST {id}/approve|
  dismiss; GET /api/admin/pinnacle-claims. **The React ops dashboard is a SEPARATE repo at
  C:\Dev\rota-ops-dashboard** (demo-mode default; admin JWT login).
- **T40 — Moderation:** `Player.Mute(expiresAt)/Unmute()/IsMuted` (derived, Ignore-mapped) +
  mute_expires_at. `AdminService.BanPlayerAsync/MutePlayerAsync/UnmutePlayerAsync` (Mod-or-Admin,
  cannot target an admin, ban revokes sessions) → audit + ModerationAction email.
  `ModerationController [ModeratorOrAdmin]` /api/moderation/players/{id}/ban|mute|unmute. Stat-rollback
  stays PHASE-2.
- **T30:** AllocateStatPointAsync credits the gained Energy/Stamina delta to current via
  RefillEnergyAsync (not a full refill).
- **T32:** `GemTransactionType.PinnacleReward`; GrantLevelUpPointsAsync grants pinnacle gems
  idempotently from `LevelingConfig.PinnacleGemRewards` (1000:250, 2500:500, 5000:1500, 7500:2000,
  10000:2500). Class-gate levels stay `ConvergenceLevels` (decision #4); 2000/15000/25000 gem amounts
  omitted pending owner confirmation.
- **T33:** `PinnacleFirstClaim` + unique index on pinnacle_level (atomic first-claim);
  `PinnacleService.RecordFirstClaimAsync` → audit + PinnacleFirstClaim email on first claim only;
  wired into GrantLevelUpPointsAsync. `LevelingConfig.IsPinnacleLevel` is the single pinnacle-level
  source. content/magics.json: 5 inert Orange placeholder magics (5000–25000).
- **T38:** `FeedbackController [Authorize]` POST /api/feedback (Bug→BugReport, Feedback→GeneralTicket
  email) with game-state snapshot. `ISubmissionRateLimiter` (Redis, reusable): 5/hr per player + 15/hr
  per IP → 429.
- **T37:** Friendship/PlayerBlock/PrivateMessage + `SocialService` (friend request/accept/remove,
  block/unblock, friends-only block-gated PM, GetConversation, ReportPlayer→PlayerReport email,
  rate-limited). `SocialController [Authorize]` /api/social/*; PM delivered live via the chat hub.
- **T35/T36 — Chat (SignalR):** `ChatHub` at /hubs/chat (JWT-over-querystring already wired) —
  SendWorldMessage (broadcast + 100-msg Redis ring buffer `RedisWorldChatStore`), JoinRaid/
  SendRaidMessage (ephemeral per-raid group). Muted players (T40) rejected. `SenderRole` carried for
  reserved-name colouring. `SubUserIdProvider` maps SignalR identity → JWT sub (needed for PM).
  GET /api/chat/world/history backfill.
- **CLIENT (Unity) — BUILT + VERIFIED-COMPILING** on branch `feat/phase2-client-plumbing` (local-only,
  off master, UNMERGED; headless compile exit 0, zero `error CS`). DTO mirror + IRotaApi/Http/Mock
  plumbing (commit b49d955) + UI (commit c4f9882): **T31** profile scrollbar, **T38** bug/feedback panel
  (HeaderBar 🐞), **T37** SocialScreen (friends, **PM over REST**, blocks, report dialog; nav entry),
  **T34** raid layout restructure (combat log bottom-left, compact actions, leaderboard 5→10), **T32**
  pinnacle gem callout on LevelUpOverlay, **T36** world-chat read-only panel + HeaderBar 💬 unread dot.
  Mock paths are stateful. Merge to master when ready.
- **CLIENT DEFERRED (need a Unity SignalR client → /hubs/chat):** **T35** raid chat, and public
  **world/raid chat SEND** (send box is present-but-disabled "Live chat coming soon"). Private messaging
  is unaffected — it rides REST. Wiring a SignalR client lights up real-time world/raid chat + live PM
  push in one follow-up.
- **OPEN PLAYTEST BUGS (client/mock fidelity, 2026-06-06)** — see `docs/SESSION_HANDOFF.md` §A for
  root-cause + fix locations: (1) alloc doesn't credit current bar (MockRotaApi.AllocateStatAsync omits
  the LiveValue bump T30 does live); (2) HeaderBar bars drift from server truth (ticker reconcile);
  (3) Hit ×20 allowed with 10 stamina (RaidCombatView gates on defeated-only, not stamina; mock doesn't
  enforce). Live backend is authoritative + tested — these are client display + MockRotaApi fidelity gaps.

## PHASE-2 Deferred Items
- DiscernmentInvestment effect: quest drop quality (raid crit shipped v0.2.3)
- Explicit DB transaction scope for QUEST reward steps (energy committed but rewards not atomic; raids fixed v0.2.5)
- Consumable item type: potions and buffs
- Crafting system: Material → Equipment recipes
- Guild system: GuildStamina, guild raids
- Gear set bonuses (deferred from v0.2.4 gear)
- Phase 2 migration: split loot table format for quest/raid clarity
- Content pipeline refactor: IContentLoader, folder structure, sigil templates, formula-based loot tables

## Phase 1 Extensions — Design Decisions (Pre-Build)
All of the following are confirmed and locked. Build against
these specs exactly.

### Skill Points & LSI
- 10 SkillPoints granted per level-up (not 3)
- LSI formula: (EnergyInvestment + (StaminaInvestment × 2))
  / Player.Level
- LSI cap: 9.0 (player-investable, server-enforced)
- Gear and item bonuses CAN push effective LSI above 9.0
- MaxEnergy = 10 + EnergyInvestment
- MaxStamina = 10 + StaminaInvestment
- MaxGuildStamina = Player.Level exactly (1:1, no investment)
- Discernment (renamed from Perception): no LSI cap,
  invest freely, affects quest drop quality and raid
  critical damage bonus (// PHASE-2 for bonus effects)

### Gems on Level-Up
- Every 5 levels: 5 gems granted (levels 5, 10, 15, 20...)
- ReferenceId = "levelup:gems:{playerId}:{level}"
- Idempotent via GemTransactionType.LevelUpReward

### Quest Difficulty System
Four difficulties (mirrors DotD exactly):
  Normal    — Green   — energy ×1.0 — rewards ×1.0
  Hard      — Yellow  — energy ×1.5 — rewards ×1.5
  Legendary — Red     — energy ×2.0 — rewards ×2.0
  Nightmare — Purple  — energy ×3.0 — rewards ×3.5
Note: Nightmare reward ×3.5 vs cost ×3.0 — intentional,
      rewards players who push to hardest content.

Unlock gates (server-enforced):
  Hard:      requires Normal CompletionCount >= 1
  Legendary: requires Hard CompletionCount >= 1
  Nightmare: requires Legendary CompletionCount >= 1

XP-per-energy ratio scales with zone index:
  baseRatio = 1.2 + (zoneIndex × 0.05)
  Boss quests always use ratio 2.0 regardless of zone
  Difficulty reward multiplier applied on top.

### Raid Difficulty System
Four difficulties with HP multipliers (from DotD wiki data):
  Normal:    baseHp × 1.0  — Green
  Hard:      baseHp × 1.4  — Yellow
  Legendary: baseHp × 2.0  — Red
  Nightmare: baseHp × 3.6  — Purple

Raids defined with baseHp only in raids.json.
Server applies multiplier at summon time.
No hardcoded HP per difficulty in JSON.

### Contribution Tier Multipliers
Applied to all rewards (gold, XP, gems, stat points, items):
  Legendary Rank 1: ×1.50 (top damage dealer)
  Legendary Rank 2: ×1.25
  Legendary Rank 3: ×1.10
  Epic (top 10%):   ×1.00
  Rare (threshold): ×0.75
  Participant:      ×0.25
Gems granted to Rare and above only.
Highest tier wins when a player qualifies for multiple.

### Item Rarity System
  Grey=0, White=1, Green=2, Blue=3, Purple=4, Orange=5
Orange is the permanent ceiling. Never add above it.

### Item Types
  Equipment  — wearable gear (Phase 2 stats)
  Material   — crafting ingredient, no direct stats
  StatBag    — consumable, grants unassigned SkillPoints
  Sigil      — summons a raid at a specific difficulty
  Consumable — potions, buffs (Phase 2)

### Sigil System (renamed from Essence/Scroll)
- Sigils are dropped by quest bosses only
- One Sigil type per boss per difficulty (4 per boss)
- First defeat on a difficulty: guaranteed Sigil drop
- Subsequent defeats: chance-based (defined in quest JSON)
- Using a Sigil summons that boss at that difficulty
- Sigil rarity matches difficulty:
    Normal=White, Hard=Green, Legendary=Blue, Nightmare=Purple

### Loot Tables
- Per-difficulty tiers within each loot table
- Threshold rewards are CUMULATIVE (5% gives 0.1%+1%+5%)
- Attack/Defense/Discernment stat points:
    World and Event raids ONLY — never Standard or Guild
- On-hit drops: World and Event raids ONLY
    Standard raids always have hasOnHitDrops: false
- Startup validation: server throws on misconfigured tables

### Stat Point Rewards from Raids
- Unassigned SkillPoints: all raid tiers, all difficulties
- Attack/Defense/Discernment points: World/Event only,
    higher contribution thresholds only (not floor tier)
- Materials and StatBags: all raids, scales with difficulty
- Items (Equipment): future content additions via JSON

### GuildStamina
  MaxGuildStamina = Player.Level × 1 (exact, no investment)

---

## Documentation Index
- [Docs index](docs/README.md) — what every `.md` in the repo is for (start here)
- [Game Design & Unity UI Reference](docs/ui/ROTA_GameDesign_UI_Reference.md) — DotD mechanics analysis, screen-by-screen UI blueprints, Unity implementation prompt, content pipeline guide
- [Operations & Tooling Runbook](docs/OPERATIONS.md) — every dotnet command, the admin CLI, admin REST API, config flags, secrets, migrations, deployment order, beta onboarding
- [Design North Star](docs/DESIGN_NORTHSTAR.md) — durable design vision; research-paper divergences recorded as amendments (no resets, capped scaling, Gauntlet as core spine)
- [System specs](docs/specs/README.md) — per-system build specs, organized `shipped/` · `active/` · `backlog/` (index resolves the "System 13" naming collision)

## Function Reference
Full method signatures, entity fields, endpoint map:
docs/ROTA_Function_Reference.md
Read this at session start instead of opening
individual files when planning where to make changes.
PHASE-2 backlog is tracked at the bottom of this file.

## Run commands
docker-compose up -d                    ← start postgres + redis
dotnet build                            ← build check
dotnet test                             ← run all tests  
dotnet run --project src/ROTA.Api       ← run server
dotnet ef migrations add <Name> --project src/ROTA.Infrastructure --startup-project src/ROTA.Api
dotnet ef database update --project src/ROTA.Infrastructure --startup-project src/ROTA.Api

## Admin CLI commands (replace Kestrel, no HTTP server started)
dotnet run --project src/ROTA.Api -- seed-admin                    ← create Xolaces admin (reads Seed:AdminPassword)
dotnet run --project src/ROTA.Api -- gen-beta-key [count]          ← generate 1..100 ROTA-XXXX-XXXX-XXXX keys
dotnet run --project src/ROTA.Api -- promote <user|guid> <Role>    ← grant Admin or Moderator role
dotnet run --project src/ROTA.Api -- demote  <user|guid> <Role>    ← revoke Admin or Moderator role
dotnet run --project src/ROTA.Api -- leaderboard-refresh-stat      ← refresh StatAttack/StatDefense/StatDiscernment Live boards