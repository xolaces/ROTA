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
  never hardcoded; default email admin@rota.local), wired in Program.cs pre-Run,
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
  Seed:AdminEmail           default admin@rota.local

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

## System 16 — Gauntlet (2026-06-07) — COMPLETE (7 slices, merged to main)
Build: 0 errors. Tests: 737 green at merge. Spec: docs/specs/active/system-16-gauntlet.md. Branches
v0.2.9-gauntlet-s1..s6 + -loop (tagged + pushed). Migration AddGauntletSystem (NOT applied). The
**individual** competitive pillar — your power → your placement. Reuses the raid engine (NO parallel combat
path: every amplifier gates on `ActiveRaid.GauntletEventId`).
- League-locked by convergence tier (Whelpling/Wyrm/Dragon); join + gem→strikes buy (idempotent).
- 7 entities incl. append-only StrikeTransaction + GauntletCurrencyTransaction (Token/Pitchfork) ledgers.
- Combat fork in RaidService.HitRaidAsync: trophy mult (highest-only, pre-PowerScaling); off-cap
  Wrath/Blessing auras (current-owner ×1.25 / former-honor ×1.10, outside MaxAggregateProcBonus, pre-crit);
  strike-spend fork (Strikes not Stamina, tx-safe raw-SQL StrikeRepository.SpendAsync); score hook.
- Idempotent settlement (ledger + unique-index; honor write-back); token shop; finite-6-stage auto-advance
  ladder (GET /api/gauntlet/ladder lazy-spawn) + rank-magic hand-off on open. CLI gauntlet-open/close/settle.
- KNOWN FOLLOW-UPS: ladder double-spawn race (per-player lock / partial unique index); finite ladder ceiling
  tunable via gauntlet_raids.json stage count.

