# ROTA Closed-Beta Triage Batch

_Source: closed-beta playtest feedback, triaged 2026-06-22 via a multi-agent root-cause pass (12 parallel investigations + compile). 30 tickets, root causes pinned with file:line._

## ▶ RESUME HERE — Editor-verify the Unity batch, then deploy (2026-06-23 PM)
**State:** Backend Tier 1–3 + gem/Pano/XP batch DONE (`dotnet build` 0 err, **1005 unit green**, UNCOMMITTED on `main`). **The full Unity client batch below is now DONE** (write + cross-file compile-sanity audit + lead spot-review; UNCOMMITTED on client branch `client/webgl-build`). **3 migrations NOT applied**: `AddQuestProgressDifficulty`, `WidenGemAmountToBigint`, `WidenStatAndRewardFieldsToBigint` — idempotent SQL pre-generated at `deploy/migrate.sql` (gitignored). Deploy commands ready (backend live at api.riseoftheancients.com; client WebGL → play.riseoftheancients.com).
**Repos:** backend `C:\Users\xolac\OneDrive\Documentos\Projects\ROTA`; client `C:\Dev\ROTA.Client6` — Unity 6, code-first UI Toolkit, **CANNOT headless-compile here** → owner verifies in Editor Play mode.

### Unity client batch — DONE 2026-06-23 (UNCOMMITTED, branch `client/webgl-build`; needs Editor Play-mode verify)
Wired via a 6-agent workflow (foundation → 4 parallel screen agents → compile-sanity audit) + lead spot-review. Audit verdict: **compiles, no blockers**; IRotaApi gained `GetMagicCatalogueAsync` + `OnSessionExpired`, both implemented in Http + Mock. Files touched: Dtos.cs, IRotaApi/Http/MockRotaApi.cs, AppBootstrap.cs, LoginScreen.cs, RaidScreen.cs, RaidCombatView.cs, QuestScreen.cs, ProfileScreen.cs, BazaarScreen.cs, ItemsScreen.cs (+ pre-existing edits in Theme.uss/HeaderBar/LeaderboardScreen/EquipmentScreen/BuildPlayer/build-client.ps1/ProjectSettings).
- **Foundation/contract:** int→long DTO mirror (Gems/Effective*/reward fields) matched to backend; ADDED `InventoryItemResponse.Tier`, `RaidHitResponse.NewHealthValue/Max`, `AllocateStatResponse.Effective*`, `MagicCatalogueEntry/Response`. `HttpRotaApi.TryRefreshAsync` now serialized via static `SemaphoreSlim` + capture-before-lock rotation re-check (FIXES the concurrent-refresh silent-logout) and fires new `OnSessionExpired` on genuine refresh failure.
- **Auth/bootstrap:** LoginScreen hardcoded `admin@rota.local`→`""`; AppBootstrap subscribes `OnSessionExpired`→fresh LoginScreen (UI-thread marshaled) and `_state.LeveledUp`→class-gate check (FIXES class-gate-only-on-relog); Dev Tools gated `useMock || viewer.IsAdmin` (was `Debug.isDebugBuild` — FIXES tutorial-reappears; TODO: surface Developer role client-side).
- **Raids:** boss-card tier label from `Tier` (was hardcoded "World raid"); per-hit Health-bar patch from `NewHealthValue/Max` (FIXES frozen health bar); `LoadCompleted()` after claim; client sort pill-row + per-card fast Share; DoHit + quest-attempt debounce.
- **Profile/stats:** ATK/DEF chips patched synchronously from the allocate response's `Effective*`; bulk +10/+100/+1k/+10k/+100k(+Clear) alloc buttons (backend cap is 100M).
- **Bazaar/items:** Magics tab now calls `GET /api/magics/catalogue` → full catalogue (Owned/Buy/Not-for-sale affordances); ItemsScreen Material/Equipment show clarifying labels + disabled Use.
- **MOCK-FIDELITY FIXES (lead, post-audit):** MockRotaApi.AllocateStatAsync now sets `Effective*` (chips no longer flash to 0 in mock); HitRaidAsync now sets `NewHealthValue/Max` (mock Health bar moves). Both were the audit's only `major` findings — owner playtests in mock, so these would have looked like backend bugs.
- **Known minor (left):** BazaarScreen.BuildMagicCard(MagicShopEntry) is now dead code (harmless, unused private). `GetMagicShopAsync` kept (other callers).

**Unity queue (DONE — kept for reference):**
1. **Wire the shipped backend hooks** (highest leverage; server side done + tested):
   - `InventoryItemResponse.Tier` → `RaidScreen.BuildBossCard` (stop hardcoding "World raid").
   - `AllocateStatResponse.EffectiveAttack/EffectiveDefense` (long) → patch Profile ATK/DEF chips from the allocate response.
   - `RaidHitResponse.NewHealthValue/NewHealthMax` → patch the Health bar per hit (was frozen after hit 1).
   - `GET /api/magics/catalogue` (`MagicCatalogueEntry.IsOwned/ForSale/GemPrice`) → Bazaar Magics tab full catalogue.
   - `PlayerProfileResponse.XpToNextLevel` (long) → HeaderBar "x/xxxx TNL" already wired this session (verify in Editor).
   - **int→long DTO mirror** (`Dtos.cs`): backend widened Gems/XP/stat/reward/effective fields to `long`; mirror them (e.g. `PlayerProfileResponse.Gems` is still `int` in the mirror) so big late-game values don't truncate.
