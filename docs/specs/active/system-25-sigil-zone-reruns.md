# System 25 — Sigil-as-boss-reward, Zone Re-lock & Zone-Rerun Achievements

*Status: active · Branch: `feat/sigil-rework-zone-reruns` · Owner-locked 2026-06-16*

## Owner outline (verbatim intent)
1. Sigils are the **final-clear reward of the zone boss**, never an on-hit reward.
2. **First clear per difficulty = 100%** sigil drop; **any zone rerun = flat 15%**.
3. A zone must be **fully re-run** (every non-boss node re-cleared this cycle) before its boss is
   attemptable again — *every* reset, not just the first.
4. Each zone reset increments a per-`(chapter, zone)` **rerun counter**.
5. New **zone-rerun achievements**, graded by the 6 rarity colours (Grey→Orange); **Orange at 500
   reruns**, with incremental thresholds for the colours below.

## Locked decisions (2026-06-16)
- **Rerun sigil chance:** flat **15%** from new `QuestConfig.SigilRerunDropChance = 0.15`. Removes the
  System-22 Discernment "sigil-find" scaling from the sigil step (left in code, marked inert-for-sigils;
  it still serves nothing else, documented as drift). First-clear stays a guaranteed 100%.
- **Rerun counting:** **per-zone cycle** — +1 each time a zone boss is cleared (every `ZoneReset==true`,
  including the first clear → cycle #1). Idempotent per cycle via the boss node's `CompletionCount`.
- **Reward:** **Achievement Points only** (rides the existing append-only award ledger; no new grant path).
- **Authoring:** **templated** — a single 6-tier rarity ladder in `AchievementConfig`, expanded per
  distinct `(chapter, zone)` at boot by `AchievementDefinitionProvider` (injects `IQuestDefinitionProvider`,
  mirroring `LootTableProvider`). Deterministic ids `ach_zonererun_c{ch}z{zone}_{rarity}`; 6-tier NextId
  chain synthesized; scales to the planned 24 chapters with zero new rows.
- **Curve:** Grey=10, White=25, Green=50, Blue=100, Purple=250, **Orange=500**; AP 5/10/20/40/75/150
  (config-tunable). Identical for every zone.
- **Boss re-lock:** the zone-boss gate + boss-greying switch from the permanent `HasEverCleared` latch to
  the **current-cycle `IsCleared`** state. The forward prerequisite chain stays on `HasEverCleared` so a
  reset never re-locks the *next* zone.
- **Sigil coverage:** wire **all 25 zone bosses** (23 currently have `sigils:null`). Each needs the summon
  target raid too → generate **23 boss-raids + 92 sigil items** (formula-scaled HP/rewards, tunable).
- **Mock fidelity:** the client mock currently drops sigils on **raid kills** + seeds them in inventory
  (inverse of canon) and drops none from quest bosses — this is the "sigils on hit" the owner saw. Fix the
  mock so sigils come from quest bosses only (100% first / 15% rerun, stateful), and the raid path stops
  granting them.

## Rarity ↔ difficulty (shipped convention — NOTE: differs from CLAUDE.md)
Shipped `items.json` uses **Normal→Green, Hard→Blue, Legendary→Purple, Nightmare→Orange**. CLAUDE.md's
Sigil-System section says White/Green/Blue/Purple — that's stale. Match the shipped data; fix the doc.

## Implementation map
**Backend mechanics** (`QuestService.cs`):
- Sigil step (~410-442): first-clear 100% (unchanged) / else `_random < QuestConfig.SigilRerunDropChance`;
  drop the `SigilDropChance * DiscernmentSigilFindMultiplier` line. `SigilDropChance` JSON field becomes
  vestigial (presence of a `Sigils` map = enabled).
- Boss gate (~267-278): `!sibProg.HasEverCleared` → `!sibProg.IsCleared`.
- Boss greying (~194-200): build a `clearedThisCycleIds` set (IsCleared) next to `unlockedQuestIds`; boss
  `IsUnlocked` = all non-boss siblings in `clearedThisCycleIds`.
- Zone-rerun hook (14c, best-effort): on `zoneReset`, `RecordZoneRerunAsync(playerId, chapter, zoneIndex,
  "zonererun:{ch}:{zone}:{bossCompletionCount}")` before the existing `EvaluateCompletionsAsync`.

**Achievements:** `AchievementMetric.ZoneReruns=6`, `AchievementCategory.ZoneMastery=5`;
`AchievementDefinition += int? Chapter, int? ZoneIndex`; `AchievementConfig += ZoneRerunLadder` (+`ZoneRerunTier`);
provider synthesizes ladders + `GetZoneRerunTiers(ch,zone)`; validation requires Chapter/ZoneIndex iff
Metric==ZoneReruns; `IAchievementService.RecordZoneRerunAsync`; DI injects quests + config (ctor params
optional so existing 1-arg test calls still build).

**Content:** `QuestConfig.SigilRerunDropChance`; 23 raids → `raids.json`; 92 sigils → `items.json`; 23 boss
`sigils` maps → `quests.json`. New raids: tier Standard, `lootTableId:""` (gold/XP/gem tier rewards, item
loot a follow-up), HP/reward formula-scaled by chapter+zone (tunable; flagged).

**Client (ROTA.Client6):** `MockRotaApi` — quest-boss sigil drop (100% first / 15% rerun, stateful latch);
remove raid-kill sigil grant + seeded sigil inventory; mirror the IsCleared boss re-lock; stateful
zone-rerun achievements.

**Tests:** rewrite the two Discernment-sigil tests; add flat-15% (seeded RNG) first/rerun tests; add a
re-lock-after-reset test; add provider-templating + `RecordZoneRerunAsync` scoping/idempotency tests.

## Known follow-ups
- Item loot tables for the 23 new boss-raids (now gold/XP/gem only).
- `EvaluateCompletionsAsync`/overview iterate all defs (150 now → ~1000 at 24 chapters) — scope/paginate
  before the achievement browse screen.
- Tune the generated raid HP/reward curve + the rerun-ladder thresholds/points after playtest.
- Excise vs keep the inert Discernment sigil-find lane (System 22 docs still describe it).