## System 21 — Guild / Clan Foundations (2026-06-07) — S1+S2 merged to main; S3a+S3b on branches
Spec: docs/specs/active/system-21-guild-foundations.md (§5 LOCKED). The **collective** pillar (vs the
Gauntlet's individual one). Memory: guild-foundations-decisions.md.
- **S1 core/membership/join** [merged]: Guild/GuildMembership/GuildJoinRequest + 4 enums + GuildConfig +
  GuildService (create [gold + L20 gate], disband, per-guild join policy Open/Application/InviteOnly,
  apply/accept/invite/leave/kick/promote/demote/transfer, inactivity succession, roster/browse) +
  GuildController (14 endpoints). One-guild-per-player + ci name/tag uniqueness via partial unique indexes
  (the friendship lesson). Permission: actor.Rank>target.Rank ∧ newRank<actor.Rank (only Leader sets ranks).
- **S2 chat** [merged]: ChatHub guild channel (mute-gate → member-gate, per-guild group) +
  RedisGuildChatStore (100-msg ring) + GET /api/chat/guild/history. Unity SignalR client deferred.
- **S3a sigil economy** [branch feat/system21-guild-s3a-sigil-economy]: append-only discriminated per-player
  ledger (GuildCurrency {Sigil,ShopTicket}) + per-guild sigil-pool ledger. Daily claim (sigil + ticket
  allowance, idempotent guildclaim/guildticket:{p}:{date}); buy (spends tickets, ≤3/day); donate
  (personal→pool, ≤3/day, atomic cross-table); balances. GuildSigilController api/guilds/sigils. Migration
  AddGuildSigilEconomy. GuildConfig tunables (DailySigilClaimAmount/DailyTicketGrantAmount/SigilShopTicketPrice/
  DailyBuyCap/DailyDonateCap).
- **S3b guild raids** [branch feat/system21-guild-s3b-guild-raids, stacked on S3a]: `ActiveRaid.GuildId`
  fork — NO parallel combat path. RaidService.HitRaidAsync gates to guild members, spends GuildStamina = hit
  size (1/5/20) inside the advisory-lock tx (the first GuildStamina sink), accrues
  GuildMembership.ContributionTotal; rewards via the existing contribution-tier engine. SummonGuildRaidAsync
  (officer-gated; consumes 1 pooled sigil via raw-SQL balance-guarded TrySpendPoolAsync). content/guild_raids.json
  (3 bosses, Tier="Guild", lootTableId="" → gold/XP/gem tier rewards; item loot a follow-up). GuildRaidController
  api/guilds/raids (list + summon); members hit via the existing /api/raids/{id}/hit. Migration AddGuildRaidLink.
- At S3b: **722 unit + 84 integration = 806 green; 0 errors, 0 code warnings.** Migrations NOT applied
  (AddGuildSystem, AddGuildSigilEconomy, AddGuildRaidLink) — owner coordinates `dotnet ef database update`.
- KNOWN FOLLOW-UPS: inactivity-succession scheduled auto-driver; guild-raid summon pool-debit + raid-insert
  not in one tx (debit is atomic, no overspend — accepted Phase-2 pattern); guild-raid item loot tables;
  confirm tunable balances (CreationGoldCost, daily caps, ticket allowance).

## System 22 — Masteries Core (Phase A, 2026-06-08) — COMPLETE (7 slices, merged to main)
Spec: docs/specs/active/system-22-masteries-core.md. The **individual horizontal-progression spine**: 4 Ancients
(**Wrath** +% legion power · **Bulwark** +% guild-raid dmg, ~1% cap · **Hoard** +% drop/gold · **Discernment** +%
drop-quality + sigil-find), each leveled 1→5 by per-Ancient challenge checklists, with an always-on global + a pledge
(≈×2) modifier — all through EXISTING combat/loot hooks, **NO new combat path**. Modifiers are a dedicated
`IMasteryService` (per-player DB level-state), NOT `ConditionalBonus` rows.
- **S1** content/defs: `MasteryAncient`/`MasteryActivityType` enums, `AncientDefinition`+tier-challenge models,
  `MasteryConfig`, `IMasteryDefinitionProvider` (eager singleton, startup-validated), `content/masteries.json`.
- **S2** state+read: `PlayerMastery`/`PlayerMasteryActivity`/`MasteryActivityEvent` + `Player.ActivePledgeAncient` +
  migration **AddMasterySystem**; `GET /api/masteries`; Formula-B `ComputeRating` (Active==Lifetime, monotonic) +
  derived titles; `LeaderboardBoard.MasteryRating{Active,Lifetime}` + admin refresh + CLI `mastery-refresh-rating`;
  profile gains ActivePledge + MasteryRatingActive.
- **S3** re-spec: `MasteryRespecTransaction` ledger + migration **AddMasteryRespecLedger**; `POST /api/masteries/pledge`
  — LOSSLESS (free first-pledge-per-Ancient → free monthly → paid weekly: Redis cap `IMasteryRespecCapStore` + idempotent
  `GemTransactionType.MasteryRespec=13`). Only flips the pledge; levels never touched.
- **S4** leveling: `RecordActivityAsync` (raw ON CONFLICT increment + idempotency event ledger) wired at 8 chokepoints
  (raid hit/kill/guild-contribution/gold enlisted; quest node/boss/gold + GauntletRank settle best-effort);
  off-hot-path tier-up evaluation (on read + post-quest).
- **S5** combat: Wrath into `totalLegionBonus` (single-touch, active-legion-gated, never Gauntlet); Bulwark into
  `FlatDamagePercent` gated `lockedRaid.GuildId!=null` (hard-capped). Mastery-less hit byte-for-byte unchanged.
- **S6** loot: combined `GetModifiersAsync` (one read/hit); Hoard drop-rate (quest `Scale`) + gold (quest+raid on-hit);
  Discernment sigil-find (post-first-clear, clamp ≤1.0). 
- **S7** drop-quality: opt-in `Item/GearDefinition.UpgradesTo` (startup-validated: resolves + strictly-higher rarity ≤
  Orange); quest item chance-drops roll a Discernment-scaled rarity-upgrade; starter ladder seeded.
- **786 unit + 88 integration = 874 green; 0 errors, 0 CS warnings.** Migrations **AddMasterySystem** +
  **AddMasteryRespecLedger** NOT applied — owner runs `dotnet ef database update`.
- KNOWN FOLLOW-UPS: raid threshold-drop Hoard scaling + gear/raid quality-upgrade wiring (deferred — per-participant
  kill-loop reads / Orange-ceiling gear); TUNE magnitudes + challenge thresholds + the off-by-default breadth
  micro-bonus; paid-respec crash-recovery gap (strict weekly cap, PHASE-2 note). Phase B (The Rise) + Phase C
  (PoE-depth) stay in backlog.

## Ticket 52 — Subject Enforcement + Email Priority (2026-06-08) — BACKEND COMPLETE (migration NOT applied)
Replaces free-typed subjects with server-validated, config-driven subject lists for Bug + Player reports; Feedback
stays open-text but is always filed under a fixed "Player Feedback" category. Each outbound email now carries a
derived priority. Tests: 847 unit + 97 integration green; 0 errors, 0 src warnings.
- **Config:** `content/subjects.json` ({ bugSubjects, reportSubjects [{key,label}], feedbackCategory }) is the single
  source of truth, loaded by an eager startup-validated singleton `ISubjectCatalogProvider`/`SubjectCatalogProvider`
  (mirrors MagicDefinitionProvider — throws at boot on empty list / duplicate key / blank feedbackCategory).
- **Priority (`EmailPriority {Low=0,Normal=1,High=2}`):** PlayerReport=High, GeneralTicket(feedback)=Low, BugReport=Normal.
  `OutboundEmail.Priority` + `EmailPayload.Priority` + `priority` int column (migration **AddEmailPriority**, DB default
  Normal, `HasSentinel((EmailPriority)(-1))` so explicit Low(0) still writes; IX_outbound_emails_priority).
- **Validation:** `FeedbackRequestValidator`/`ReportPlayerRequestValidator` inject the provider — Bug subject + report
  reason must be on-list (accept key OR label → 400 otherwise); Feedback subject stays open text.
- **Normalization:** the LABEL is stored in `OutboundEmail.Subject`; the `subjectKey` is stashed in the detail jsonb.
- **Endpoint:** `[Authorize] GET /api/subjects` → `SubjectCatalogResponse` (client wiring is a later step).
- **Ops:** `IOutboundEmailRepository.ListAsync` gains `EmailPriority? priority` filter + `string? sort`
  ("priority"|"created"); default sort = priority-first then newest. `OpsController.List` accepts `priority`+`sort`
  query params; `OutboundEmailResponse.Priority` is the string name ("Low"/"Normal"/"High").
- KNOWN FOLLOW-UPS: client (Unity) + ops-dashboard wiring to GET /api/subjects + the new priority column (later steps).

## System 23 / Ticket 50 — Raid Visibility & Indexing model (2026-06-08) — BACKEND COMPLETE (migration NOT applied)
Spec: docs/specs/active/system-23-raid-visibility-lifecycle.md. Replaces the boolean `ActiveRaid.IsPublic`
with a **`RaidVisibility` tier enum** {Private=0,Public=1,GuildOnly=2,FriendsOnly=3} and adds a
completed→`Lootable`→`Looted` lifecycle (**`RaidLifecycleState`** {Active=0,Lootable=1,Looted=2}). Makes the
**"active raid (alive + hittable by its GUID) ≠ public raid (indexed in a list)"** split explicit in code.
- **CRITICAL (verified):** raid rewards are FULLY granted on the killing hit (`DistributeKillRewardsAsync`) —
  there is NO unclaimed-reward state — so **`Loot()` is a DISMISS / remove-from-all-indexes action, NOT a
  reward claim.** `MarkDefeated()` also flips lifecycle Active→Lootable; `Loot()` (guarded Lootable-only)
  flips Lootable→Looted and does NOT soft-delete (`IsDeleted` stays false → raid_participants FK + history intact).
- `ActiveRaid`: `Visibility`+`LifecycleState` (private set) replace `IsPublic`; `Create(...visibility=Private)`;
  `ShareTo(RaidVisibility)` + `Share()` back-compat overload (→Public); `Loot()`.
- `RaidService`: injects `IFriendshipRepository` (accepted-friends via
  `ListForPlayerAsync(playerId, FriendshipStatus.Accepted)` → `Friendship.OtherSide`). `GetActiveRaidsAsync`
  resolves caller guildId + accepted-friend set ONCE, then lists `LifecycleState==Active && (own || (non-Personal
  && (Public || GuildOnly&same-guild via Include-loaded SummonedByPlayer.GuildId || FriendsOnly&accepted-friend)))`.
  `ShareRaidAsync(callerId, raidId, visibility=Public)` (GuildOnly validates summoner in a guild→`NotInGuild`;
  Personal→`CannotSharePersonal`; Private coerced→Public). NEW `LootRaidAsync` (summoner-only; NotFound/NotSummoner/
  NotLootable). `GetRaidByIdAsync` lets the summoner resolve their own `Lootable` raid; `Looted`→null.
- API: `POST /api/raids/{id}/share` body `ShareRaidRequest{Visibility="Public"}` **optional** (no body→Public,
  back-compat) → 200/400/403/404/409(Personal or NotInGuild); NEW `POST /api/raids/{id}/loot` → 200/403/404/409.
- DTOs: `ActiveRaidResponse += Visibility, LifecycleState` (KEEP derived `IsPublic = Visibility==Public` on the
  wire — shipped client unaffected). `ShareRaidFailureCode += NotInGuild=4`. NEW `ShareRaidRequest`, `LootRaidResult`
  + `LootRaidFailureCode {None,NotFound,NotSummoner,NotLootable}`.
- EF: `visibility`+`lifecycle_state` (int, store defaults 0); filtered index `ix_active_raids_visibility_lifecycle`
  on (visibility, lifecycle_state) WHERE is_defeated=false AND is_deleted=false. Migration **AddRaidVisibilityModel**
  (add visibility→backfill is_public→drop is_public; add lifecycle_state→backfill is_defeated→Looted; index).
  **NOT applied** — owner runs `dotnet ef database update`.
- **830 unit + 94 integration green; 0 errors, 0 CS warnings.** CLIENT MIRROR (ROTA.Client6) is a SEPARATE later step.

## T43 — Dev Guild "The Dev Coffee Shop" (2026-06-08) — COMPLETE (backend, NO migration)
The hidden developers-only guild. NO schema change — the new `PlayerRoles.Developer = 1 << 3` flag is a
plain bit in the existing int `roles` column, and the guild reuses the Guild/GuildMembership tables.
- **Enum/config:** `PlayerRoles.Developer`; new `DeveloperConfig` (bound from `Developer` section:
  `Usernames[]` + `PlayerIds[]`, **EMPTY by default** — owner adds the owner's identifier later, nothing
  hardcoded); `GuildConfig.DevGuildTag="DEV"`/`DevGuildName="The Dev Coffee Shop"`/`DevGuildDescription`.
- **Seeding:** `SeedData.EnsureDevGuildAsync` (wired in Program.cs right after EnsureAdminAsync) —
  idempotently grants the Developer flag to allowlisted accounts (by username AND guid), ensures the Dev
  guild exists (created **led by the first resolvable dev**, JoinPolicy=InviteOnly), and auto-joins devs.
  OWNER DECISION: if NO dev account resolves, guild creation is SKIPPED with a warning (a guild needs a
  non-null leader FK and we must never flag/lock the seeded admin Owner into it) — it auto-seeds once a
  dev is added to config and the server restarts. Audit actions: DevFlagGranted / DevGuildSeeded /
  DevGuildJoined (+ DevFlagRevoked on unflag).
- **Visibility:** `GuildRepository.BrowseAsync` (injects IOptions<GuildConfig>) excludes the dev tag
  BEFORE Skip/Take so paging stays correct; `GuildService.GetGuildAsync` returns null (→404) for the dev
  guild when the caller lacks the Developer flag (hide existence).
- **Server gates (DB-HasRole-based, not JWT-claim):** new `GuildFailureCode.DevGuildRestricted=14` →403.
  Dev actors can't create guilds; Apply/AcceptInvite/Invite/AcceptApplication enforce both sides — a dev
  may ONLY belong to the Dev guild, and a non-dev can NEVER enter it.
- **CLI:** `flag-dev <user|guid>` (grants flag + ensures guild + auto-joins) / `unflag-dev <user|guid>`
  (removes from dev guild + revokes flag) via `SeedData.FlagDeveloperAsync`.
- JWT: AuthService already emits a role claim per set flag, so Developer surfaces automatically.
- Tests: 7 GuildService gate tests + 4 EnsureDevGuildAsync tests (create/idempotent/by-username+guid/
  empty-allowlist no-op) + 1 BrowseAsync exclusion (real Postgres). **887 green (794 unit + 93 integration),
  0 errors, 0 CS warnings.** No EF migration. Developer allowlist: OWNER DECISION 2026-06-11 — `Developer.Usernames:["Owner"]`
  is INTENTIONAL (Owner stays flagged Developer + in the Dev guild); the owner's identifier still pending.

## T44 + T45 — Chapter/Zone map + XP rebalance (2026-06-08) — COMPLETE (one coupled job)
The questing spine becomes a data-driven **Chapter → Zone → Node** hierarchy and the previously-dead
zone-indexed XP formula is wired so XP scales by chapter + zone depth (early LOW, late HIGH). NO EF
migration (zone membership lives entirely in JSON; PlayerQuestProgress still keys on quest_id only).
- **T45 hierarchy/gating:** QuestDefinition + QuestAvailabilityResponse gain `ZoneIndex`/`ZoneName`/
  `NodeIndex` (0-based; boss is the last NodeIndex in its zone). Ordered chain node→zone→chapter is
  enforced by `prerequisiteQuestId` (node N requires N−1; a zone's first node requires the previous
  zone's boss; a chapter's first node requires the previous chapter's final boss). New **zone-boss
  gate** in AttemptQuestAsync (before any energy spend): a per-zone boss fails
  `QuestFailureCode.ZoneBossLocked=8` → **409** until every NON-boss node in its zone HasEverCleared.
  GetAvailableQuestsAsync greys a surfaced-but-zone-incomplete boss (`IsUnlocked=false`).
- **T44 XP formula:** `xp = ExperienceReward(base) × zoneRatio × chapterScalar × rewardMult`, where a
  battle's `zoneRatio = XpZoneRatioBase(1.2) + ZoneIndex×XpZoneRatioPerZone(0.05)` and a boss always
  uses `XpBossRatio(2.0)`. `QuestConfig.ChapterXpScalars {1:1.0,2:1.6,3:2.6,4:4.2,5:7.0,6:11.0}`
  (appsettings-overridable). XP is NOT Hoard-scaled (only gold/drops are). ExperienceReward in
  quests.json is now the per-node BASE the ratio multiplies.
- **OWNER DECISION — REVISES T26:** the per-zone boss now resets only **its own ZONE** (not the whole
  chapter). `ResetChapterAsync→ResetZoneAsync` (filters Chapter && ZoneIndex);
  `QuestResultResponse.ChapterReset→ZoneReset`. HasEverCleared still preserved (forward unlocks survive).
- **content/quests.json — FULL rewrite:** 6 chapters / 25 zones / **136 nodes**. Legacy q001–q005 ids,
  names, loot-table refs (lt_quest_q001..q005) and sigils are preserved in place (Ch1 Z0 = q001/q002 +
  q003 boss; Ch2 Z0 = q004 + 2 new battles + q005 boss); all new nodes have `lootTableId: null`.
  Per-chapter Normal XP-per-node: Ch1 battle 50–90 / boss 180–200; Ch2 105–288 / 384–800; Ch3 218–245 /
  780; Ch4 453–510 / 1596; Ch5 924–1078 / 3220; Ch6 1716–2002 / 6160 — sane vs
  XpToNextLevel=round(30·level^0.7) (≈30 @L1 → ≈6135 @L2000).
- **Client (ROTA.Client6, local mirror — NOT compiled here):** Dtos.cs ChapterReset→ZoneReset + zone
  fields on QuestAvailabilityResponse; QuestScreen callout "ZONE RESET" + ZoneBossLocked copy;
  MockRotaApi made zone-aware (zone fields, zone-boss gate, zone-scoped reset) and stateful.
- **900 green (807 unit + 93 integration); 0 errors, 0 CS warnings.** Tests added: XP-formula theory
  (zone/chapter/difficulty), zone-boss gate (reject no-energy / succeed), cross-zone ordering, zone-reset
  scope (other zone untouched), availability zone fields + greyed boss. No new migration.

## Ticket 46 — Achievement Points (2026-06-08) — BACKEND COMPLETE (migration NOT applied)
Data-driven achievements mirroring System 22 Masteries' architecture (provider / ON-CONFLICT repos /
unique-violation-idempotent ledger / service / DI / controller). JSON content: category, tracked metric,
points, threshold, optional tier-chain `nextId`. Per-player counters tracked at the EXISTING combat/loot
chokepoints + a NEW days-played login hook. **TOTAL AP is SUMMED over an append-only `achievement_awards`
ledger** (gem-ledger discipline — one award row per achievement via a unique index), never stored.
Migration **AddAchievementSystem** (achievement_progress + achievement_awards + players.last_login_date +
players.days_played) **NOT applied** — owner runs `dotnet ef database update`. Client mirror (DTO + 3-way
API + ProfileScreen AP label) is a SEPARATE later step; only the Shared DTO field exists so far.
- **Enums:** `AchievementCategory {RaidCompletion,QuestClear,EquipmentOwned,DaysPlayed,Collector}`;
  `AchievementMetric {RaidCompletions,QuestNodesCleared,QuestBossesCleared,EquipmentPiecesOwned,DaysPlayed,CollectorItemCount}`.
- **Content/provider:** `content/achievements.json` (≥1 per category) + eager startup-validated
  `IAchievementDefinitionProvider` (throws on dup id / bad category-metric / points≤0 / threshold≤0 /
  dangling-or-cyclic NextId / non-increasing-or-metric-mismatched chain / missing CollectorKey).
- **Entities:** `AchievementProgress` (counter + IsCompleted/CompletedAt latch; UNIQUE player+achievement,
  ON-CONFLICT target) + append-only `AchievementAward` (Points + ReferenceId; UNIQUE player+achievement +
  FK index). `Player.LastLoginDate(DateOnly?)`+`DaysPlayed(int)`+`RecordLogin(today)` (increments only on
  a new UTC day).
- **Service:** `RecordProgressAsync` (delta; idempotent per (achievement,referenceId)), `SetCounterAsync`
  (absolute, EquipmentPiecesOwned), `RecountCollectorCountersAsync` (per-key distinct-owned count by item
  Type/Tags), `EvaluateCompletionsAsync` (awards once + latches + audits "AchievementUnlocked"),
  `GetForPlayerAsync`/`GetTotalPointsAsync`.
- **Hooks (best-effort):** RaidService isKill → RaidCompletions (idempotent `ach:raidkill:{raid}:{player}`,
  inside the advisory-lock tx); QuestService node/boss clear → QuestNodes/BossesCleared + evaluate; quest
  item grant (new distinct) → Collector recount; EquipmentService.GrantGearAsync → absolute
  EquipmentPiecesOwned recount; AuthService.LoginAsync → DaysPlayed once/day (`ach:day:{player}:{date}`);
  PlayerService.GetProfileAsync → evaluate + hydrate `PlayerProfileResponse.TotalAchievementPoints`.
- **Endpoint:** `[Authorize] GET /api/achievements` → `AchievementOverviewResponse {TotalPoints, Achievements[]}`.
- **885 unit + 102 integration = 987 green; 0 errors, 0 CS warnings.** DECISIONS: AP summed (not stored);
  EquipmentPiecesOwned + Collector RECOUNTED absolute on grant (no drift); ship AP-on-profile + endpoint now,
  defer browse screen; days-played = distinct UTC days via Player.RecordLogin; completed_at latches on first
  award. KNOWN FOLLOW-UPS: client mirror; TUNE rosters/points/thresholds; repeatable achievements (PHASE-2);
  raid item-loot / threshold-drop Collector hooks (only quest item grants recount Collector today).

## Tickets 53–58 — Playtest batch 2 (2026-06-08) — COMPLETE & GREEN (Unity compile owner-gated)
Build: 0 errors / 0 warnings. **991 tests pass (889 unit + 102 integration).** 2 new migrations (NOT applied):
`20260609043452_AddHealthResource` (T56, empty schema diff + data backfill) + `20260609131852_AddRaidParticipantPendingDrops`
(T57, adds `pending_drops_json` text column). Unity client not compiled here. Build order T53→T55→T58→T54→T56→T57.
Detail in docs/SESSION_HANDOFF.md.
- **T53** (client) — `MockRotaApi.AttemptQuestAsync` now deducts energy (was the real HUD↔profile desync that
  looked like a backend bug in mock playtest). Backend resource-sync + level-up refill confirmed correct.
- **T55** (backend+client) — Chapter/Zone quest navigator + **co-scaled XP & energy**. `QuestConfig.ChapterScaling`
  (per-chapter EnergyCostMultiplier/XpMultiplier, capped at chapter 16, modeled for 24) replaces XP-only
  `ChapterXpScalars` (now BETA). Energy now scales per chapter; XP multiplier gentle (base XP already scales) →
  fixes "XP too high vs energy". `QuestAvailabilityResponse += EffectiveEnergyCost/EffectiveXpReward`. Config-
  driven. Mock mirrors the table + expands to 3 chapters/4 zones. No migration.
- **T58** (client) — `ItemDropOverlay` (mirrors LevelUpOverlay): tap-to-dismiss rarity-colored multi-item card,
  queued; fired from QuestScreen before NotifyLevelUp; constructed in AppBootstrap.
- **T54** (backend+client) — Gauntlet is an EVENT: glowing Home CTA (when event Active) → new `GauntletScreen`
  (ladder/strikes/shop/leaderboard) + full client DTO/IRotaApi/Http/Mock plumbing. **Curve (tested):**
  `GauntletStageCurve.Hp(n)=StageHpBase(5000)×StageHpGrowth(1.0493)^(n-1)`; `GauntletConfig.MaxLadderStage=250`
  (appsettings) — `GauntletContentProvider` formula-extends the ladder when MaxLadderStage>JSON count (0/off by
  default so unit fixtures untouched; stage-1 HP stays 5000 for the shipped integration assertion). Break-even
  power = Hp/StrikesPerDefeat ⇒ stage 250 ≈ 80M (presumed endgame); smooth power→stage (1k→15,1M→158,80M→250).
  `GauntletCurveTests` asserts + prints the table. "Gauntlet Legion Power" stays a PHASE-2 placeholder.
- **T56** (backend+client) — Health = 4th `PlayerResource` (`ResourceType.Health=4`). Seeded at BaseMaxHealth,
  regen via `ClassConfig.HealthRegenMinutes`(10), **refills on level-up (owner: KEEP — so NOT a T22 reversal)**,
  max synced to BaseMaxHealth on allocate. Per-hit cost: flat-per-difficulty (`CombatConfig.RaidHealthCostByDifficulty`)
  + Gauntlet Defense-scaled curve (fractional reduction, ramps past stage 200); `EnergyService.DrainAsync` clamps
  at 0 (never blocks). HUD health bar + live HP on profile. Migration **AddHealthResource** (hand-authored,
  data-only backfill = base_max_health per existing player).
- **T57** (backend+client) — explicit **per-participant Loot claim** (REVERSES T50/System-23 grant-on-kill).
  **Reward boundary (owner-locked): ON-HIT = XP + gold ONLY; LOOTED = everything else** (gems, stat-points,
  inventory items, AND the magic/unit/legion/gear collection drops). `DistributeKillRewardsAsync` grants XP+gold
  immediately on the killing hit, ROLLS the rest and stores it pending on the participant row
  (`RecordPendingRewards`; gems/SP via fields, items via `ItemsEarnedJson`, drops via new `PendingDropsJson` =
  `List<PendingDrop>{Kind,Id,Qty}`; RewardedAt null=unclaimed). Per-participant `LootRaidAsync` grants the pending
  gems/SP/items/drops on the claim (idempotent via the RewardedAt latch; gold/XP NOT re-granted). `GetActiveRaidsAsync`
  + `IActiveRaidRepository.GetLootableUnclaimedForPlayerAsync` surface unclaimed lootable raids. Controller loot →
  full `LootRaidResult` (+`Rewards` = the looted gems/SP/items, gold/XP shown 0). RaidCombatView: Loot button moved
  out of the summoner-only share body → ANY participant; kill prompts "press Loot", loot shows the spoils. Migration
  **AddRaidParticipantPendingDrops** (adds `pending_drops_json` text column).

## Wave 2 — Public-beta blockers T65–T70 (2026-06-10) — COMPLETE (uncommitted, migrations applied)
Build: 0 errors. Tests: **925 unit + 111 integration = 1036 green.** Client headless compile 0 errors.
Migrations applied to dev DB: **AddPasswordResetTokens** (T65) + **AddTermsAcceptance** (T68).
- **T65 password reset:** request → emailed single-use Crockford code (XXXX-XXXX, SHA256-hashed at
  rest, 15-min TTL via `Auth:PasswordResetTokenMinutes`) → confirm replaces the password and revokes
  ALL sessions. Anti-enumeration: request always 202. Rate-limited per-email (SHA-derived pseudo-Guid)
  + per-IP via ISubmissionRateLimiter. `EmailPayload.RecipientOverride` lets the T39 pipeline send
  PLAYER-facing mail (raw subject + dedicated body). POST /api/auth/password-reset/request|confirm.
- **T66 deploy artifacts (host-agnostic):** multi-stage Dockerfile (non-root, :8080, content/ ships),
  .dockerignore, appsettings.Production.json (secrets via env only), docker-compose.prod.yml,
  config-gated ForwardedHeaders (trusted proxies), docs/DEPLOYMENT.md. EFCore.Relational pin → 9.*.
- **T67 CI:** docker-image job + migration gate = hermetic `MigrationSnapshotTests`
  (`Database.HasPendingModelChanges()`, no DB needed) inside the unit suite.
- **T68 terms/privacy:** `Legal:CurrentTermsVersion` config; Player.AcceptedTermsVersion/+At
  (monotonic AcceptTerms()); register requires the exact current version (validator → 400);
  AuthResponse.{RequiresTermsAcceptance,CurrentTermsVersion} on every token issue; GET
  /api/legal/terms|privacy (anonymous, content/legal/*.md, boot-validated provider) + POST
  /api/legal/accept (409 stale). Legal text is PLACEHOLDER — replace before launch.
- **T69 onboarding-lite (client):** TutorialOverlay 5-step first-run tour (PlayerPrefs latch).
- **T70 (client):** tools/build-client.ps1 + Editor/BuildPlayer.cs (batchmode Win64 build + zip).
- Client mirrors for T65/T68 (Dtos/IRotaApi/Http/Mock/LoginScreen rebuild) shipped in ROTA.Client6.

## UX wave T72–T75 + T76 Gauntlet foundation (2026-06-10) — COMPLETE (uncommitted)
Build: 0 errors. Tests: **947 unit + 111 integration = 1058 green.** Client compile clean.
Migration **AddGauntletEventIdentity** applied. Detail: docs/SESSION_HANDOFF.md (canonical).
- **T72:** Theme.uss root-cause fix — `.btn-link` was referenced but NEVER defined + no base
  `Button` type rule → Unity-grey buttons. Base Button rule (gold-on-dark, readable :disabled),
  .btn-link, themed Toggle. Grey buttons now impossible by construction.
- **T73:** persistent LATEST-REWARDS box inside QuestScreen; item-drop pop-up optional
  (PlayerPrefs `rota_reward_popups`, default OFF). Level-up overlay stays mandatory.
- **T74:** `QuestAvailabilityResponse.HighestUnlockedDifficulty` (per-node gate-chain walk; new
  IQuestDifficultyProgressRepository.GetAllForPlayerAsync); client renders 🔒 + "Clear <prev>
  first" INSTEAD of a button; mock gates + tracks tiers.
- **T75:** `[AdminOnly]` DevController /api/dev/grant|grant-item|refill → audited DevService
  (XP grants fire real level-ups via MutateWithRetryAsync; gems = AdminGrant ledger). Client
  DevToolsScreen += Player tab (grants, stateful in mock) + System tab (JWT decode, tutorial
  reset, PlayerPrefs wipe).
- **T76 (System 24, spec docs/specs/active/system-24-gauntlet-event-experience.md):** owner
  locked — solo auto-summon ladder IS the DotD shape. 4 level brackets (GauntletLeague +=
  Ancient: 1–999/1000–2499/2500–4999/5000+); rank by HIGHEST STAGE COMPLETED
  (GauntletEntry.HighestStage, atomic GREATEST on stage kill; rank ORDER highest_stage→score→
  tie_break); late HP ramp (LateRampStartStage 200 → growth 1.0493→×2.0 at 250; off by default,
  on in appsettings); Neck/Ring event families (Kind/RunNumber/LoreBlurb/BannerKey; Neck→Neck
  magic hand-off; kind-aware prize bands w/ RingBands fallback = magic-stripped Neck; CLI
  `gauntlet-open ... [neck|ring]`); client event-identity header (kind badge, run #, lore,
  live countdown) + "Stg N" leaderboard. REMAINING: seasonal rank-GEAR mechanism (needs T77
  gear), settlement screen, CTA states, prize table UI.
- Staleness sweep: CURRENT_TASK.md rewritten (pointer+snapshot), PROJECT_STATE.md banner'd
  historical, specs 16/22/ops-social/23 → shipped/ + 24 → active/, changelog noted. NEW pre-beta
  items: client TokenStore plaintext tokens (encrypt before beta); magic-shop catalogue endpoint
  missing (Bazaar shows owned-only).

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
- MaxEnergy = 25 + EnergyInvestment   (OWNER AMENDED 2026-06-09: base 10→25 to match the seeded
  starting pool — the 10-base formula clamped a fresh player's seeded 25 energy down to 11 on the
  first allocation, destroying 14 max + 14 current; bases live in PlayerStats.BaseMaxEnergy/Stamina
  and Player.CreateWithId seeds reference them so they can never drift again)
- MaxStamina = 5 + StaminaInvestment  (same amendment: base 10→5 matches the seeded pool)
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
- First defeat per difficulty: guaranteed Sigil drop (100%)
- Subsequent (rerun) defeats: flat QuestConfig.SigilRerunDropChance (15%, System 25) — NOT Discernment-scaled
- A boss only drops sigils if it carries a Sigils map AND is its zone's final node
- Using a Sigil summons that boss at that difficulty
- Sigil rarity matches difficulty (shipped convention):
    Normal=Green, Hard=Blue, Legendary=Purple, Nightmare=Orange

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
dotnet run --project src/ROTA.Api -- seed-admin                    ← create Owner admin (reads Seed:AdminPassword)
dotnet run --project src/ROTA.Api -- gen-beta-key [count]          ← generate 1..100 ROTA-XXXX-XXXX-XXXX keys
dotnet run --project src/ROTA.Api -- promote <user|guid> <Role>    ← grant Admin or Moderator role
dotnet run --project src/ROTA.Api -- demote  <user|guid> <Role>    ← revoke Admin or Moderator role
dotnet run --project src/ROTA.Api -- leaderboard-refresh-stat      ← refresh StatAttack/StatDefense/StatDiscernment Live boards