2. **Correctness fixes:** `login-hardcoded-dev-email` (LoginScreen.cs ~109 `admin@rota.local`→""), dev-tools role-gate (`bug-tutorial-reappears-l22`, AppBootstrap), `bug-class-gate-only-on-relog`, `auth-no-login-redirect-on-401`, **auth `SemaphoreSlim`** concurrent-refresh session-wipe (HttpRotaApi — the silent-logout / "could not reload" 401s).
3. **UX/features:** raid completed-tab refresh, guild-autohit label, stat-alloc bulk buttons, raid share/sort UI, items consumable labels, request debounce.
Per-ticket detail + file:line in "Tickets by area" (Auth/Quests/Raids/… below). UI redesign roadmap (Wave 0→4) in `C:\Dev\ROTA.Client6\docs\UI_REDESIGN_NOTES.md`.

## Decisions LOCKED (2026-06-22)
- **Int-overflow:** migrate **everything** to `long`/`bigint` (gems, XP, gold, reward DTOs, AND stat fields). No caps.
- **Quest-boss gems:** completion-only; **flat +2 gems** on a roll — Normal 3% / Hard 5.8% / Legendary 8.3% / Nightmare 11.5%.
- **Raid-boss gem parity:** drop `baseGemReward` to mirror quest-boss base (1/2/3/4/5/6 by chapter).
- **Boss-kill XP:** **REMOVE the on-kill XP bonus entirely** — the killing blow awards only the normal per-hit XP (throughput model). Not a value tune.
- **Early TNL pace:** **PAUSED** until the kill-XP fix lands, then re-judge pacing.
- **Node-depletion:** add `difficulty` to `player_quest_progress`; each difficulty fully independent (depletion/IsCleared/HasEverCleared/zone-gate/reset). Difficulty *unlock* stays on `PlayerQuestDifficultyProgress`. Migration `AddQuestProgressDifficulty` (backfill Normal).
- **LSI cap:** 9.0 (canonical). **Stat-alloc per-call cap:** removed (service already guards SP+LSI).

