# System 20 — Quest Node Depletion + Discernment Drops (Pano set)

*Status: SHIPPED 2026-06-05 — all 4 slices built (owner: "build with my defaults"). Decisions
resolved below. Traced against current quest code 2026-06-04.*

## Goal (owner — ticket T5)
1. Each quest **node starts at 100** and **depletes 5 per attempt** (→ 20 attempts to clear).
2. When a node hits **0 it clears and unlocks the next node**.
3. Once all **pre-boss nodes are cleared, the boss node becomes available**. The **boss node also
   starts at 100 but depletes 2.5 per attempt** (→ 40 attempts) to reflect its weight.
4. **Every attempt rewards EXP** (already true today — keep).
5. As **Discernment grows, attempts yield random drops** (modelled on Dawn's drop system),
   and the drop pool **includes the Pano Questing Legendary set**.

## Current model (traced — what exists today)
- **5 quests**, linear chain (`content/quests.json`): `q001` Battle → `q002` Battle →
  `q003` **Boss** (Iron Colossus) → `q004` Battle → `q005` **Boss** (Malachar). Boss nodes carry
  per-difficulty `sigils` + `sigilDropChance`.
- Quests are **stateless-repeatable**: progress is just `PlayerQuestProgress.CompletionCount`
  (node-level, difficulty-agnostic) + `PlayerQuestDifficultyProgress` (per difficulty, holds
  `FirstSigilDropped`). **No node "health"/progress-bar primitive exists** — net-new.
- Unlock today = prerequisite node's `CompletionCount >= 1` (one clear). Difficulty gate
  (Hard←Normal…) enforced separately at attempt time.
- `AttemptQuestAsync` (`QuestService.cs:139-287`): energy spent FIRST (zero side-effects on fail),
  then gold/XP/level-ups, node + difficulty progress, gems (idempotent), **loot table (dormant)**,
  sigil drop (boss). XP already granted every attempt.
- **Loot pipeline is built but dormant**: `ProcessQuestLootAsync` (`QuestService.cs:293-351`) handles
  guaranteed/chance/magic/unit/legion/gear drops, but every quest has `lootTableId: null` and
  `loot_tables.json` has **no quest-type tables** → never runs today.
- **Discernment is allocatable but applies to nothing on quests** (CLAUDE.md PHASE-2). Drop rolls are
  flat `_random.NextDouble() < chance`, no luck/rarity weighting.
- **No Pano set, no gear set-bonus concept.** `ConditionalBonus` (v0.2.5) is the only gear-synergy
  hook; set bonuses are PHASE-2.

## Design

### A. Node depletion / progress (replaces CompletionCount-as-unlock)
Add to **`PlayerQuestProgress`** (difficulty-agnostic, one row per player/node):
- `Progress` (double, default **100.0**) — remaining; decremented each attempt, floored at 0.
- `IsCleared` (bool, default false) — one-way latch set true when `Progress` reaches 0.
- New domain method `Deplete(double amount)` → `Progress = max(0, Progress - amount)`; sets
  `IsCleared` when it hits 0; bumps `UpdatedAt`. `CompletionCount`/`RecordCompletion()` stay (still
  counts total runs for gem idempotency + display).

**Depletion per attempt** (server-authoritative, applied inside the attempt after energy spend):
- Battle node: **−5.0**
- Boss node (`isBossNode`): **−2.5**
- Config-driven (see `QuestConfig` below), not hardcoded magic numbers.

**Replayability:** once `IsCleared`, the node stays **playable** (XP/drops/sigils keep flowing) but
`Progress` stays 0 and the clear latch never flips back. The bar is a one-time gate, not a cooldown.

### B. Unlock chain
- Node prereq becomes: prerequisite node's **`IsCleared == true`** (was `CompletionCount >= 1`).
  → You must fully deplete a node (20 attempts) before the next unlocks.
- Boss node unlocks when **all of its pre-boss prerequisite nodes are cleared** — in the current
  linear chain that's exactly "prereq node `IsCleared`", so the same rule covers it. (If a future
  zone fans out to multiple pre-boss nodes feeding one boss, model boss prereqs as a *list*; flagged
  as a decision since today's chain is linear and a single prereq suffices.)
- Boss "summonable": defeating/clearing the boss node drops **sigils** (already wired) → sigils summon
  the raid (System 9/19). No new summon mechanic — T5 only gates the boss node behind cleared
  pre-boss nodes and adds its depletion.

`GetAvailableQuestsAsync` returns `IsUnlocked` from the new `IsCleared`-prereq rule and exposes the
node's `Progress`/`IsCleared` so the client can render the bar.

### C. Discernment-driven drops (activate the dormant pipeline)
- Assign each quest a **quest-type loot table** (`lootTableId`) and add those tables to
  `loot_tables.json` (a `type:"Quest"` family; extend `LootTableProvider` validation to cover them).
- Discernment modifies drop odds. **Proposed formula** (tunable in `QuestConfig`):
  `effectiveChance = baseChance × (1 + DiscernmentInvestment × discernmentDropK)`, clamped to
  `[0, maxChance]`. So a player with 0 Discernment still gets the floor odds; investment raises them.
  - The **Pano set pieces** sit in each quest's `ChanceDrops`/`GearDrops` at a low `baseChance`;
    Discernment is what makes them realistically farmable — matching "as Discernment grows, attempts
    yield drops."
  - (Optional richer model, flagged: a rarity-tier roll where Discernment shifts weight toward higher
    rarities rather than a flat multiplier. Start with the multiplier; upgrade later.)
- `ProcessQuestLootAsync` gains the Discernment multiplier on chance rolls (guaranteed drops
  unaffected). Pass the player's `DiscernmentInvestment` in.

### D. Pano Questing Legendary set (content)
- Add Pano gear pieces to `content/gear.json` (e.g. one per `EquipmentSlot`, or a subset — decision).
  Rarity = our ceiling is **Orange** (no "Legendary" rarity exists; "Legendary set" → Orange pieces).
- Add Pano pieces to the quest loot tables' drop pools.
- **Set bonus is out of scope** (PHASE-2 gear set-bonuses). The pieces are individually strong gear
  now; the named set-bonus lands when the set system ships. Flag if a stop-gap `ConditionalBonus`
  per piece is wanted in the meantime.

### DTOs
- `QuestAvailabilityResponse` gains `double Progress`, `bool IsCleared` (client renders the depletion
  bar + unlock state).
- `QuestResultResponse` gains `double NodeProgress`, `bool NodeCleared`, `bool NodeJustCleared`
  (drops already flow via `ItemsGranted`). After change, run `/audit-dtos` and mirror to client.

### Migration
`AddQuestNodeProgress` — `progress double NOT NULL DEFAULT 100`, `is_cleared bool NOT NULL DEFAULT
false` on `player_quest_progress`. **Back-compat:** existing rows default to Progress=100/uncleared,
i.e. everyone "re-grinds" the bar. If that's undesirable for current testers, seed `IsCleared=true`/
`Progress=0` for nodes with `CompletionCount > 0` in the migration (decision).

## Decisions (RESOLVED — owner chose "build with my defaults", 2026-06-05)
1. **Depletion per difficulty?** → **Flat** −5 / −2.5 regardless of difficulty (as written). Tunable
   in `QuestConfig` if revisited.
2. **Cleared-node replay:** → **Yes**, nodes stay farmable after clear (one-way `IsCleared` latch).
3. **Discernment constants:** → `DiscernmentDropMultiplier = 0.03`/pt, `MaxDropChance = 0.95` (cap
   never lowers an already-high base). Pano base rates 0.02→0.10 by difficulty. All in `QuestConfig`/
   loot tables — tune freely.
4. **Pano set scope:** → **All 8 slots**, **Orange** rarity, individually strong stats. **No set
   bonus** (stays PHASE-2); revisit when the set-bonus system ships.
5. **Migration back-compat:** → **Auto-clear** nodes already completed (`completion_count > 0` →
   `is_cleared = true, progress = 0`) so current testers keep their unlocks.
6. **Content depth:** → Deferred. Still only 5 quests; depletion lengthens traversal. Expanding the
   questline remains open (prior handoff item "2 raids / 5 quests is thin").

## Slices
1. **Node depletion core** ✅ (`41eb89f`): `PlayerQuestProgress.Progress/IsCleared/Deplete`,
   `QuestConfig`, attempt applies depletion, unlock rule → `IsCleared`-prereq; migration
   `AddQuestNodeProgress` (+ auto-clear back-compat); DTO fields; +5 unit tests.
2. **Discernment drops** ✅ (`6b93a12`): quest loot tables wired (`lootTableId` + 5 `type:"Quest"`
   tables), Discernment multiplier in `ProcessQuestLootAsync` (cap never lowers a high base); +2 tests.
3. **Pano set content** ✅ (`6b93a12`): 8 Orange Pano pieces in `gear.json`, distributed across the 5
   quest loot tables at difficulty-scaled rates. No set bonus (PHASE-2).
4. **Client** ✅ (ROTA.Client6 `master`): depletion bar + cleared/locked state on quest cards,
   "node cleared" callout, drops via `ItemsGranted`; DTO mirror; mock quests made stateful so the bar
   moves in mock mode. Headless-compiled 0 `error CS`.

## Non-goals (defer)
Real gear set-bonus system, rarity-tier weighted drop model (start with the multiplier), node
cooldowns/energy refresh tuning, new zones/quests beyond the existing 5.
