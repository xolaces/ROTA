# ROTA — Project State (current truth)

*Verified 2026-05-30 by file inventory + source tracing + green `dotnet build`/`dotnet test` runs.*
*Single source of current truth. `CLAUDE.md` = session history; `changelog.md` = release log.*

## What it is
Server-authoritative .NET 10 backend for a Dawn-of-the-Dragons-style async RPG. No game client
(Unity deferred; C# SDK is v0.3.0). Clean Architecture: `src/ROTA.{Api,Application,Domain,
Infrastructure,Shared}`. PostgreSQL 16 (EF Core 9), Redis, RS256 JWT.

## System 21 — Guild / Clan Foundations (Slice 1) — 2026-06-07 (branch `feat/system21-guild-s1-core`)
Build: 0 errors / no new warnings (4 pre-existing MSB3277 in IntegrationTests). Tests: **553 unit + 39
integration = 592, all green** (+27 unit GuildServiceTests, +4 integration GuildMembershipIntegrationTests).
Migration **AddGuildSystem** added — **`dotnet ef database update` NOT run** (orchestrator coordinates).
Spec: docs/specs/active/system-21-guild-foundations.md (PART 5 decisions locked). Guild shares ZERO tables
with the Gauntlet work. Scope = identity + membership + join flow + roles/permissions + lifecycle. No
guild chat (S2), no guild raids / sigil economy (S3).
- **Enums:** `GuildRank {Member=1,Officer=2,Leader=3}` (compare by value; no 0 → no store default),
  `GuildJoinPolicy {Open,Application,InviteOnly}`, `GuildJoinRequestKind {Application,Invite}`,
  `GuildJoinRequestStatus {Pending,Accepted,Rejected,Withdrawn,Expired}`.
- **Config:** `GuildConfig` (IOptions, appsettings `"GuildConfig"`): MemberCap 50, **CreationGoldCost 25000
  (TUNABLE — flagged for owner confirmation)**, MinCreationLevel 20, LeaderInactivityDays 14, tag 2–5.
- **Entities + Fluent + DbSets:** `Guild` (name/tag + lowercase shadow columns NameNormalized/TagNormalized
  under **partial unique indexes** `is_deleted=false` → case-insensitive uniqueness + reuse after disband),
  `GuildMembership` (**partial unique on player_id WHERE is_deleted=false** → one active guild per player,
  re-joinable; LastActiveAt drives succession), `GuildJoinRequest`. `Player` additive methods JoinGuild/
  LeaveGuild/SetGuildRank keep the denormalized GuildId/GuildRank in sync (membership row is source of truth).
- **Repos (scoped):** IGuildRepository / IGuildMembershipRepository / IGuildJoinRequestRepository
  (+ GuildRepositories.cs impl, EF/LINQ, normalized-column ci lookups).
- **Service:** IGuildService + GuildService — create (gold+level gate, ci uniqueness, reserved-name reuse
  of `ReservedUsernames`, tag length), disband (leader-only, releases members), apply (Open auto-accept /
  Application→request / InviteOnly reject; idempotent), accept/reject application (officer+), invite +
  accept-invite, leave (leader can't), kick / promote / demote (**actor.Rank > target.Rank**, and rank-change
  additionally requires resulting rank < actor.Rank → only the Leader changes ranks; officers can't create
  officers), transfer leadership, **RunInactivitySuccessionAsync** (promotes most-active officer when leader
  idle ≥ LeaderInactivityDays; **auto-driver is a documented FOLLOW-UP**), GetGuild (detail+roster), Browse.
  Every state change writes audit_log.
- **API:** `GuildController [Authorize]` /api/guilds (browse/create/detail/apply/requests-accept|reject/
  invite/invites-accept/leave/members-kick|promote|demote/transfer/PUT update/disband). Failure codes →
  400/403/404/409. DTOs in GuildDTOs.cs + validators in GuildValidators.cs (reuse ReservedUsernames).

## System 21 — Guild / Clan Foundations (Slice 2 — guild chat) — 2026-06-07 (branch `feat/system21-guild-s2-chat`)
Build: 0 errors / no new warnings (4 pre-existing MSB3277). Tests: **563 unit + 39 integration = 602, all
green** (+10 unit: 4 RedisGuildChatStoreTests + 6 GuildChatHubTests). **No schema change** — Redis-only, no
migration. Additive to the existing `ChatHub` (world/raid chat untouched). MOTD already surfaced via
`GuildDetailResponse.Motd` (S1) — no extra work this slice.
- **`ChatHub` guild methods (`src/ROTA.Api/SignalR/ChatHub.cs`):** `JoinGuildChannel()` /
  `LeaveGuildChannel()` (group resolved server-side from the caller's verified `Player.GuildId`, never a
  client-supplied id) and `SendGuildMessage(string)`. **Member-gate + mute-gate** in `SendGuildMessage`:
  resolves the caller once via `IPlayerRepository.FindByIdAsync(sub)`, rejects a banned/muted player
  (`"Muted"` event — same rejection world/raid use), then rejects a player with null `GuildId`
  (`"GuildChatUnavailable"` event). On pass: builds the same `ChatMessageDto` shape as world chat
  (`Scope="Guild"`, SenderId/SenderName/SenderRole/SentAt), appends to the per-guild ring buffer, and
  broadcasts `"GuildMessage"` to group `guild:{guildId}`. Reuses the world-chat `Sanitize` (trim + 500-char
  cap). World/raid methods unchanged.
- **`IGuildChatStore` + `RedisGuildChatStore`** (`Application/Interfaces` + `Infrastructure/Services`):
  per-guild 100-message ring buffer, key `chat:guild:{guildId}` (LPUSH newest → LTRIM to cap, read
  oldest→newest). Exact mirror of `RedisWorldChatStore` plus a `guildId` arg → buffers are isolated per
  guild. Registered scoped in `ServiceCollectionExtensions` beside the world store.
- **History endpoint:** `GET /api/chat/guild/history?count=` `[Authorize]` on `ChatController` — resolves
  the caller's `Player.GuildId` from JWT `sub`; **member-gated** (null GuildId → 200 with empty list,
  mirroring the always-200 world-history shape) else returns that guild's recent messages from the store.
- **Out of scope (deferred, per spec):** the Unity SignalR client (like world/raid chat send), guild raids
  + the sigil economy (S3).

## System 22 — Masteries Core (Phase A, Slice 1 — content/definitions) — 2026-06-07 (branch `feat/system22-masteries-s1`)
Build: 0 errors / no new warnings (4 pre-existing MSB3277 in IntegrationTests). Tests: **737 unit, all green**
(+15 unit MasteryDefinitionProviderTests; integration suite unchanged — Slice 1 touches no DB/infra). Spec:
docs/specs/active/system-22-masteries-core.md (decisions LOCKED + owner-confirmed 2026-06-07). **Scope = content
+ definitions + config only** — NO entities/migrations/endpoints/combat (Slices 2–7). The progression spine of the
"collective vs individual" Ancients design: 4 pledgeable Ancients, each leveled 1→5 by challenge checklists, each
granting an always-on global + an amplified pledge modifier through EXISTING combat/loot hooks (no new path).
- **Enums (Domain):** `MasteryAncient {Wrath=0, Bulwark=1, Hoard=2, Discernment=3}`, `MasteryActivityType`
  (11 deterministic counters: RaidHit/RaidDamageDealt/RaidKill/QuestNodeCleared/QuestBossCleared/
  GuildRaidContribution/GauntletRankEarned/LevelGained/EnergySpent/StaminaSpent/GoldEarned). Persisted as int — append, never renumber.
- **Content models (Application/Models):** `AncientDefinition` (Ancient, name/theme/desc, `GlobalPercentByLevel[5]`,
  `TierChallenges[4]`, iconKey) + `MasteryTierChallenge {FromLevel, Checklist}` + `MasteryChallengeItem {ActivityType, Threshold}`.
- **Config:** `MasteryConfig` (IOptions, appsettings `"MasteryConfig"`): PledgeMultiplier 2.0, BulwarkMaxGuildDamagePercent 1.0
  (hard cap), RespecGemCost 150 (TUNE), IncludeWeakestPillarFloor false, BreadthMicroBonusPercent 0.0 (off)/Max 2.0,
  SigilFindAppliesToFirstClear false. Per-Ancient per-level magnitudes live in JSON; this holds only scalar dials.
- **Provider:** `IMasteryDefinitionProvider` + `MasteryDefinitionProvider` (Infrastructure singleton, eager-constructed
  in Program.cs). Loads `content/masteries.json`; **throws at startup** on ≠4 Ancients / duplicate / bad magnitude table
  (length≠5, negative, non-decreasing) / bad tier checklist (count≠4, fromLevel gap, empty, threshold≤0, unknown
  activityType) / final tier not spanning ≥2 activity types (breadth curve). Methods GetAll/Get/GlobalPercent/GetTierChallenge.
- **Content:** `content/masteries.json` — 4 Ancients with locked magnitudes (Wrath 0.5→2.5 / Bulwark 0.1→0.5 /
  Hoard & Discernment 0.8→4.0) + cross-system challenge checklists (late tiers require raid + guild + gauntlet effort).
- DI wiring in ServiceCollectionExtensions; Configure<MasteryConfig> + eager construct in Program.cs; appsettings section.
- **Deviation noted:** `GemTransactionType.MasteryRespec=13`, `MasteryRespecKind`, and the `LeaderboardBoard.MasteryRating*`
  values are added in their consuming slices (S3/S2) rather than S1, to avoid unused symbols — cleaner per-slice diffs.

## System 22 — Masteries Core (Phase A, Slice 2 — state + read API + rating) — 2026-06-08 (branch `feat/system22-masteries-s2`)
Build: 0 errors / no new warnings. Tests: **750 unit + 84 integration = 834 green** (+13 unit MasteryServiceTests;
PlayerService/Leaderboard tests updated for the new ctor dep + 2 boards). Migration **AddMasterySystem** generated —
**`dotnet ef database update` NOT run** (owner coordinates). Integration suite re-run green (migration applies on a
fresh Testcontainers DB; Player profile + leaderboard flows intact).
- **Entities + Fluent + DbSets:** `PlayerMastery` (monotonic Level 1..5, unique (player_id, ancient)),
  `PlayerMasteryActivity` (cumulative Counter, unique (player_id, activity_type) — the ON CONFLICT target for S4),
  `MasteryActivityEvent` (append-only idempotency ledger, partial unique (player_id, activity_type, reference_id)).
  `Player.ActivePledgeAncient` (nullable enum) + SetPledge/ClearPledge (MasteryService sole writer; GuildId-style denorm).
- **Enums:** `LeaderboardBoard.MasteryRatingActive=6`, `MasteryRatingLifetime=7` (appended).
- **Repos (scoped):** `IPlayerMasteryRepository` (GetForPlayer/Find/Upsert/**EnsureAllAsync** lazy-create-4-L1/**GetAllRatingsAsync**),
  `IPlayerMasteryActivityRepository` (GetForPlayer; Increment/event in S4). `PlayerMasteryRatingRow` record.
- **Service:** `IMasteryService` + `MasteryService` — `GetMasteriesAsync` (ensure L1 rows, per-Ancient global%/effective%
  (pledged ×2, Bulwark clamped), checklist X/Y progress, rating, titles), `ComputeRating` (pure **Formula B**, no
  weakest-floor by default — worked examples 10/14/56 asserted), `GetCombatModifiersAsync`/`GetLootModifiersAsync`
  (plain modifiers for S5/S6; Wrath percent / Bulwark fraction-capped / Hoard+Disc multipliers + Disc quality chance),
  `SnapshotRatingBoardAsync` (Live snapshot via ILeaderboardEntryRepository.SetValueAsync). Titles derived (no entity):
  Touched Everything/Well-Rounded/Paragon/Ascendant + Master of <Ancient>. Active==Lifetime in Phase A (monotonic).
- **API:** `GET /api/masteries` (`MasteryController [Authorize]`); `POST /api/admin/masteries/rating/refresh`
  (`MasteryAdminController [AdminOnly]` + DB actor re-verify + audit); CLI `mastery-refresh-rating`. Board metadata
  added to `LeaderboardService.AllBoards` (GET /api/leaderboards now lists 8). DTOs in MasteryDTOs.cs.
- **Profile:** `PlayerProfileResponse.ActivePledge` + `MasteryRatingActive`; `PlayerService` hydrates them (pledge free
  from the loaded player; rating from current levels — no row writes on a GET). PlayerService ctor += IPlayerMasteryRepository, IMasteryService.

## System 22 — Masteries Core (Phase A, Slice 3 — pledge + re-spec economy) — 2026-06-08 (branch `feat/system22-masteries-s3`)
Build: 0 errors / no new warnings. Tests: **760 unit + 85 integration = 845 green** (+10 unit MasteryRespecTests,
+1 integration MasteryRespecIdempotencyTests). Migration **AddMasteryRespecLedger** generated — NOT applied.
- **Enums:** `GemTransactionType.MasteryRespec=13`; `MasteryRespecKind {Paid, FreeMonthly, NewAncientUnlock}`.
- **Entity + Fluent + DbSet + migration:** `MasteryRespecTransaction` (append-only; Kind/From?/To/GemCost/ReferenceId;
  unique (player_id, reference_id) — the hard cap + idempotency backstop). Table `mastery_respec_transactions`.
- **Cap store (ROTA Redis pattern):** `IMasteryRespecCapStore` (Application) + `MasteryRespecCapStore` (Infrastructure,
  IConnectionMultiplexer) — key `respec:paid:week:{p}` set-on-use, TTL to next Monday 00:00 UTC (mirrors
  AuthLockoutService). Keeps StackExchange.Redis out of Application (like ISubmissionRateLimiter).
- **Repo:** `IMasteryRespecRepository` (ReferenceExistsAsync + CreateAsync → false on unique-violation).
- **Service:** `MasteryService.RespecAsync(playerId, toAncient)` — eligibility resolved cheapest-first:
  **(1) free first-pledge** to an Ancient (no `respec:unlock:{p}:{ancient}` row → free; also auto-covers a future
  awakened Ancient, so NO explicit grant hook needed), **(2) free monthly** (`respec:free:{p}:{yyyy-MM}`),
  **(3) paid weekly** — cap-store fast gate → `IGemService.SpendGemsAsync(MasteryRespec, refId=respec:paid:{p}:{isoYear}-Www)`
  (Charged → swap + mark cap; AlreadyProcessed → WeeklyCapReached + sync cap; Insufficient → InsufficientGems).
  **LOSSLESS + idempotent** — only flips `Player.SetPledge`; mastery levels never touched (unit-asserted: never calls
  masteryRepo). Audited `MasteryRespec:{Kind}`. GetMasteries RespecStatus now reflects real availability.
  **PHASE-2 noted:** a crash between the paid gem charge and the pledge commit banks the charge until next week (rare;
  strict cap is the deliberate priority).
- **API:** `POST /api/masteries/pledge` (PledgeRequest + PledgeRequestValidator) → 200/400/404/409/422
  (AncientNotFound/PlayerNotFound 404, AlreadyPledged/WeeklyCapReached 409, InsufficientGems 422). DTOs
  MasteryRespecResult + MasteryRespecFailureCode in MasteryDTOs.cs.
- **Integration proof:** with the cap store stubbed to no-op (the "Redis flushed" case), two paid swaps the same ISO
  week charge gems EXACTLY ONCE via the gem-ledger week refId — verified vs real Postgres.

## System 22 — Masteries Core (Phase A, Slice 4 — activity counters + tier-up leveling) — 2026-06-08 (branch `feat/system22-masteries-s4`)
Build: 0 errors / no new warnings. Tests: **771 unit + 88 integration = 859 green** (+11 unit MasteryServiceTests,
+3 integration MasteryActivityRepositoryTests). No new migration (tables exist from S2). The progression spine now moves.
- **Repo (raw SQL):** `IPlayerMasteryActivityRepository.IncrementAsync` (clone of LeaderboardEntryRepository — raw
  ON CONFLICT upsert-increment, ambient-tx aware so it rides the raid advisory-lock tx) + `HasEventAsync` /
  `RecordEventAsync` (the idempotency event ledger; pre-check avoids a unique-violation that would poison an ambient tx).
- **Service:** `MasteryService.RecordActivityAsync(playerId, activityType, amount, referenceId?)` (no ref → increment;
  ref → HasEvent pre-check → RecordEvent + increment, exactly-once). `EvaluateTierUpsAsync` (off the hot path): ensures
  L1 rows, compares cumulative counters to the per-tier checklist, advances `PlayerMastery.Level` (monotonic, audited
  `MasteryLevelUp`, may jump several tiers). Triggered from `GetMasteriesAsync` (read) + end of `AttemptQuestAsync`.
- **Chokepoints wired** (the 8 counters the shipped checklists use — EnergySpent/StaminaSpent/LevelGained are reserved
  enum values, intentionally UNWIRED until a checklist needs them):
  - `RaidService.HitRaidAsync` (enlisted in the advisory-lock tx, like the leaderboard hook): RaidHit + RaidDamageDealt
    (after RecordHit), GuildRaidContribution (guild fork), GoldEarned (on-hit gold), RaidKill (isKill block, idempotent
    `mastery:kill:{raid}:{player}`). Redis cached-replay can't double-count (early-return fires before the tx).
  - `QuestService.AttemptQuestAsync` (best-effort try/catch — quest rewards are non-tx): GoldEarned, QuestNodeCleared
    (gated on `nodeJustCleared`), QuestBossCleared (boss + just-cleared), then `EvaluateTierUpsAsync`.
  - `GauntletAdminService.SettleEventAsync` (best-effort + idempotent `mastery:rank:{event}:{player}`): GauntletRankEarned.
- New ctor deps: `IMasteryService` on RaidService, QuestService, GauntletAdminService. Cycle-free (MasteryService
  injects none of them). Unit harnesses for those three + the leaderboard-hook test updated with a mock.
- **Tests:** RecordActivity (ref/no-ref/already-seen/non-positive); tier-up (advance, blocked, multi-jump, breadth
  curve blocks final tier until all cross-system counters met, max-level no-op); GetMasteries triggers tier-up;
  integration: ON CONFLICT create-then-accumulate, separate-types-separate-rows, event-ledger guard.

## System 22 — Masteries Core (Phase A, Slice 5 — combat: Wrath + Bulwark) — 2026-06-08 (branch `feat/system22-masteries-s5`)
Build: 0 errors / no new warnings. Tests: **775 unit + 88 integration = 863 green** (+4 unit Wrath/Bulwark combat
tests; the 700+ existing hit tests run with neutral mods → byte-for-byte regression proof). No migration. DEEP slice
(combat money path) — NO new combat path; reuses the existing legion-bonus + FlatDamagePercent hooks.
- **One per-hit read:** `RaidService.HitRaidAsync` loads `IMasteryService.GetCombatModifiersAsync(playerId)` once
  (next to the `combat`/legion loads). Mastery-less player → `(0,0)` → unchanged hit.
- **Wrath (+% legion power, never Gauntlet):** `masteryMods.WrathLegionPercent` added into `totalLegionBonus` BEFORE
  the `bonusFraction` divide (composes additively with `PowerBonus` + Σ General.LegionBonus; flows once through
  rawLegionPower, ahead of the trophy `*=` stage + PowerScaling — single-touch). Gated by an active legion (no legion
  → no-op). Marginal captured for display.
- **Bulwark (+% guild-raid damage, hard-capped, guild raids only):** at the post-crit FlatDamagePercent stage,
  `flatDamageFraction = combat.FlatDamagePercent + (lockedRaid.GuildId != null ? BulwarkGuildDamageFraction : 0)`;
  the `> 0` guard now tests the combined value so Bulwark fires even when gear flat is 0. Stacks additively with gear
  flat. The ~1% cap is enforced in MasteryService (S2); the hook just applies the fraction.
- **DTO:** `RaidHitResponse.WrathLegionBonus` + `BulwarkBonus` (marginal display amounts; 0 when N/A).
- **Tests:** Wrath comparison (bonusFraction 0.55→0.60 ratio), Wrath no-legion → 0, Bulwark guild-raid adds damage,
  Bulwark non-guild → not applied. Existing RaidService/leaderboard hit harnesses given a neutral-mods mastery mock.

## System 22 — Masteries Core (Phase A, Slice 6 — loot: Hoard + Discernment sigil-find) — 2026-06-08 (branch `feat/system22-masteries-s6`)
Build: 0 errors / no new warnings. Tests: **778 unit + 88 integration = 866 green** (+3 unit: Hoard quest gold,
Discernment sigil-find, Hoard raid on-hit gold). No migration.
- **Combined read (perf):** new `IMasteryService.GetModifiersAsync` returns combat + loot mods from ONE
  (levels+pledge) load. `RaidService.HitRaidAsync` now calls it once (replaces the S5 GetCombatModifiersAsync call) —
  one mastery-state read per hit, not two — and uses `.Combat.*` (Wrath/Bulwark) + `.Loot.HoardGoldMultiplier`.
- **Hoard (+% drop rate / gold):** quest drop-rate folded into `ProcessQuestLootAsync`'s `Scale` closure (multiplicative,
  inside the existing 0.95 cap); quest gold `goldReward × HoardGoldMultiplier`; raid on-hit gold
  `goldGained × HoardGoldMultiplier` (the S4 GoldEarned counter then reflects the boosted amount).
- **Discernment sigil-find:** post-first-clear sigil chance `× DiscernmentSigilFindMultiplier` (clamp ≤ 1.0);
  the guaranteed first-per-difficulty drop is never scaled (`SigilFindAppliesToFirstClear=false`).
- **Best-effort fetch (quest):** `GetLootModifiersAsync` wrapped in try/catch → neutral on failure, so a mastery read
  error never denies base rewards. Loot quality (rarity-upgrade) is the Slice 7 piece (DiscernmentQualityChance unused here).
- **NOTE — deferred to a follow-up:** raid *threshold-drop* (kill-loot) Hoard scaling. Quest drops (the canonical
  System-20 loot pipeline) + quest/raid gold + sigil-find are delivered. Raid threshold drops fire per-participant on a
  kill (up to 250 for a world raid) → per-participant mastery reads on the kill; deferred to avoid that on the combat
  kill path. Hoard's drop benefit applies to the quest pipeline; flagged for owner confirmation.

## System 22 — Masteries Core (Phase A, Slice 7 — Discernment drop-quality) — 2026-06-08 (branch `feat/system22-masteries-s7`)
Build: 0 errors / no new warnings. Tests: **786 unit + 88 integration = 874 green** (+8 unit: 5 ItemDefinitionProvider
validation, 3 QuestService quality-upgrade). No migration (content-model field only).
- **Opt-in content field:** `ItemDefinition.UpgradesTo` + `GearDefinition.UpgradesTo` (next-tier item/gear id; null =
  never). `ItemDefinitionProvider` + `GearDefinitionProvider` now validate at startup: upgradesTo resolves + is
  STRICTLY higher rarity (Orange items can't upgrade — the ceiling). ItemDefinitionProvider also gained a duplicate-id guard.
- **Quest item quality-upgrade (wired):** in `ProcessQuestLootAsync`, a FIRED chance drop rolls `DiscernmentQualityChance`;
  on success, `ResolveQualityUpgrade` substitutes the item's `UpgradesTo` (single step). Guaranteed drops never upgrade.
- **Starter ladder (content):** `mat_iron_shard` (Grey)→`mat_arcane_dust` (White); `statbag_minor` (Green)→`statbag_major` (Blue).
- **Deferred (documented):** gear-drop quality-upgrade wiring (current quest gear is the Orange-ceiling Pano set — no
  headroom) + raid threshold-drop quality, bundled with the S6 raid-threshold follow-up. The `GearDefinition.UpgradesTo`
  field + validation ship now so the wiring is a pure code change later.

## System 22 — Masteries Core (Phase A) — COMPLETE (2026-06-08) — all 7 slices merged to main
Spec: docs/specs/active/system-22-masteries-core.md. **786 unit + 88 integration = 874 green; 0 errors, 0 CS warnings.**
Migrations generated, NOT applied (owner runs `dotnet ef database update`): **AddMasterySystem**, **AddMasteryRespecLedger**.
The months-long horizontal progression spine: 4 Ancients (Wrath/Bulwark/Hoard/Discernment) leveled 1→5 by per-Ancient
challenge checklists; always-on global + pledge (≈×2) modifiers through EXISTING combat/loot hooks (no new combat path);
Formula-B Overall Mastery Rating + derived titles + leaderboard board; lossless re-spec economy (free unlock/monthly +
paid weekly via gems, idempotent). Phase B (The Rise) + Phase C (PoE-depth) remain in backlog.
- **Owner follow-ups to confirm:** (1) raid threshold-drop Hoard scaling + gear quality-upgrade (deferred — per-participant
  kill-loop reads / Orange-ceiling gear); (2) TUNE the magnitudes + challenge thresholds + the (off-by-default) breadth
  micro-bonus; (3) the paid-respec crash-recovery gap (strict weekly cap; PHASE-2 note in MasteryService).

## System 22 — Masteries dev-force endpoint (2026-06-08) — for client dev tools / local testing
Build: 0 errors. Tests: **787 unit + 88 integration green** (+1 unit ForceLevels). Local DB updated
(`AddMasterySystem` + `AddMasteryRespecLedger` applied). Supports the Unity client's dev-force panel + Swagger testing.
- `PlayerMastery.ForceSetLevel(int)` — admin/dev only; sets level 1..5 up OR down (bypasses the monotonic guard).
- `IMasteryService.ForceLevelsAsync(playerId, levels)` — ensures rows, force-sets, audited (`MasteryForceLevel`),
  returns the refreshed overview.
- `POST /api/admin/masteries/force` [AdminOnly + DB actor re-verify] — `MasteryForceRequest { TargetPlayerIdOrUsername?
  (null=self), Levels (ancient→1..5) }`; 400 bad ancient/level, 403 non-admin, 404 unknown target; audited
  (`MasteryForceLevels`). The live client dev-tool calls this behind a confirm dialog; mock fakes it locally.

## Build status (High — earlier sessions, see the dated entries above for the latest)
- **400 unit + 34 integration = 434 tests pass. 0 warnings, 0 errors.**
- `main` past tag **v0.2.7-s6** (Legion epic complete) + 3 post-fixes merged & pushed (untagged hardening):
  gem-buy lost-purchase recovery, class-based regen DTO field, System 16 Gauntlet **draft** spec.
- Branches `slice/leaderboards-s1` + `slice/leaderboards-s2` merged to main (System 17 Slices 1+2).
- Branch `slice/leaderboards-s3` (System 17 Slice 3): ILeaderboardService + LeaderboardController + LeaderboardDTOs + eligibility-aware SQL (GetEligiblePageAsync/CountEligibleAsync/GetCallerRankAsync). +31 new tests (15 unit service, 7 unit controller, 9 integration eligibility).
- Branch `slice/leaderboards-s4` (System 17 Slice 4): RecordEnergySpendAsync + RecordRaidHitAsync write hooks on ILeaderboardService/LeaderboardService. EnergyService + RaidService wired. +12 new unit tests (period boundaries, idempotency guard, failure-swallow, Stamina exclusion, cached-replay guard).
- Branch `slice/leaderboards-s5` (System 17 Slice 5): Stat board snapshot. SetValueAsync + GetEligibleStatSnapshotAsync on ILeaderboardEntryRepository/LeaderboardEntryRepository. SnapshotStatBoardAsync on ILeaderboardService/LeaderboardService. POST /api/admin/leaderboards/stat/refresh [AdminOnly] + CLI leaderboard-refresh-stat. StatBoardRefreshResponse DTO. +9 unit tests (5 service snapshot, 4 admin controller) + 8 integration tests (ranking by ATK/DEF/Disc, eligibility exclusions, Moderator included, idempotency, value update, last_progress_at preservation + bump).

## Inventory (High)
11 controllers · 17 services · 19 entities · 25 enums · 18 repositories · 3 middleware ·
20 EF migrations (InitialCreate→AddLeaderboardEntry) · 8 content JSON files · GitHub Actions CI.
(Slice 3 adds: ILeaderboardService + LeaderboardService, LeaderboardController, LeaderboardDTOs, 3 new repo methods on ILeaderboardEntryRepository.)
(Slice 1 adds: LeaderboardBoard/LeaderboardPeriod/LeaderboardAggregation enums, LeaderboardConfig + supporting config enums, IPeriodKeyResolver + PeriodKeyResolver singleton.)
(Slice 6 adds: GemPrice to UnitDefinition/LegionDefinition, GemTransactionType.UnitPurchase/LegionPurchase,
UnitDropChance/LegionDropChance loot drop models, GrantUnitAsync/GrantLegionAsync/BuyUnitAsync/BuyLegionAsync
in ILegionService+LegionService. QuestService+RaidService wired to fire unit/legion drops from loot tables.
Two auditor fold-ins: AssignSlotAsync quantity > 0 guard; ComputeLegionPowerAsync reads LegionConfig coefficients.)

## Implemented & tested (High)
Auth · Rate limiting · Audit · Energy/resources · Player profile · Gem ledger · Quests+difficulty ·
Raid engine (pg advisory-lock, Redis idempotency) · Items/sigils · Stats · Class system ·
RBAC + beta keys + admin (REST+CLI) · Character gear (v0.2.4) · Conditional/stacking bonuses (v0.2.5) ·
Raid magic content layer (System 14 Slice 1 — MagicDefinition + IMagicDefinitionProvider + magics.json) ·
**Raid magic ownership (System 14 Slice 2 — PlayerMagic entity + GET /api/magics) ·
**Raid magic application (System 14 Slice 3 — RaidMagic + slot-cap advisory lock + world gate; DEEP review)** ·
**Raid magic damage procs (System 14 Slice 4 — DamageProc magic fires in HitRaidAsync; MagicProcBonus+MagicProcs in RaidHitResponse)** ·
**Raid magic utility effects (System 14 Slice 5 — CritChanceFlat/GoldProc/XpProc wired into HitRaidAsync; MagicCritBonus in RaidHitResponse)** ·
**Raid magic economy (System 14 Slice 6 — GrantMagicAsync idempotent upsert, BuyMagicAsync gem shop, magicDrops in loot tables wired into RaidService+QuestService)**.
- **Resource regen is class-based (v0.2.2):** energy/stamina/guild regen derive from `ClassConfig`
  (minutes-per-point). **GuildStamina now regenerates** (was 0). Stored `RegenPerMinute` is vestigial.
- **RaidSize set (v0.2.2):** Personal/Small/Medium/Large/Titanic, participant caps 1/10/25/50/250,
  enforced pre-spend on hit. Personal = summoner-only.
- **Raid on-hit rewards (v0.2.2):** XP = single 1–4 roll × stamina; gold = stamina × per-raid
  `goldPerStamina`; hit response now returns per-hit `XpGained`/`GoldGained`/`DamageDealt`.
- **Discernment crit (v0.2.3):** raid hits crit via `DiscernmentInvestment` — chance 5%→15% (+10%
  hard cap @1000 disc), damage 1.5×→2.5× (@5000 disc), tunable `CombatConfig`.
- **Character gear (v0.2.4):** 8 slots. Raid damage uses effective stats. Mount proc once per hit.
- **Conditional bonuses (v0.2.5):** JSON-only bonus framework. `ConditionalBonusEvaluator` shared
  by gear/future legions. 5 bonus types, 3 condition types. `FlatDamagePercent` applied after crit.
  Reward atomicity fixed: stamina spend inside advisory-lock tx (atomic rollback). `ProcBonus` is `long`.

## Content state (High)
Minimal playable slice: 2 chapters, 5 quest nodes (3 battle + 2 boss), 2 raids, 12 items, 2 loot
tables. Loop works; thin.

## Partially implemented (High)
- SignalR registered, **no hubs mapped** (real-time inert).
- Admin "panel" is API-only (no UI).

## System 15 — Legion (v0.2.7, Slices 1–6 — COMPLETE)
- **Slice 1**: 6 enums, UnitDefinition/LegionDefinition models, IUnitDefinitionProvider/ILegionDefinitionProvider, content/units.json (8 units) + content/legions.json (3 legions). Tag v0.2.7-s1.
- **Slice 2**: PlayerUnit/PlayerLegion entities, EF configs, migration AddLegionOwnership, repos, LegionService (GetOwnedUnitsAsync/GetOwnedLegionsAsync), LegionController (GET /api/units, /api/legions). Tag v0.2.7-s2.
- **Slice 3**: PlayerLegionSlot entity, EF config, migration AddLegionSlots, PlayerLegionSlotRepository, full LegionService (SetActiveLegionAsync, AssignSlotAsync, ClearSlotAsync, ComputeLegionPowerAsync, GetLegionDetailAsync). Slot constraint validation (Race/Role/Attribute). Tag v0.2.7-s3.
- **Slice 4 (DEEP)**: LegionConfig (PowerScaling, UnitCoefficients, MaxUnitProcBonus), legion power integrated into HitRaidAsync preProc (same RNG multiplier+hitSize as charBase; inline from injected repos, NOT LegionService.ComputeLegionPowerAsync), unit-ability proc phase (separate cap from magic), RaidHitResponse gains LegionPower/UnitProcBonus/UnitProcs. Tag v0.2.7-s4.
- **Slice 5 (MODERATE)**: Commander slot — PlayerCommanderGear entity (one row per player, upsert in place), IPlayerCommanderGearRepository, EF config + migration AddCommanderGear. ILegionService: EquipCommanderAsync/UnequipCommanderAsync/GetCommanderAsync. LegionController: PUT/DELETE/GET /api/legions/commander. Combat: commander gear proc fires in proc phase off preProc (stats deliberately excluded — PlayerCommanderGear never reaches GetEffectiveCombatDataAsync path). RaidHitResponse: CommanderProcFired/CommanderProcBonus. Tag v0.2.7-s5.
- **Slice 6 (MODERATE)**: Economy/acquisition — GemPrice on unit/legion defs. GrantUnitAsync/GrantLegionAsync (idempotent upsert). BuyUnitAsync/BuyLegionAsync: ownership pre-check → AlreadyOwned 409 without charge, idempotent referenceId (unitbuy:{playerId}:{id} / legionbuy:{…}). GemTransactionType.UnitPurchase/LegionPurchase. LootTable: UnitDropChance/LegionDropChance added to ThresholdReward (raid) and LootTableDifficulty (quest); wired in RaidService.DistributeKillRewardsAsync and QuestService.ApplyLootAsync. Auditor fold-ins: AssignSlotAsync Quantity>0 guard; ComputeLegionPowerAsync reads LegionConfig.UnitCoefficients (no hardcoded values). LegionService gains IGemService + IOptions<LegionConfig>; QuestService+RaidService gain ILegionService dep. POST /api/units/buy + POST /api/legions/buy. Tag v0.2.7-s6.

## System 17 — Global Leaderboards
- **Slice 1** (merged): `LeaderboardBoard` / `LeaderboardPeriod` / `LeaderboardAggregation` enums (Domain). `LeaderboardConfig` (IOptions, appsettings-bound, startup-validated — Timezone/MinLevel/PageSize/WeekStartsOn). `IPeriodKeyResolver` interface + `PeriodKeyResolver` singleton (ISOWeek-correct weekly keys, UTC-normalised, config validation at ctor). 24 unit tests covering all period-key boundaries + ISO year-edge cases + bad-config guards.
- **Slice 2** (merged): `LeaderboardEntry` entity (private setters, no EF attributes; `Create`/`AddValue`/`MaxValue`/`SetRank` methods). Fluent EF config: table `leaderboard_entry`, snake_case, enum columns stored as `int` with NO `HasDefaultValue` (no sentinel needed), unique index `ix_leaderboard_entry_upsert_key` on `(player_id, board, period_key)`, read index `ix_leaderboard_entry_board_period_value` on `(board, period_key, value)`, FK index `ix_leaderboard_entry_player_id`. Migration `AddLeaderboardEntry`. `DbSet<LeaderboardEntry>`. `ILeaderboardEntryRepository` + `LeaderboardEntryRepository` (raw Npgsql ON CONFLICT upsert — race-safe increment and max-update). 8 unit tests (domain methods) + 6 integration tests (Testcontainers Postgres: create+accumulate, 20-concurrent no-lost-updates, max-only-raises, page-order tiebreak, lookup present/absent, soft-delete filter).
- **Slice 3** (branch `slice/leaderboards-s3`): `ILeaderboardService` + `LeaderboardService`. `LeaderboardDTOs` (LeaderboardEntryDto/LeaderboardPageResponse/LeaderboardSummary — Board+Period as strings per ROTA.Shared convention). `LeaderboardController [Authorize]`: GET /api/leaderboards, GET /api/leaderboards/{board}. Eligibility-aware SQL: `ILeaderboardEntryRepository` extended with `GetEligiblePageAsync` / `CountEligibleAsync` / `GetCallerRankAsync` — all JOIN players and apply `is_deleted=false AND is_banned=false AND level>=@minLevel AND (roles & 4)=0 when excludeAdmins`. Caller rank computed in SQL as (COUNT eligible strictly above) + 1. 15 unit tests (service: page ordering, rank offset, caller-off-page, empty board, bad combos, period key format, display-name fallback) + 7 unit tests (controller: 200/400 map) + 9 integration tests (Testcontainers Postgres: banned excluded, soft-deleted excluded, below-MinLevel excluded, Admin excluded+Mod included, tiebreak, caller rank with ineligible interleaved, banned caller=null, count excludes ineligible, display-name hydration).
- **Slice 4** (branch `slice/leaderboards-s4`): Write hooks. `ILeaderboardService` extended with `RecordEnergySpendAsync(playerId, amount, at)` and `RecordRaidHitAsync(playerId, damageFinal, at)`. `LeaderboardService` implements both — fan-out to board+period calls via the existing `ILeaderboardEntryRepository`. `EnergyService.SpendEnergyAsync`: after the atomic spend + audit write, calls `RecordEnergySpendAsync` for `ResourceType.Energy` only (Stamina/GuildStamina excluded, Q6); failure is swallowed+logged — spend never rolls back. `RaidService.HitRaidAsync`: inside the `AtomicApplyHitAsync` advisory-lock callback immediately after `participantFinal.RecordHit(damageFinal)`, calls `RecordRaidHitAsync` — rides the ambient transaction; never reached on the Redis cached-replay early-return path. No new migrations. +12 unit tests (LeaderboardWriteHookTests: period boundaries, leaderboard failure swallow, Stamina exclusion, cached-replay guard, damageFinal pass-through).
- **Slice 5** (branch `slice/leaderboards-s5`): Stat board snapshot. `ILeaderboardEntryRepository` extended with `SetValueAsync(playerId, board, period, periodKey, value, at)` (overwrite-upsert: unconditional value overwrite; `last_progress_at` bumped ONLY when value changed — tiebreak survives repeated snapshots) and `GetEligibleStatSnapshotAsync(minLevel, excludeAdmins)` (single SQL join players+player_stats applying full eligibility predicate). `ILeaderboardService.SnapshotStatBoardAsync()` queries eligible stat snapshots and calls `SetValueAsync` × 3 per player (StatAttack=BaseAttack, StatDefense=BaseDefense, StatDiscernment=DiscernmentInvestment), Period=Live, period_key="live". `AdminController` extended: `POST /api/admin/leaderboards/stat/refresh` [AdminOnly] — DB actor re-verify, calls SnapshotStatBoardAsync, writes audit_log (`StatBoardRefreshed`), returns `StatBoardRefreshResponse{PlayersSnapshotted, SnapshotAt}`. CLI `leaderboard-refresh-stat` added to AdminCli. `StatBoardRefreshResponse` DTO added. No new migrations. +5 unit service tests + 4 unit controller tests + 8 integration tests (ranking, eligibility, Moderator included, idempotency, value update, last_progress_at preserved/bumped).

## System 16 — Gauntlet (in progress)
- **Slice 1** (branch `feat/system16-gauntlet-s1-content`): content + definitions only. 7 enums
  (GauntletLeague/EventState/RewardKind/TrophyTier/Currency/CurrencyTransactionType/StrikeTransactionType).
  `GauntletConfig` (IOptions, appsettings-bound, league bounds/entry floor/prize+page counts/strike rates).
  Content models GauntletPrizeTable+Band, GauntletTrophyDefinition, dedicated GauntletRaidDefinition (NOT
  reusing RaidDefinition). `MagicDefinition.OffCap` field (combat reads it in S4). `IGauntletContentProvider`
  + `GauntletContentProvider` (singleton, startup-validated, eager-constructed). content/gauntlet_prizes.json
  (7 bands → 1..500) + gauntlet_trophies.json (3) + gauntlet_raids.json (6-stage ladder); 2 off-cap magic
  rows (Wrath 0.27/2.50, Blessing 0.15/4.25). +25 unit tests. No entities/migrations/combat (Slices 2/4).
  Build 0 errors; **551 unit + 35 integration green**. (NOTE: global counts elsewhere in this doc predate
  Phase 2 / Systems 18–20 and are stale — separate reconciliation pending.)

- **Slice 2** (branch `feat/system16-gauntlet-s2-ledgers`): persistence + ledgers + lifecycle + join.
  7 entities (GauntletEvent guarded state machine; GauntletEntry league-locked + unique per event/player;
  StrikeTransaction + GauntletCurrencyTransaction append-only ledgers; PlayerGauntletTrophy, PlayerEventMagic,
  PlayerMagicHonor) + Fluent configs + ONE consolidated migration `AddGauntletSystem` (DB update NOT run —
  coordinate). 7 repos with tri-state SpendAsync on both ledgers (idempotency-first + unique-violation
  backstop; AlreadyCharged ≠ Insufficient). IGauntletService (join: league-lock via ResolveLeague + reject
  no-event/L<20/banned/deleted + idempotent; gem→strikes buy with client-key lost-purchase recovery).
  IGauntletAdminService (open ≤1-active / close / idempotent state-only settle — payout = S5). GauntletController
  [Authorize] + GauntletAdminController [AdminOnly + actor re-verify] + CLI gauntlet-open/close/settle.
  ActiveRaid.GauntletEventId additive column+FK. GemTransactionType.GauntletStrikePurchase; GauntletConfig.StrikeGemPrice.
  +38 unit + 10 integration. Build 0 errors; **589 unit + 45 integration green**.

- **Slice 3** (branch `feat/system16-gauntlet-s3-leaderboard`): scoring aggregation + per-league snapshot
  rank + leaderboard read (NO combat changes — S4 wires the score hook). `IGauntletScoringService` +
  `GauntletScoringService` (scoped): `UpdateScoreAsync` (atomic score += delta; tie_break_at advances only
  on a positive delta), `RecomputeRanksAsync` (idempotent), `GetLeaderboardAsync` → `GauntletLeaderboardResponse`.
  `IGauntletEntryRepository`/`GauntletEntryRepository` extended with raw-Npgsql ranked reads (mirrors
  LeaderboardEntryRepository): `IncrementScoreAsync` (single race-safe UPDATE, ambient-tx aware),
  `RecomputeRanksAsync` (one set-based UPDATE via `ROW_NUMBER() OVER (PARTITION BY league ORDER BY score DESC,
  tie_break_at ASC)` → last_rank), `GetLeaderboardPageAsync` (ORDER BY last_rank, league-isolated, joined to
  players for DisplayName, excludes un-snapshotted), `GetRankAndScoreAsync` (league-scoped caller standing),
  `CountRankedAsync`. `GauntletRankSnapshotService` hosted (singleton; DI scope per tick; PeriodicTimer at
  `ScoreSnapshotSeconds`; no-op when no active event; try/catch never crashes host). GET
  /api/gauntlet/leaderboard?league= [Authorize] (400 invalid/missing league; empty board when none active).
  DTOs GauntletLeaderboardResponse/GauntletLeaderboardEntryDto. No new migrations (GauntletEntry + ranking
  index already exist). +6 unit + 17 integration. Build 0 errors; **595 unit + 62 integration green**.

- **Slice 4 (DEEP — combat)** (branch `feat/system16-gauntlet-s4-combat`): five amplifiers in
  HitRaidAsync, no parallel path. (A) highest-only trophy mult on rawLegionPower before PowerScaling
  (every raid); (B) off-cap Wrath/Blessing auras on Gauntlet raids (current ×1.25 / former-honor ×1.10 /
  else none), outside MaxAggregateProcBonus, before crit; (C) Gauntlet hits spend Strikes (1/5/20 via
  GauntletConfig) not Stamina — StrikeRepository.SpendAsync reimplemented tx-safe (raw SQL, ambient-tx,
  no ChangeTracker.Clear), 422 InsufficientStrikes; (D) GauntletScoringService.UpdateScoreAsync hook
  (rides ambient tx, no-op if unjoined). RaidHitResponse +OffCapAuraBonus/+NewStrikeBalance;
  RaidHitFailureCode.InsufficientStrikes=8. 6 new RaidService ctor deps. Trophy-less non-Gauntlet hit
  byte-identical (regression-proven). +14 test methods. Build 0 errors; **615 unit + 62 integration green**.
  KNOWN FOLLOW-UP: Gauntlet ladder summon/climb endpoint + gauntlet-stage def resolution (S1–S6 omit it).

- **Slice 5 (settlement)** (branch `feat/system16-gauntlet-s5-settlement`): idempotent prize payout in
  GauntletAdminService.SettleEventAsync — RecomputeRanks → per-league band distribution (tokens+pitchfork
  via currency ledger with ReferenceExists + unique-index guard; trophy UpsertAsync) → honor write-back
  (RevokeAllForEventAsync returns revoked rows → PlayerMagicHonor) → MarkSettled (only after grants);
  already-Settled fast-path = zero-count no-op. Per-defeat reward in HitRaidAsync isKill block (Gauntlet
  only, inside tx): +StrikesPerDefeat strikes + 1 Token, ReferenceExists-guarded. GauntletSettlementSummaryResponse.
  +19 unit + 5 integration (incl. settle-twice-pays-once vs real Postgres ledgers + a forced-rerun test
  proving the per-grant referenceId guard alone blocks re-credit). Build 0 errors; **634 unit + 67 integration green**.
  DEFERRED: next-event consumable hand-off (bundled with the ladder-summon follow-up).

- **Slice 6 (token shop)** (branch `feat/system16-gauntlet-s6-shop`): power-focused Token/Pitchfork shop —
  the LAST Gauntlet slice. `content/gauntlet_shop.json` (6 entries, real def ids) + `GauntletShopEntry`
  model `{Id, RewardKind, PayloadId, Amount?, Currency, Price, MaxOwned?}`; `GauntletShopRewardKind
  {Unit, Legion, Gear, GemBundle, StrikeRefill}`. `IGauntletShopProvider`/`GauntletShopProvider`
  (Infrastructure singleton, eagerly constructed; ctor takes unit/legion/gear def providers) throws at
  boot on dup id / price≤0 / bad currency / unresolved Unit/Legion/Gear payloadId / own-once not maxOwned:1
  / bundle|refill amount≤0. `GauntletService.GetShopAsync` (catalogue + Token+Pitchfork balances +
  per-entry AlreadyOwned) + `BuyFromShopAsync` mirrors LegionService.BuyUnitAsync: ownership pre-check →
  AlreadyOwned (no charge) → tri-state `IGauntletCurrencyRepository.SpendAsync(entry.Currency, …)` (refId
  `gauntletshop:{playerId}:{entryId}`; Insufficient → InsufficientTokens, no write; Charged|AlreadyCharged
  → grant) → idempotent grant per rewardKind (Unit/Legion Grant*; Gear GrantGearAsync(.,1) gated by the
  pre-check; GemBundle GrantGemsAsync via gem-ledger unique index; StrikeRefill CreateAsync guarded by
  ReferenceExistsAsync). **Token vs Pitchfork isolation:** spend uses entry.Currency, so a Pitchfork-priced
  item with only Tokens → InsufficientTokens (Token balance untouched). Additive enums
  `GemTransactionType.GauntletShopReward=12` + `StrikeTransactionType.ShopRefill=4` (code-only, stored as
  int — NO migration/sentinel). GauntletController [Authorize]: GET /api/gauntlet/shop, POST
  /api/gauntlet/shop/{entryId}/buy (Success/AlreadyCharged→200, AlreadyOwned→409, InsufficientTokens→422,
  unknown→404). DTOs GauntletShopEntryResponse/GauntletShopResponse/BuyShopResult. New GauntletService ctor
  deps: IGauntletShopProvider, ILegionService, IEquipmentService. (Pre-existing BuyUnitIdempotencyTests
  given an EmptyShopProvider override so its single-unit IUnitDefinitionProvider stub doesn't fail shop
  boot validation.) +18 unit + 2 integration (buy-twice-charges-once + currency isolation vs real Postgres
  ledgers). Build 0 errors; **652 unit + 69 integration green**. No new migration.

- **Slice 7 (loop completion)** (branch `feat/system16-gauntlet-s7-loop`): makes the Gauntlet
  **end-to-end playable** — ladder summon/auto-advance + cross-event rank-magic hand-off. **ZERO
  combat-formula change** (HitRaidAsync untouched). (1) **Gauntlet-stage def resolution:**
  `RaidDefinitionProvider` ALSO loads `gauntlet_raids.json` and maps each `GauntletRaidDefinition` →
  `RaidDefinition` into the same id→def dict, so `HitRaidAsync`'s `GetById(raid.RaidDefinitionId) ?? throw`
  resolves `gauntlet_stage_N` unchanged (lootTableId "" → benign kill-reward pass; throws on id collision
  with raids.json). Gauntlet combat stays gated on `ActiveRaid.GauntletEventId`, not the def. (2) **Ladder:**
  `IGauntletService.GetLadderAsync(playerId)` — active event + joined entry required; returns the player's
  ACTIVE gauntlet stage, else lazily spawns `nextStage = (highest defeated)+1` (Personal, GauntletEventId-
  stamped, MaxHp = stage baseHp, NO difficulty mult, ExpiresAt = event end) up to the 6-stage ceiling, else
  Complete. Auto-advance = next stage spawned on the next call after a defeat; stage # parsed from
  RaidDefinitionId. **NO new entity / NO migration** (progress derived from ActiveRaids). New repo method
  `IActiveRaidRepository.GetGauntletStagesForPlayerAsync`. `GET /api/gauntlet/ladder` [Authorize] →
  `GauntletLadderResponse {ActiveRaid?, CurrentStage, StageCount, Complete, JoinedRequired, NoActiveEvent}`.
  Projection reuses `RaidService.GetRaidByIdAsync`. New GauntletService ctor deps: `IActiveRaidRepository`,
  `IRaidService`. (3) **List exclusion:** `GetActiveRaidsAsync` excludes `GauntletEventId != null` (ladder
  stages reached only via /ladder). (4) **Rank-magic hand-off:** `OpenEventAsync` → after create+activate,
  `IGauntletEventRepository.GetMostRecentSettledAsync()` → grant its ranked winners (LastRank + band.MagicId)
  their consumable on the NEW event via `PlayerEventMagic.GrantAsync` (idempotent FindAsync pre-check) — prior
  rank-1 = current Wrath owner ×1.25, ranks 2–10 = current Blessing owner. This is the deferred spec step 2e
  (belongs at open, not settle). +10 unit (7 GetLadderAsync + 3 open-hand-off) + 6 integration
  (`GauntletLadderTests`: gauntlet hit resolves end-to-end + strikes + score; defeat reward; list exclusion;
  ladder spawn-then-same; not-joined; open hand-off vs real Postgres). Build 0 errors; **662 unit + 75
  integration green**. No new migration. **The finite-6-stage ladder ceiling is TUNABLE** — add stages /
  formula-extend HP in `content/gauntlet_raids.json` for deeper climbs (no code change).

## Not implemented (High)
Game client (C# SDK = v0.3.0) · discernment quest-drop-quality (later) ·
moderation (back-burnered) · world chat · guild · gacha/pity ·
equipment crafting / consumables · gear set bonuses (Phase 2) ·
structured log sink / monitoring · background jobs.
(System 16 Gauntlet is now loop-complete through Slice 7 — see the System 16 section above.)

## Known issues / debt (High)
- (Resolved v0.2.5: reward atomicity — stamina spend now inside advisory-lock tx.)
- (Resolved v0.2.5: ProcBonus type — now `long`.)
- **(Resolved 2026-06-02: gem-buy lost-purchase recovery.)** `GemService.SpendGemsAsync` now returns a
  tri-state `GemSpendOutcome` (Charged / AlreadyProcessed / InsufficientBalance). All 3 shops
  (magic/unit/legion) treat AlreadyProcessed as success and re-run the idempotent grant, so a
  charged-but-not-granted retry recovers the item with no double-charge. A real Testcontainers integration
  test (`BuyUnitIdempotencyTests`) proves single-charge + re-grant against the live ledger + unique index.
  **Still PHASE-2:** wrapping spend+grant in one DB transaction (needs a cross-repo transaction-scope
  abstraction) — the recovery path makes this a hardening step, not a correctness fix; `// PHASE-2` notes
  are in all 3 buy methods.
- (Documented Phase-2, pre-existing: `GemService` concurrent balance-overspend — non-atomic balance check
  + insert can drive balance negative under contention across *different* referenceIds; advisory lock deferred.)

## Balance values (accepted per CURRENT_TASK; tunable in appsettings)
- **Regen pacing — accepted:** Conscript = 5.0 min/point for BOTH energy and stamina (~2 h for full
  energy); higher classes regen faster (Eternal = 1.0). `ClassConfig.RegenMinutesPerPoint`. Offset by
  level-up RefillResource + future consumables. Revisit only if beta feedback shows it's too slow.
- GuildStamina regenerates at 2.0 min/point.

## To verify (below High)
Test coverage % · exact lockout thresholds · production deployment topology.

## Key docs
`docs/OPERATIONS.md` · `docs/ARCHITECTURE.md` · `docs/CURRENT_TASK.md` · `docs/DESIGN_NORTHSTAR.md`.