## Overnight automation (2026-06-23 ~02:40 CDT) — UNATTENDED
Owner asleep; authorized autonomous burn-down on current usage + a 04:50 refire at usage reset.
- **RUNNING NOW:** int32 Unit 2 workflow (run `wf_1edac4ae-48e`). On completion the session is re-invoked → finalize (build/test/migration review/docs) → if usage allows, launch the backend Tier 2-3 burn-down (`burndown-tier23.workflow.js`, in the `.claude/projects/.../` session dir).
- **04:50 cron FIRED + COMPLETED.** Assessed state (Unit 2 + burn-down already done → skipped). **CLIENT UI (Wave 0, ROTA.Client6, UNVERIFIED — owner verifies in Editor Play mode):** login Obsidian-Gilt redesign confirmed intact (12 class refs / 16 USS rules); added `:root` design-token block + 2 referenced-but-undefined rules (`.combat__autohit`, `.quest-card__result`) to Theme.uss (all ADDITIVE — no existing rule touched, login pixel-stable); wrote `C:\Dev\ROTA.Client6\docs\UI_REDESIGN_NOTES.md` (Wave 0→4 plan + per-screen polish + token-migration pattern + the backend hooks the client should now wire up). Remaining Wave-0 (shared-class token migration/polish, UiKit C# extraction) DEFERRED to Editor (C# can't be headless-compiled here).
- Everything stays UNCOMMITTED; migrations NOT applied. No double-apply (cron skips done work).

## Progress
- **Batch 7 DONE + verified (build 0 err, 1005 unit tests green; NO migration) — UNCOMMITTED (owner-directed 2026-06-23 AM):**
  - **Gem model — UNIFIED chance-scaling (CORRECTED — I first mis-scaled the AMOUNT; owner wants the CHANCE scaled, amount stays flat 2):** boss gems = a flat **2 gems** (`QuestConfig.BossGemRewardAmount`) dropped on a per-chapter-scaled CHANCE. `ResolveBossGemChance(chapter, difficulty)` = `BossGemDropChance[diff] × min(1, chapter/GemChanceFullChapter(6))` — rarer early, reaching the per-difficulty GOAL at Ch6 (Normal 3% / Hard 5.8% / Legendary 8.3% / Nightmare 11.5%). Applies to **BOTH quest bosses AND raid bosses** (owner: unified). QuestService rolls it on boss clear. RaidService (`DistributeKillRewardsAsync`): replaced the old `BaseGemReward × contribution-tier` grant with the same chance roll per Rare+ participant (raid chapter parsed from the `c{ch}z{z}b` id; non-chapter raids → full-goal chapter); injected `IOptions<QuestConfig>`. `raids.json baseGemReward` + `RaidDefinition.BaseGemReward` are now VESTIGIAL (raids.json reverted to original via `git checkout`). NO "World" special-casing — raids are just raids until one is explicitly designated. Tests: `ResolveBossGemChance` theory (5 cases) + unknown-difficulty; existing kill/loot gem tests unchanged (Participant-excluded + deferred-to-Loot still hold). ⚠️ FLAG: raids are now a LOW gem source (chance per Rare+ contributor, was amount×tier) — playtest + tune the goal %s / `GemChanceFullChapter` if raids feel too stingy. ⚠️ Ramp is per-CHAPTER (owner-chosen); refine to per-zone later if wanted.
  - **Pano set (gear.json):** 7 pieces → **15/15** ATK/DEF; steed (Mount) → **45/45** + proc **7% / procPercent 1.2** (combat applies `procBonus = preProc × procPercent` → proc adds 120% of base; ⚠️ FLAG: if you meant "hit does 120% total" i.e. +20%, set procPercent 0.2).
  - **XP "x/xxxx TNL" display:** backend `PlayerProfileResponse.XpToNextLevel` (long) populated via injected `IStatService` (Experience = current-level XP numerator); client HeaderBar shows `"Lv N   x / xxxx TNL"`, preferring the server value, local-formula fallback for mock. Client (Dtos.cs mirror + HeaderBar) UNVERIFIED — owner checks in Editor.

- **Batch 6 DONE + verified (build 0 err / 0 warn, 999 unit tests green; NO new migration) — UNCOMMITTED:** backend **Tier 2-3 burn-down** (workflow + 3-lens adversarial verify: regression PASS, correctness only minor/cosmetic, completeness strong). **Raid cluster (7):** `raid-sort-nondeterministic` (OrderBy CurrentHp), `guild-raid-stamina-wrong-pool` (GuildStamina branch in hit response), `health-drain-first-hit-only` (RaidHitResponse.NewHealthValue/Max), `raid-loot-no-all-claimed-expiry` (Loot() fires when last participant claims) + `raid-lifecycle-state-stale-enum-doc`, `raid-completed-tab-shows-all-history-forever` (30-day `since` window), `raid-tier-label-hardcoded-world` (InventoryItemResponse.Tier from IRaidDefinitionProvider). **`atk-def-chip-stale-on-alloc`:** AllocateStatResponse gains Effective{Attack,Defense} (long), populated post-alloc. **`response-compression`:** AddResponseCompression (Brotli+Gzip). **Content tuning (flagged for owner):** gem parity raids.json baseGemReward→1-6 by chapter (World bosses kept 2/4 premium); Pano set +21/+22→+8/+8 (~⅔ cut, steed proc kept); XpExponent 0.7→0.8. **`bazaar-catalogue-endpoint` = NO-OP** (already shipped as `GET /api/magics/catalogue` with `IsOwned` — idempotency guard caught it; client just needs to call it). Post-verify micro-fixes: LSI cap msg `:F1`→`:F2` (showed 7.5); boss-gem comment accuracy. **NOT DONE (deferred, owner call):** audit_log retention/partitioning half of `response-compression-and-data-size`.

- **Batch 5 DONE + verified (build 0 err, 998 unit tests green; migration NOT applied) — UNCOMMITTED:** `int32-overflow-audit` **Unit 2 — stat + reward fields → long/bigint** (via workflow + adversarial verify). `PlayerStats.{BaseAttack,BaseDefense,SkillPoints,EnergyInvestment,StaminaInvestment,DiscernmentInvestment}` + `RaidParticipant.{GemsEarned,StatPointsEarned,XpEarned}` → long; EF configs → bigint; `EffectiveCombatData`/`GetEffectiveCombatDataAsync`/`ComputePowerAsync`/Gauntlet battalion accumulators → long; reward+stat DTOs → long. Migration **`WidenStatAndRewardFieldsToBigint`** (9 lossless AlterColumn int→bigint; snapshot test PASS) — **NOT applied.** **Post-verify follow-up fixes (this session):** truncation bug RaidService:1245 XP-proc `(int)`→`(long)` cast (MAJOR, fixed); QuestService `RollEnergyXp`/`XpPreview`→long + `QuestResultResponse.XpGained` + `QuestAvailabilityResponse.EffectiveXpReward`→long (quest XP no longer int-capped); `DevGrantRequest.{Gems,SkillPoints}`→long (symmetry). Left int (bounded/per-claim, documented safe): RaidRewards stat-point granted fields, resource-pool ComputeMax, GetCritProfile(Discernment). **int32-overflow-audit is now COMPLETE (Unit 1 gem ledger + Unit 2 stat/reward).**

- **Batch 1 DONE + verified (build 0 err, 188 unit tests green) — UNCOMMITTED on `main`:** `stat-alloc-validator-cap` (≤100→≤100M), `stat-alloc-lsi-cap-wrong` (8.0→9.0; **then owner-lowered to 7.45 on 2026-06-23 "for now"** — `StatService.LsiCap`), `stat-alloc-friend-404` (resolved — was the cap), `boss-raid-kill-xp-inflated` (zeroed kill-XP grant RaidService:1542 **and** the report :1674).
- **Batch 2 DONE + verified (build 0 err, 997 unit tests green) — UNCOMMITTED on `main`:** `quest-boss-gem-on-every-hit` — gems are now a BOSS-CLEAR reward only: gated `quest.IsBoss && nodeJustCleared`, flat `QuestConfig.BossGemRewardAmount` (=2) on a per-difficulty roll `BossGemDropChance` (Normal .030/Hard .058/Legendary .083/Nightmare .115), referenceId `questbossgem:{quest}:{player}:{difficulty}:{diffProg.CompletionCount}` (unique-per-clear, idempotent — diffProg count never reset by a zone reset). Old `gemReward` JSON field now vestigial for bosses. Battle nodes were already `gemReward:0`. Tests: rewrote the 1 non-boss gem test → 4 boss-gem tests (clear-grants / not-cleared-no-gems / non-boss-no-gems / roll-fail-no-gems).
- **Batch 3 DONE + verified (build 0 err, 998 unit tests green; migration NOT applied) — UNCOMMITTED on `main`:** `node-depletion-per-difficulty` — `PlayerQuestProgress` now keys on `(player_id, quest_id, difficulty)`. **Owner decision 2026-06-23: PER-DIFFICULTY LADDER** — every piece of state (Progress/IsCleared/**HasEverCleared** forward-unlock/CompletionCount) is per-difficulty, so each tier is progressed in order on its own track (full front-to-back replay per difficulty). `IQuestProgressRepository.GetAsync` + all QuestService call sites (prereq/zone-boss-gate/node-lock/deplete/create/`ResetZoneAsync`) are difficulty-scoped; `GetAvailableQuestsAsync(playerId, difficulty=Normal)` + `GET /api/quests?difficulty=` (no param → Normal, shipped client unaffected — display-only; the exploit is closed at ATTEMPT time). Migration **`AddQuestProgressDifficulty`** (drop old uq index → add `difficulty int NOT NULL DEFAULT 0` backfilling existing rows to Normal → new uq index on player+quest+difficulty) — **NOT applied, owner runs `dotnet ef database update`.** Tests: per-difficulty exploit-fix test + all GetAsync/Create mocks updated to the 4-arg signature. NOTE: existing beta players' Hard/Legendary/Nightmare progress resets to fresh (backfill = Normal only) — accepted, closes the exploit, small closed beta.
- **Batch 4 DONE + verified (build 0 err, 998 unit tests green; migration NOT applied) — UNCOMMITTED on `main`:** `int32-overflow-audit` **Unit 1 of 2 — gem ledger → 64-bit** (the genuine overflow: balance = `SUM(amount)` grows unbounded over a no-reset lifetime). `GemTransaction.Amount` int→long; `GetBalanceAsync` chain (repo/iface/service)→long; `GrantGemsAsync`/`SpendGemsAsync`/`TrySpendAsync` amount params→long (+ raw-SQL `neg`/`cost` → `NpgsqlDbType.Bigint`); `PlayerService` gemBalance + `PlayerProfileResponse.Gems`→long. Migration **`WidenGemAmountToBigint`** (AlterColumn int→bigint, lossless widening — safe on live data) — **NOT applied, owner runs `dotnet ef database update`.** Tests: 41 gem-amount `It.IsAny<int>()` mock matchers → `It.IsAny<long>()` (typed matcher must match the long param).
- **Next, in order:** `int32-overflow-audit` **Unit 2 — stat fields + reward entity/DTO fields → long** (ripples through combat math; well-mapped — see below) → auth `SemaphoreSlim` (concurrent-refresh session-wipe + 401 race; **client/Unity fix**, not backend). NOTE: integration suite NOT run this session (Docker down) — hermetic migration-snapshot gate in the unit suite covers model↔migration consistency; owner can run integration tests once Docker is up.

### `int32-overflow-audit` Unit 2 — blast radius (mapped, ready to execute)
- **Entities → long:** `PlayerStats.{BaseAttack,BaseDefense,SkillPoints,EnergyInvestment,StaminaInvestment,DiscernmentInvestment}`; `RaidParticipant.GemsEarned` (+ Attack/Defense/Discernment/StatPoints earned if widening those). EF: `PlayerStatsConfiguration` + `RaidParticipantConfiguration` column types → bigint; migration on `player_stats` (+ `raid_participants`).
- **Combat ripple (needs casts/signature changes):** `EffectiveCombatData.{EffectiveAttack,EffectiveDefense}` int→long (IEquipmentService.cs:23-27); `IEquipmentService.GetEffectiveCombatDataAsync(int baseAtk,int baseDef)` params→long; `EquipmentService.cs:235` arithmetic; `GauntletBattalionService.cs:94-96` int locals + `atkSum/defSum` accumulators; `RaidService.cs:897` already uses `4L` (safe). Damage formulas mostly `double`/`long` already.
- **Reward DTOs → long:** `QuestResultResponse.{GoldGranted,ExperienceGranted,GemsGranted}`; `RaidRewards.{ExperienceGranted,GemsGranted}` (GoldGranted already long); `RaidHitResponse.XpGained` (GoldGained already long). Feeders are int and widen losslessly; optionally widen the `(int)`-cast gold/xp computations.
- **Stat DTOs → long:** `AllocateStatResponse.{NewSkillPointsRemaining,NewEnergyInvestment,NewStaminaInvestment,NewDiscernmentInvestment,NewMaxEnergy,NewMaxStamina,NewMaxGuildStamina,AmountAllocated}`; `PlayerStatsResponse.{SkillPoints,EnergyInvestment,StaminaInvestment,DiscernmentInvestment,BaseAttack,BaseDefense,BaseMaxHealth,CurrentHealth,EffectiveAttack,EffectiveDefense}`. `ComputeMaxEnergy/Stamina` return int (widen). `LeaderboardService` DiscernmentInvestment → SetValueAsync (Value already long, fine).
- **Tests:** StatServiceTests/RaidServiceTests int assertions on these fields compare fine vs long; watch any `It.IsAny<int>()` matchers on widened params (same fix pattern as the gem matchers). **CLIENT (Unity) DTO mirror = separate later step.**
- **Deploy:** all backend fixes uncommitted on `main`; reach the live beta via `git stash → pull → up -d --build` on the droplet (docker-compose.prod.yml has a local edit that blocks a plain pull).

---

## Owner decisions needed (FIRST)

### 1. INT-OVERFLOW STRATEGY (ticket `int32-overflow-audit`)
**Type widths:** .NET `int` = signed 32-bit, max **~2.1 billion** (2³¹−1). .NET `long` = signed 64-bit, max **~9.2 quintillion** (2⁶³−1). Postgres `integer` = 32-bit, `bigint` = 64-bit, `numeric` = arbitrary precision (slow, only for truly unbounded values).

**Currently in ROTA (from triage):**
- **Already safe (`long`/`bigint`):** `Player.Experience`, `Player.Gold`, `ActiveRaid.CurrentHp/MaxHp`, `GauntletEntry.Score`, `LeaderboardEntry.Value`.
- **At risk (`int`/`integer`):** `GemTransaction.Amount` (ledger balance = SQL `SUM(amount)` → overflow risk even with safe individual rows); `QuestResultResponse.{GoldGranted,ExperienceGranted,GemsGranted}`; `RaidHitResponse.XpGained`; `PlayerStats.{BaseAttack,BaseDefense,SkillPoints,EnergyInvestment,StaminaInvestment}` (250k SP by L25000, uncapped ATK growth).

**RECOMMENDATION — migrate to `long`/`bigint`:** gems (ledger sum), XP, gold, all reward DTO fields. DotD lapped int32 late-game; ROTA's no-reset capped-scaling design will too. Do **not** use `numeric` anywhere — none of these are truly unbounded; `bigint` headroom (9.2 quintillion) is sufficient. For `BaseAttack/BaseDefense/SkillPoints`: keep `int` **only if** you add a server-side soft cap (e.g. 99,999/stat) in the allocate path; otherwise also migrate to `long`. **DECISION:** (a) confirm long/bigint for gems+XP+gold+reward DTOs; (b) soft-cap-int vs long for stat fields; (c) gem-balance ceiling target.

### 2. GEM DROP RATES — quest bosses (`quest-boss-gem-on-every-hit`)
Bonus = **2 gems** on a per-difficulty chance roll on boss clear. Anchors: Normal **3%**, Nightmare **11.5%**.
**PROPOSED interpolated curve:** Normal 3% · **Hard 5.8%** · **Legendary 8.3%** · Nightmare 11.5% (roughly linear). **DECISION:** confirm curve; flat 2 gems vs scaled 2/3/4/5 by difficulty; whether reruns (post-zone-reset) roll the same chance or lower/zero; whether the legacy `gemReward` JSON field is removed or repurposed as a first-ever-clear guaranteed drip.

### 3. RAID-BOSS GEM PARITY (`gem-parity-raid-vs-quest-boss`)
**Current quest-boss base gem value:** `gemReward` = 1/2/3/4/5/6 for Ch1–Ch6 (× difficulty mult ×1.0/1.5/2.0/3.5; Ch6 Nightmare ceiling = round(6×3.5)=**21**).
**Current raid-boss base gem value:** `baseGemReward` = 2/5/12/28/64/140 for Ch1–Ch6 (× contribution tier; Ch6 Legendary1 = round(140×1.5)=**210** — ~10× the quest ceiling).
**PROPOSED:** drop raids.json `baseGemReward` to **1/2/3/4/5/6** (mirror quest-boss base) across all 25 boss-raid entries. Content-only, no code. **DECISION:** cap before or after tier×difficulty mult; whether World-tier raids (Iron Colossus=2, Malachar=4) keep a coordination premium; whether the 23 auto-gen Standard bosses get a separate curve.

### Other cross-ticket decisions
- **LSI cap (`stat-alloc-lsi-cap-wrong`):** code enforces **8.0**, docs/CLAUDE.md/entity say **9.0**. Pick canonical (recommend 9.0). One-line const.
- **Stat-alloc per-call cap (`stat-alloc-validator-cap`):** remove the 100 ceiling or raise to 10,000? (Service already guards SP+LSI.)
- **Dev Tools visibility (`bug-tutorial-reappears-l22`):** gate whole screen behind `Developer` role, or just the destructive buttons?
- **TNL exponent (`tnl-curve-too-flat-early`):** target hours-to-L20 anchors the value (0.7→0.85 vs 0.9, or floors-only).
- **Solo boss-kill XP cap (`boss-raid-kill-xp-inflated`):** max level-jump per solo kill (1 level? 0.5?) → back-calculates baseExperienceReward.
- **Quest-progress-per-difficulty design (`node-depletion-per-difficulty`):** confirm per-difficulty `IsCleared` zone-gate, which row(s) set permanent `HasEverCleared` forward-unlock, per-difficulty `ResetZoneAsync` scope.
- **WebGL beta target (`auth-webgl-device-id-fallback-same-key`):** is WebGL shipping for beta? Determines if the shared-AES-key fix is in-scope now.

---

## Tickets by area

### Auth
- **`auth-401-token-expiry-race` — High/Bug.** Idle 15+ min → tap Quest/Profile → intermittent "Could not load (401)". **Root cause:** zero `ClockSkew` (Program.cs:100) + reactive-only refresh; client sends expired token, gets 401, then refreshes (HttpRotaApi.cs:1178). No proactive pre-flight, no concurrency guard. **Fix:** client pre-flight refresh when `AccessTokenExpiry - UtcNow < 60s`, serialized via `SemaphoreSlim(1,1)` + shared in-flight `Task<bool>`. Backend replay logic stays. _Open Q: 60s vs 120s buffer; WebGL hidden-tab throttle may need a focus-reconnect hook._ **(Overlaps `auth-concurrent-refresh-session-wipe`.)**
- **`auth-concurrent-refresh-session-wipe` — High/Bug.** Fast Quest→Profile nav at token expiry → both 401 → both refresh with same token → second loses `TryRevokeAsync` conditional-UPDATE race → replay detection wipes ALL sessions → silent logout. **Root cause:** `TryRefreshAsync` (HttpRotaApi.cs:1072) has no mutex; AuthService.cs:239-244 correctly treats 0-row UPDATE as replay. **Fix:** static `SemaphoreSlim _refreshLock(1,1)`; after acquiring, re-check `_tokens.HasTokens` and return true if another thread already refreshed. Backend unchanged.
- **`auth-no-login-redirect-on-401` — Medium/Bug.** Expired session → "Could not load (401)" text, no nav back to login; player stuck. **Root cause:** no `OnSessionExpired` signal; AppBootstrap.cs:194 no hook. **Fix:** add `event Action OnSessionExpired` (fired after `_tokens.Clear()`); AppBootstrap → fresh LoginScreen.
- **`login-hardcoded-dev-email` — High/Bug.** `admin@rota.local` pre-filled in Email, ships in every build incl. WebGL. **Root cause:** LoginScreen.cs:109 hardcoded `value = "admin@rota.local"`. **Fix:** empty string; optionally editor-only devEmail via AppBootstrap when `useMock`.
- **`auth-webgl-device-id-fallback-same-key` — Medium/Bug.** All WebGL users share one fixed AES key (TokenStore.cs:113-132 derives from public app salt) → any same-origin script decrypts `rota_tokens.dat`. **Fix:** random 32-byte key via `crypto.getRandomValues` into localStorage (JSLib), or sessionStorage + re-login. 15-min access token limits blast radius. _Open Q: is WebGL a beta target._

### Quests
- **`node-depletion-per-difficulty` — CRITICAL/Bug (EXPLOIT).** Deplete a boss node on Normal (~19 cheap attempts), switch to Nightmare — shared `Progress` row is already ~5 so one Nightmare hit clears it and the guaranteed first-clear Nightmare sigil drops at ~1/20th intended cost. Works for Hard/Legendary too. **Root cause:** `PlayerQuestProgress` keyed `(player_id, quest_id)` only — no difficulty column; depletion/lock/reset all share this table. **Fix:** add `difficulty` column; extend unique index + `GetAsync`/`Create`/`ResetZoneAsync`/`GetAvailableQuestsAsync` to be difficulty-scoped; migration `AddQuestProgressDifficulty`. Files: PlayerQuestProgress.cs:8, PlayerQuestProgressConfiguration.cs:55, IQuestProgressRepository.cs:8, QuestProgressRepository.cs:24, QuestService.cs:291/362/528-542.
- **`quest-boss-gem-on-every-hit` — CRITICAL/Bug (ECONOMY).** Boss gems granted on every hit (~40/clear) — Ch6 Nightmare = 21×40 = 840 gems/cycle instead of one completion reward. **Root cause:** QuestService.cs:336/393-399 grant unconditionally each successful attempt; referenceId keyed on `CompletionCount` which increments per attempt (line 365). **Fix:** wrap gem grant in `if (quest.IsBoss && nodeJustCleared)`, replace flat reward with per-difficulty chance roll (2 gems), stable referenceId after RecordCompletion. _See decision §2._

### Raids
- **`raid-loot-no-all-claimed-expiry` — High/Bug.** Fully-claimed raids stay `Lootable`/`is_deleted=false` forever; rows accumulate. **Root cause:** `LootRaidAsync` (RaidService.cs:563) never calls `raid.Loot()`; `Loot()`/`Looted` dead code since T57. No cleanup job. **Fix:** after `TryClaimRewardsAsync`, if no participant has `RewardedAt IS NULL` → `raid.Loot()` → `Looted` + `UpdateAsync`; background prune of old `Looted`/stale `Lootable`. _Open Q: flip-on-last-claim vs cooldown; hard vs soft delete + retention._
- **`raid-sort-nondeterministic` — High/Bug.** Public list shuffles every load. **Root cause:** `GetAllActiveAsync` (ActiveRaidRepository.cs:31-35) no `OrderBy`. **Fix:** `.OrderBy(r => r.CurrentHp)` (low-HP-first). **(Pairs with `raid-list-sort-controls`.)**
- **`guild-raid-stamina-wrong-pool-in-response` — High/Bug.** Guild-raid hit response returns regular Stamina under the "Guild Stamina" label; GuildStamina never decrements until profile re-fetch. **Root cause:** RaidService.cs:1381-1382 always reads `ResourceType.Stamina` ignoring `isGuildRaid`. **Fix:** branch `resourceType = isGuildRaid ? GuildStamina : Stamina`.
- **`raid-completed-tab-shows-all-history-forever` — Medium/Bug.** Completed tab shows all-time looted history (≤50), no recency. **Root cause:** `GetCompletedForPlayerAsync` (RaidParticipantRepository.cs:29) no date filter. **Fix:** optional `recentDays`/`since` (default ~30d). **(Overlaps `raid-completed-tab-no-refresh-after-claim`.)**
- **`raid-completed-tab-no-refresh-after-claim` — Medium/Bug (client).** History doesn't update after Loot; needs nav-away/back. **Root cause:** loot handler (RaidScreen.cs:492) manual `RemoveFromHierarchy`, no `LoadCompleted()`. **Fix:** call `LoadCompleted()` after claim.
- **`raid-tier-label-hardcoded-world` — Medium/Bug.** All Summon-tab boss cards show "World raid" even for Standard zone bosses (C1z1b = Ashen Causeway, C1z2b = Hollow Marches). **Root cause:** RaidScreen.cs:854 hardcoded `"World raid · "`; `InventoryItemResponse` has no Tier. **Fix:** add `Tier` to `InventoryItemResponse`, populate from raid def, use in BuildBossCard.
- **`guild-raid-autohit-stop-wrong-label` — Low/Bug.** Auto-hit stop says "not enough stamina" on guild raids. **Root cause:** RaidCombatView.cs:994 hardcoded. **Fix:** branch on `IsGuildRaid` → "guild stamina".
- **`raid-lifecycle-state-stale-enum-doc` — Low/Bug.** `RaidLifecycleState` XML doc says "rewards granted on kill" — contradicts T57. **Fix:** docs-only. **(Bundle with `raid-loot-no-all-claimed-expiry`.)**

### Stats
- **`stat-alloc-validator-cap` — High/Bug.** `amount > 100` → 400 regardless of available SP. **Root cause:** StatValidators.cs:22 `.LessThanOrEqualTo(100)` placeholder. **Fix:** raise to 10,000 (or remove; service already gates SP+LSI). **(Blocks `stat-alloc-ui-bulk-buttons`; resolves the "friend 404".)**
- **`stat-alloc-lsi-cap-wrong` — High/Bug.** Code enforces LSI **8.0**, docs say **9.0**. **Root cause:** StatService.cs:12 `LsiCap = 8.0`. **Fix:** set to 9.0 (canonical).
- **`int32-overflow-audit` — High/Bug.** See decision §1. **Fix:** migrate gem ledger + reward DTOs to `long`/`bigint`; cap or migrate stat fields. Files: GemTransaction.cs:28, QuestDTOs.cs:57-59, RaidDTOs.cs:52-53/103-104/155-156, PlayerStats.cs:29-39, PlayerConfiguration.cs:37-48.
- **`atk-def-chip-stale-on-alloc` — Medium/Bug.** ATK/DEF hero-card chips stay pre-allocation if the post-allocate `GetProfileAsync` throws. **Root cause:** `AllocateStatResponse.EffectiveAttack/Defense` declared (StatDTOs.cs:41-42) but never populated (StatService.cs:122-135); refresh depends on the profile round-trip in the try (ProfileScreen.cs:638-651). **Fix:** (A) backend populate Effective* via `GetEffectiveCombatDataAsync`; (B) client update chips synchronously from the allocate response.
- **`stat-alloc-friend-404-investigation` — Medium/Bug (INFO).** "Got 50 back with a 404" — no refund/404 path exists on allocate. Likely the >100 cap 400 misread, or sequential partial-success. **Fix:** none until reproduced. **(Resolves once `stat-alloc-validator-cap` is fixed.)**

### Economy
- **`gem-parity-raid-vs-quest-boss` — High/Tuning.** See decision §3. Content-only raids.json reduction.

### XP / Leveling
- **`boss-raid-kill-xp-inflated` — CRITICAL/Bug.** Solo boss kill jumps 5–7 levels mid-game (L151→158). **Root cause:** System 25 auto-gen `baseExperienceReward` flagged "TUNE later", never tuned (c4z1b=4850 → 7275 XP at Legendary1 Normal ÷ TNL 1006 = 7.2 levels; Ch6 apex=33600 → 50400 XP). Grant logic correct (RaidService.cs:1542/1553). **Fix:** content-only — reduce all 25 boss `baseExperienceReward` (anchor to World baselines Iron Colossus=300, Malachar=700). _See decision §._
- **`tnl-curve-too-flat-early` — High/Tuning.** L1–50 fly by. **Root cause:** `XpExponent=0.7` (LevelingConfig.cs:5-6), no floor until L100. **Fix (hot-config, no migration):** raise `XpExponent` to 0.85–0.9 and/or low-level `MilestoneFloors` (10:120, 20:240, 50:600). _See decision §._

### Class
- **`bug-class-gate-only-on-relog` — High/Bug.** Class overlay only prompts on relog, not when L5/L100 reached mid-session. **Root cause:** `ClassGate.CheckAndShowIfPending()` called once in `onLoggedIn` (AppBootstrap.cs:204); no level-up hook. **Fix:** subscribe `_state.LeveledUp += level => { if (level == 5 || level == 100) _classGate.CheckAndShowIfPending(); }`. _Open Q: use `newLevel >= gate && prevLevel < gate` to catch skips._

### Items
- **`pano-set-nerf` — High/Tuning.** 8-piece Pano = +21 ATK/+22 DEF (~doubles base 10/10) + 10% proc @4% mount; ~3× baseline damage. **Fix:** content-only gear.json (~100-194) ~2/3 reduction; stats read per-hit so no DB action. _Open Q: final per-piece numbers; mount stays chase-worthy?_

### UI / UX
- **`bug-tutorial-reappears-l22` — High/Bug.** Tutorial re-appears (no level trigger exists). **Root cause:** Dev Tools shown to ALL when `useMock || Debug.isDebugBuild` (beta ships debug); player tapped CLEAR ALL PLAYERPREFS / RESET TUTORIAL, wiping `rota_tutorial_done_v1`. **Fix:** gate Dev Tools behind `Developer` JWT role in mock+live (AppBootstrap.cs:176); confirmation on CLEAR ALL PLAYERPREFS (SystemDevTab.cs:70-75).
- **`health-drain-first-hit-only` — High/Bug.** Health bar freezes after first raid hit. **Root cause:** backend drains correctly (EnergyService.cs:113-128) but `RaidHitResponse` has no `NewHealthValue/Max`; RaidCombatView.cs:912-918 only patches Stamina. **Fix:** add `NewHealthValue/NewHealthMax` to RaidHitResponse (+ Dtos mirror), populate (~1383/1398), client `PatchResource("Health", …)`. Mock already correct.
- **`stat-alloc-ui-bulk-buttons` — Medium/Feature.** Allocator only has ±1; 10k Attack = 10k taps. **Fix:** +10/+100/+1k/+10k/+100k composable buttons (additive) + Clear; one call per stat. **Requires `stat-alloc-validator-cap` first.**
- **`raid-share-fast-button` — Medium/Feature.** Sharing requires opening combat pop-out. **Fix:** per-card "Share" button for own Private raids; select-all + "Share All" on Public tab. Backend supports.
- **`raid-list-sort-controls` — Medium/Feature.** No sort UI. **Fix:** client sort pill-row (HpAsc/Desc, TimeRemaining, MaxHp, FairShare, Alphabetical); all fields on DTO except FairShare. Default HpAsc. **(Pairs with `raid-sort-nondeterministic`.)**
- **`items-consumable-differentiation` — Medium/Feature.** Material/Equipment show no button/explanation. **Fix:** ItemsScreen BuildCard — "Crafting ingredient" / "Equipped via Equipment screen" labels + dimmed disabled Use for inert types.
- **`request-debounce-hit-quest` — Medium/Feature.** Manual hits/quest-attempts can double-fire. **Fix:** 250ms debounce in RaidCombatView.DoHit + QuestScreen attempt; optionally raise `PlayerRequestsPerWindow` 180→240.
- **`bazaar-catalogue-endpoint` — Medium/Feature.** Bazaar shows owned-only; new players see empty Magics tab. **Root cause:** `GetMagicShopAsync` is BETA-PLACEHOLDER (HttpRotaApi.cs:421). **Fix:** backend `GET /api/magics/shop` (all defs + `AlreadyOwned`); client calls it.

### Infra
- **`response-compression-and-data-size` — Medium/Feature.** No response compression; unbounded un-partitioned `audit_log` (every auto-hit writes a row). **Fix:** `AddResponseCompression`(Brotli+Gzip); audit_log retention (partition by month / 90-day archive / `ix_audit_log_created_at`); consider suppressing per-hit success audits. _Open Q: Caddy already gzips (server-side redundant?); retention policy._

### Content / Phase-2
- **`crafting-system` — Low/Feature.** Materials exist, no recipes/service/UI. **Fix (Phase-2):** content/recipes.json + ICraftingService + CraftingController; client tab; no schema change (output → PlayerGear).

---

## Suggested fix order

**Tier 1 — Data-integrity / economy exploits / crashes:**
1. `node-depletion-per-difficulty` (CRITICAL — sigil exploit; migration + per-difficulty design).
2. `quest-boss-gem-on-every-hit` (CRITICAL — gem inflation; needs §2).
3. `boss-raid-kill-xp-inflated` (CRITICAL — XP explosion; content-only once cap decided).
4. `stat-alloc-validator-cap` + `stat-alloc-lsi-cap-wrong` (High — 1-line each; unblocks bulk buttons, resolves friend-404).
5. `auth-concurrent-refresh-session-wipe` + `auth-401-token-expiry-race` (High — single SemaphoreSlim client fix).
6. `int32-overflow-audit` (High — migrate gems/XP/gold/reward DTOs to long/bigint before more ledger data accrues).

**Tier 2 — High-visibility correctness:**
7. `login-hardcoded-dev-email` (1-line, embarrassing in beta).
8. `bug-tutorial-reappears-l22` (gate Dev Tools by role).
9. `health-drain-first-hit-only` + `guild-raid-stamina-wrong-pool-in-response`.
10. `bug-class-gate-only-on-relog`.
11. `raid-loot-no-all-claimed-expiry` (+ doc fix `raid-lifecycle-state-stale-enum-doc`).
12. `raid-sort-nondeterministic` (1-line OrderBy).
13. `atk-def-chip-stale-on-alloc`, `auth-no-login-redirect-on-401`, `tnl-curve-too-flat-early` (config), `pano-set-nerf` (content), `gem-parity-raid-vs-quest-boss` (content).

**Tier 3 — UX / content / features:**
14. `auth-webgl-device-id-fallback-same-key` (if WebGL is a beta target).
15. `raid-completed-tab-no-refresh-after-claim` + `raid-completed-tab-shows-all-history-forever`, `raid-tier-label-hardcoded-world`, `guild-raid-autohit-stop-wrong-label`.
16. `stat-alloc-ui-bulk-buttons` (after #4), `raid-share-fast-button`, `raid-list-sort-controls` (after #12), `items-consumable-differentiation`, `request-debounce-hit-quest`, `bazaar-catalogue-endpoint`.
17. `response-compression-and-data-size` (pre-scale).
18. `crafting-system` (Phase-2, defer).

**Dedupe notes:** the two auth tickets = one SemaphoreSlim change. `raid-sort-nondeterministic` (bug) underlies `raid-list-sort-controls` (feature). The two Completed-tab tickets are the server + client halves of one fix. `raid-lifecycle-state-stale-enum-doc` is a doc sub-task of the expiry ticket. `stat-alloc-friend-404-investigation` needs no work — symptom of the validator cap.
