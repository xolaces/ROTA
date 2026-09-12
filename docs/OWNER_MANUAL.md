# ROTA — Owner's Control Manual

**What you can change yourself, where it lives, and what it does.**

Everything in this document is a JSON or config edit. None of it needs C#, a rebuild, or a database
migration. Section 9 lists the things that *do* need code, so you know where the line is.

Verified against the repository on 2026-09-07. Where a number is hardcoded rather than configurable,
this manual says so and gives the file and line.

---

## 1. The whole loop, in 30 seconds

```
1. Edit a file under  src/ROTA.Api/content/   (content)  or
                      src/ROTA.Api/appsettings.json      (tuning)
2. Restart the API.
3. Done.
```

**No rebuild is needed.** `dotnet run --project src/ROTA.Api` resolves content from the *project*
directory, so it reads `src/ROTA.Api/content/*.json` directly — not the copy in `bin/`. This was
verified by moving `bin/Debug/net10.0/content` aside and confirming the server still boots healthy.

```bash
# stop, then start
taskkill /IM ROTA.Api.exe /F
dotnet run --project src/ROTA.Api
```

> **Always stop the API before `dotnet build`.** A running instance holds `ROTA.Application.dll`, so
> the build fails with MSB3027 — and `dotnet test --no-build` then passes against the *stale*
> binaries, which looks like a clean run and is not one.

### Changes that need more than a restart

| Change | Also needs |
|---|---|
| Anything in `content/*.json` | nothing — restart only |
| Anything in `appsettings.json` | nothing — restart only |
| A **new field** on a content type | C# (see §9) |
| Anything touching the database schema | a migration — and **you** run it, never the agent |

---

## 2. Where everything lives

```
src/ROTA.Api/content/
  raids.json           28  boss definitions, HP, rewards, tags
  guild_raids.json      3  guild-only bosses
  gauntlet_raids.json   6  Gauntlet ladder stages
  quests.json         139  the campaign: chapters, zones, nodes, energy, XP
  items.json          176  materials, sigils, consumables, stat bags
  gear.json           111  equipment, 13 sets
  magics.json          41  the magic catalogue
  units.json           37  generals and troops
  legions.json         10  legion frames, slot constraints, tag affinities
  recipes.json         23  crafting
  loot_tables.json     82  what drops, from what, at what rate
  achievements.json     7  achievement definitions
  subjects.json            bug/report subject lists
  legal/terms.md           terms text (PLACEHOLDER — replace before public beta)
  legal/privacy.md         privacy text (PLACEHOLDER)

src/ROTA.Api/appsettings.json    every tuning knob (§8)
```

**Rule of thumb:** *content* (a thing that exists in the world) is JSON. *Tuning* (a number that
governs how things behave) is `appsettings.json`. A few numbers are neither — they are hardcoded in
C#, and §9 lists them.

---

## 3. Your safety net

**The server refuses to start on bad content, and tells you exactly what is wrong.** Content is
loaded and validated once at boot by eager singletons, so a typo is a startup crash with a clear
message — never a silent problem that surfaces as a weird bug three days into a playtest.

Real examples of what it will say:

```
items.json: duplicate id 'mat_iron_shard'.
items.json: 'mat_road_flint' upgradesTo 'mat_causeway_ash' which does not exist.
items.json: 'mat_x' (Grey) upgradesTo 'mat_y' (Grey) must be strictly higher rarity.
items.json: consumable 'potion_x' must set restoreResourceType.
raids.json: raid 'raid_x' has tag 'Goblins', which is not a RaidTag. Valid: ...
gear.json not found — required for startup.
```

So the workflow when you are unsure is simply: **edit, restart, read the message.** If it boots, the
content is structurally valid.

There is also a standalone checker you can run without starting the server:

```bash
python tools/validate_content_pack.py
```

**What validation does NOT catch:** whether your numbers are *good*. It will happily accept a raid
with 3 HP or a 100% drop rate on an Orange item. Balance is on you.

---

## 4. Raids

### 4.1 Changing a raid's health

Open `src/ROTA.Api/content/raids.json` and find the raid. Two fields matter:

```json
{
  "id": "raid_c1z1b",
  "name": "Guardian of Ashen Causeway",
  "baseHp": 4000,            ← shared raid pool (Small/Medium/Large/Titanic)
  "personalBaseHp": 1000,    ← solo raid pool
  ...
}
```

**`baseHp` is the Normal-difficulty pool.** The server multiplies it at summon time:

| Difficulty | Multiplier | 4,000 becomes |
|---|---|---|
| Normal | ×1.0 | 4,000 |
| Hard | ×1.4 | 5,600 |
| Legendary | ×2.0 | 8,000 |
| Nightmare | ×3.6 | 14,400 |

So you set **one** number and get all four tiers. Never write per-difficulty HP into the JSON.

> Those four multipliers are **hardcoded** in `src/ROTA.Application/Services/RaidService.cs:15–22`,
> not config. Changing them is a code edit (§9).

**Current spread**, for calibration — the campaign's raid ladder runs roughly 4,000 at chapter 1
to 4.67 million at chapter 7:

| Raid | baseHp |
|---|---|
| `raid_c1z1b` (chapter 1) | 4,000 |
| `raid_lastwatch_relay` | 2,650,000 |
| `raid_lastwatch_vault` | 3,520,000 |
| `raid_lastwatch_lamp` (chapter 7) | 4,670,000 |

`raid_ironcolossus` and `raid_malachar` have `baseHp: 0` deliberately — they are **World** tier and
end on their timer, not on damage.

**If you retune HP, you must also retune the loot table.** Threshold rewards are expressed as a
*fraction of the raid's pool*, so doubling HP silently halves how far players get up the ladder. See
§5.3.

### 4.2 Every field on a raid

```json
{
  "id": "raid_c1z1b",              unique; also the loot-table and art key convention
  "name": "Guardian of Ashen Causeway",
  "tier": "Standard",              Standard | World | Guild | Event
  "baseHp": 4000,                  Normal-difficulty shared pool
  "personalBaseHp": 1000,          Normal-difficulty solo pool
  "timerHours": 48,                how long before it expires
  "staminaCostPerHit": 1,          multiplied by hit size (1 / 5 / 20)
  "goldPerStamina": 1,             gold yield per stamina spent
  "lootTableId": "lt_raid_c1z1b",  "" = tier rewards only (gold/XP/gems, no items)
  "baseGoldReward": 600,           on kill, before tier + difficulty multipliers
  "baseExperienceReward": 350,
  "baseGemReward": 2,
  "hasOnHitDrops": false,          World/Event ONLY — Standard must stay false
  "artKey": "raid_c1z1b",
  "grade": "Common",               Common | Elite | Deadly | Mythic (display/curve band)
  "tags": ["Construct"]            what legions counter it — see §4.4
}
```

### 4.3 Adding a new raid

Four steps, all JSON:

**1 — add the raid** to `raids.json`. Copy the block above, change `id`, `name`, `baseHp`,
`baseGoldReward`, `tags`.

**2 — give it a loot table** in `loot_tables.json`, or set `"lootTableId": ""` to ship it with tier
rewards only (gold, XP, gems — no items). Shipping with `""` is completely fine and is what the 23
generated boss-raids do.

**3 — make it summonable.** A raid nobody can start does not exist. Either:
- **a sigil** — add an item to `items.json` with `"type": "Sigil"`, `"summonRaidId": "<your raid id>"`,
  `"summonDifficulty": "Normal"`, `"summonSize": "Small"`; then drop that sigil from a quest boss
  (§6.4), or
- **a quest boss reward** — put it in the boss node's `sigils` map (§6.2).

**4 — restart and check the log.** If the id is duplicated or a tag is misspelled, it will say so.

### 4.4 Raid tags and legion counters

A raid's `tags` say what *kind* of thing it is. A legion with a matching `tagAffinities` entry hits
it harder.

Valid tags — these are the only accepted strings:

```
None  Goblin  Beast  Undead  Shadow  Construct  Demon  Horror  Legion  Dragon
```

`None` is meaningful: an untagged raid is one **no** legion counters. The Ashen Throne and Cinder
Crown are untagged on purpose — set-pieces answer to nobody.

In `legions.json`, affinity is a percentage:

```json
"tagAffinities": { "Goblin": 12, "Beast": 8 }
```

That legion deals **+12%** legion power against Goblin raids and +8% against Beast raids.

> **Highest-only, never summed.** If a raid is tagged `["Goblin","Beast"]` and your legion has both,
> it gets 12%, not 20%. This is deliberate and matches the Gauntlet trophy rule — summing would let
> a legion with three small bonuses beat a true specialist at its own specialty. Don't "fix" it.

**Tag reagents follow their tag automatically.** An item in `items.json` carrying `"tags": ["Goblin"]`
is wired into every Goblin-tagged raid's loot by `tools/content/wire_wave2_drops.py`. If you retag a
raid and re-run that script, its drops move with it.

---

## 5. Drops and loot tables

`loot_tables.json` is the single biggest lever you have on how the game *feels*.

### 5.1 The shape

Every table has an `id`, a `type` (`Quest` or `Raid`), and a `difficulties` block with one entry per
difficulty — so the same table gives different results on Normal vs Nightmare.

```json
{
  "id": "lt_quest_q001",
  "type": "Quest",
  "difficulties": {
    "Normal": {
      "guaranteedDrops": [ { "itemId": "mat_iron_shard", "quantity": 1, "chance": 1.0 } ],
      "chanceDrops":     [ { "itemId": "mat_bog_cotton", "quantity": 2, "chance": 0.15 } ],
      "gearDrops":       [ { "gearDefinitionId": "gear_pano_helm", "quantity": 1,
                             "chance": 0.005, "rareScaling": true } ]
    },
    "Hard": { ... }, "Legendary": { ... }, "Nightmare": { ... }
  }
}
```

### 5.2 The four drop kinds

| Kind | Rolls? | Discernment-scaled? | Use for |
|---|---|---|---|
| `guaranteedDrops` | no — always granted | no | the reliable trickle |
| `chanceDrops` | yes | only if `rareScaling: true` | the normal case |
| `gearDrops` | yes (quest side) | only if `rareScaling: true` | equipment |
| `thresholdRewards` | raid only, cumulative | n/a | raid damage ladders |

**`chance` is a probability from 0.0 to 1.0.** `0.005` is 0.5%. `0.0012` is 0.12%.

**`rareScaling: true` makes a drop respond to Discernment.** Without it, a rare item stays rare no
matter how much a player invests — which is usually wrong for a chase item and right for a common
reagent. If you add a chase drop, set it.

### 5.3 Raid threshold rewards — read this before retuning HP

Raid loot ladders are **cumulative**. Each rung is keyed on the damage a player dealt — an
absolute number, written as a fraction of the raid's Normal HP pool when the table was generated:

```json
"thresholdRewards": [
  { "damageThreshold": 100,  "contributionPercent": 0.0, "unassignedStatPoints": 1, "itemDrops": [...] },
  { "damageThreshold": 600,  "contributionPercent": 0.0, "unassignedStatPoints": 4, "itemDrops": [...] },
  { "damageThreshold": 1600, "contributionPercent": 0.0, "unassignedStatPoints": 4, "itemDrops": [...],
    "gearDrops": [ { "gearDefinitionId": "gear_weir_courser", "chance": 1.0 } ] }
]
```

A player who dealt 1,600 collects **all three rungs**, not just the last. A rung with
`damageThreshold` 0 is keyed on `contributionPercent` — the share of the total — instead; the
shipped tables all use damage, because a share means nothing until the raid is over and a ladder
the player can see mid-fight is worth more.

> **Until 2026-09-11 campaign raids paid every rung to everyone.** The service only read the
> damage key on World raids and fell back to the share key elsewhere, and a share of zero is one
> everybody clears. Fixed in `RaidService`; the ladder now pays what the catalogue shows.

> **This is why HP and loot are coupled.** If you double a raid's `baseHp` without touching its
> table, every rung is twice as far up the fight. `tools/content/retune_raid_health.py` moves both.

**Raid gear is guaranteed.** The raid path grants threshold gear *unconditionally and
cumulatively*, ignoring `chance`. So on the raid side, put gear on the **last rung only, at
`chance: 1.0`** — otherwise a single clear hands out four copies. Quest-side gear honours its
`chance` normally. Each campaign raid carries one mount this way; everything else a raid gives up
is a part (§5.5).

### 5.4 Making something rarer or more common

Find the entry, change `chance`. That is the whole operation.

Rough calibration from the shipped tables:

| Band | `chance` | Reads as |
|---|---|---|
| Reagent, common | 0.15 – 0.30 | you'll see it most sessions |
| Set gear, quests (every piece in a pool, equal) | 0.020 ch.1–2 · 0.0167 ch.3–4 · 0.0125 ch.5–6 · 0.010 ch.7 | 1-in-50 and rarer per click — the grind, by design |
| Reforge scrap, raid top rung | 0.12 → 0.30 by difficulty | a set of parts is many clears |
| Reforge tack (the mount), raid top rung | 0.04 → 0.10 by difficulty | the rarest thing a raid gives up |
| Deep relic | 0.0002 – 0.0010 | may never see it |

Quest gear rates are **generated**, not hand-set: `tools/content/reforge_sets.py` writes every set
piece in a zone pool at the chapter's rate (`QUEST_GEAR_RATE`), ×1.15 / ×1.3 / ×1.5 by difficulty,
×2 on the boss node. Change the number there and re-run; a hand edit is overwritten on the next run.

### 5.5 Reforging — what raids are for

Raids do not drop gear (one mount aside). They drop **parts** for a better version of the gear,
and crafting makes it:

- Every set piece has a **reforged twin** in `gear.json` — `gear_weir_kettle_helm_reforged` —
  same slot, same rarity, same art, bonuses ×1.5, in a set of its own (`set_weir_reforged`).
- Every set has two materials in `items.json`: a **Scrap** (`mat_weir_scrap`) for the seven body
  pieces and a **Tack** (`mat_weir_tack`) for the mount.
- Every piece has a **Reforge recipe** in `recipes.json` (the client's Reforge tab): the base piece
  + 3/4/5/6 scraps by rarity (or 3 tack for the mount) + gold. The base piece is consumed. A piece
  that is *worn* cannot be consumed (D-020) — unequip it, reforge, re-equip.
- Every raid drops the parts of the sets whose pieces fall in its chapter, on **every rung** of its
  ladder at a per-rung chance that compounds to the top-of-ladder targets below. The more of the
  fight you carried, the more rungs you roll. Guild raids have tables now (they had none); the
  World raids carry Pano's vanguard parts.

| Top of the ladder | Normal | Hard | Legendary | Nightmare |
|---|---|---|---|---|
| Scrap, at least one | 12% | 18% | 24% | 30% |
| Tack, at least one | 4% | 6% | 8% | 10% |

All of it comes from one file: **`tools/content/reforge_sets.py`**. The set → raid map, the
targets, the multiplier, the scrap counts and the gold are constants at the top; re-running the
script rewrites everything it owns and leaves everything else alone. `ReforgeContentTests` pins
the invariants (every piece has a twin and a recipe; every part drops somewhere; tack is always
rarer than scrap; every quest pool is one rate).

---

## 6. Quests, energy and XP

### 6.1 The hierarchy

`quests.json` is **Chapter → Zone → Node**. 7 chapters, 26 zones, 139 nodes.

```json
{
  "id": "q001",
  "name": "Ruins of the Old Guard",
  "chapter": 1,
  "zoneIndex": 0,               0-based within the chapter
  "zoneName": "Old Guard Ruins",
  "nodeIndex": 0,               0-based within the zone; the boss is the LAST index
  "nodeType": "Battle",         Battle | Boss
  "baseEnergyCost": 5,
  "baseXpRatio": 1.2,
  "lootTableId": "lt_quest_q001",
  "sigilDropChance": 0,         vestigial — see §6.4
  "sigils": null,               the sigil map; its presence is what enables sigil drops
  "goldReward": 100,
  "experienceReward": 50,       the BASE that the ratio multiplies
  "gemReward": 0,
  "prerequisiteQuestId": null   the ordering chain
}
```

### 6.2 Gating — how the chain is enforced

Progression is a single ordered chain expressed through `prerequisiteQuestId`:

- node N requires node N−1
- a zone's first node requires the **previous zone's boss**
- a chapter's first node requires the **previous chapter's final boss**

On top of that there is a **zone-boss gate**: a zone's boss cannot be attempted until every
non-boss node in that zone has been cleared in the current cycle. Clearing the boss then **resets
that zone** to fresh, and the boss re-locks until the zone is run again.

> Two different latches do two different jobs. `IsCleared` is the current cycle (resettable, controls
> whether you can attempt). `HasEverCleared` is permanent (controls unlocks, so a reset never
> re-locks progress you earned). Don't conflate them.

### 6.3 Changing energy cost and XP

The value in the JSON is a **base**. The server scales it per chapter.

**Energy** = `baseEnergyCost` × `QuestConfig.ChapterScaling[chapter].EnergyCostMultiplier`,
with a per-zone ramp of `EnergyZoneRampPerZone` (0.04 = +4% per zone into the chapter).

**XP** = `experienceReward` × zone ratio × `ChapterScaling[chapter].XpMultiplier` × difficulty
multiplier, where the zone ratio is `XpZoneRatioBase (1.2) + zoneIndex × XpZoneRatioPerZone (0.05)`,
and a **boss always uses `XpBossRatio` (2.0)** regardless of zone.

So there are two places to change XP, and they mean different things:

| Want | Change |
|---|---|
| this one node gives more XP | `experienceReward` on that node |
| this whole chapter gives more XP | `QuestConfig.ChapterScaling.<n>.XpMultiplier` |
| deeper zones give proportionally more | `QuestConfig.XpZoneRatioPerZone` |
| bosses give more relative to battles | `QuestConfig.XpBossRatio` |
| the whole game levels faster | `LevelingConfig.XpBaseMultiplier` (§8) |

**XP is not Hoard-scaled.** Only gold and drops respond to the Hoard mastery.

### 6.4 Sigils — how raids become reachable

**Sigils drop from zone bosses only, on a final clear.** A node drops sigils only if it carries a
`sigils` map *and* is the last node in its zone.

```json
"sigils": {
  "Normal":    "sigil_c1z1b_normal",
  "Hard":      "sigil_c1z1b_hard",
  "Legendary": "sigil_c1z1b_legendary",
  "Nightmare": "sigil_c1z1b_nightmare"
}
```

- **First clear per difficulty:** guaranteed, 100%.
- **Every rerun:** flat `QuestConfig.SigilRerunDropChance` — **not** Discernment-scaled.

> `sigilDropChance` in the JSON is **vestigial** and ignored. The presence of the `sigils` map is
> what enables the drop. Don't spend time tuning it.

**`SigilRerunDropChance` is the single most consequential number in the game right now.** Raids are
gated on sigils, and at chapter 6 a sigil costs somewhere between 34 and 70 days of banked energy.
There are 37 raids. If testers are not seeing raids, this is the number — not the raid content.

### 6.5 Adding a quest node

1. Add the object to `quests.json` with the right `chapter` / `zoneIndex` / `nodeIndex`.
2. Set `prerequisiteQuestId` to the id of the node before it.
3. If you inserted it mid-zone, **fix the next node's `prerequisiteQuestId`** to point at yours, or
   the chain skips it.
4. If it is the new last node in the zone, it becomes the boss for gating purposes — set
   `"nodeType": "Boss"` and move the old boss's `sigils` map if appropriate.
5. `lootTableId: null` is fine — the node still gives gold and XP.

---

## 7. Items, gear, magic, units, legions, recipes

### 7.1 The rarity ladder — everywhere, always

```
Grey → White → Green → Blue → Purple → Orange
```

**Orange is the permanent ceiling. Never add above it.** Anything with an `upgradesTo` must point at
a *strictly higher* rarity, and the server refuses to start otherwise. This guard has caught three
real mistakes.

### 7.2 Items — `items.json`

```json
{
  "id": "mat_iron_shard",
  "name": "Iron Shard",
  "description": "...",              player-facing; write it in the world's voice
  "rarity": "Grey",
  "type": "Material",                Material | Sigil | Consumable | StatBag | Equipment
  "artKey": "mat_iron_shard",
  "statPointsOnUse": 0,              StatBag: skill points granted
  "isCraftingIngredient": true,
  "summonRaidId": null,              Sigil: which raid it summons
  "summonDifficulty": null,          Sigil: Normal | Hard | Legendary | Nightmare
  "summonSize": null,                Sigil: Personal | Small | Medium | Large | Titanic
  "upgradesTo": "mat_arcane_dust",   the Discernment rarity-upgrade target; null = none
  "tags": []                         raid tags — makes it a tag reagent (§4.4)
}
```

**Consumables must set `restoreResourceType`** (`Energy` / `Stamina` / `Health`) and either
`restoreAmount` or `restoreToMax: true`. The server enforces this at boot.

### 7.3 Gear — `gear.json`

```json
{
  "id": "gear_conscript_helm",
  "rarity": "Grey",
  "slot": "Head",                Head | Neck | Torso | Gloves | Boots | Ring1 | Ring2 | Mount
  "bonusAttack": 0,
  "bonusDefense": 1,
  "procChance": null,            0.0–1.0, or null for no proc
  "procPercent": null,
  "iconPath": "icons/gear/conscript_helm.png",
  "setId": "set_conscript"       groups pieces into a set
}
```

**`setId` is currently descriptive only.** Set *bonuses* are not implemented — that is the largest
piece of already-paid-for design sitting unused, and it is the headline of the next planned update.
Adding a `setId` today groups the pieces and does nothing mechanical yet.

**Adding a set:** give 8 pieces the same `setId`, one per slot, then wire them into loot tables
(§5). `tools/content/add_gear_sets.py` shows the pattern.

### 7.4 Magic — `magics.json`

```json
{
  "id": "magic_whetstone",
  "rarity": "White",
  "category": "Damage",          Damage | Crit | Gold | Leveling | Utility
  "effectType": "DamageProc",
  "procChance": 1.0,             1.0 = always fires
  "procAmount": 0.03,            +3%
  "conditions": [],
  "stacks": true,
  "gemPrice": 5                  0 = not purchasable
}
```

There is a global cap: `MagicConfig.MaxAggregateProcBonus` (5.0). Individual magics cannot push a
player past it, so adding more magic does not break the ceiling.

### 7.5 Units and legions

`units.json` — generals and troops:

```json
{
  "unitType": "General",         General | Troop
  "baseAttack": 80, "baseDefense": 60,
  "race": "Human",               Human Elf Undead Oroc Dwarf Beast Construct Demon
  "role": "Tank",                Tank Melee Ranged Healer Special
  "attribute": "Strength",
  "ability": { "procChance": 0.08, "procAmount": 0.4, "conditions": [] },
  "legionBonus": 5
}
```

`legions.json` — the frame those units slot into. `generalSlots` / `troopSlots` carry constraints
(`constraintType` of `None`, or a race/role restriction), `powerBonus` is a flat add, and
`tagAffinities` is the counter system from §4.4.

Contribution to legion power uses `LegionConfig.UnitCoefficients` — a General is `Atk × 2.0 +
Def × 0.4`, a Troop `Atk × 1.44 + Def × 0.36`.

### 7.6 Recipes — `recipes.json`

```json
{
  "id": "craft_ironward_ii",
  "outputKind": "Unit",          Unit | Item | Gear
  "outputId": "gen_ironward_ii",
  "outputQuantity": 1,
  "ingredients": [
    { "kind": "Unit", "id": "gen_ironward", "quantity": 1 },
    { "kind": "Item", "id": "mat_oathsteel", "quantity": 2 }
  ],
  "goldCost": 15000,
  "category": "General"          General | Reforge | Events | Guild | Special — Reforge is generated (§5.5)
}
```

Boot validation rejects: a recipe consuming its own output, a duplicated ingredient, a unit recipe
that does not take and make exactly one, and any id that does not resolve.

**The three reagent axes**, which is why crafting asks for what it asks for:

| Axis | Earned by | Costs |
|---|---|---|
| **Road** | quest drops | Energy |
| **Field** | raid thresholds | Stamina |
| **Tag** | tagged raids | fighting a specific *kind* |

Capstone recipes require one of each. That is the structural answer to a single-pool build: no
amount of energy buys a Leviathan Tooth — you have to go and fight a Horror.

---

## 8. `appsettings.json` — every knob

Path: `src/ROTA.Api/appsettings.json`. Restart to apply. **Never put secrets here** — those go in
user-secrets or environment variables.

### 8.1 The ones you will actually reach for

| Key | Now | Does |
|---|---|---|
| `QuestConfig.SigilRerunDropChance` | 0.15 | **Raid reachability.** See §6.4. |
| `LevelingConfig.XpBaseMultiplier` | 30.0 | Global XP-to-level curve. Higher = slower. |
| `LevelingConfig.XpExponent` | 0.8 | How much steeper each level gets. |
| `QuestConfig.ChapterScaling.<n>` | per chapter | Energy cost and XP per chapter. |
| `MarketConfig.Enabled` | **false** | Turns the player market on. |
| `RateLimitConfig.PlayerRequestsPerWindow` | 180 | Per-player requests per 60s. |
| `RateLimitConfig.AuthRequestsPerWindow` | 10 | Per-IP on `/api/auth/**`. |
| `GuildConfig.CreationGoldCost` | 25000 | Cost to found a guild. |
| `GuildConfig.MinCreationLevel` | 20 | Level gate to found one. |
| `Legal.CurrentTermsVersion` | 1 | **Bump this and every player must re-accept.** |
| `Developer.Usernames` | `[]` | Grants the Developer flag + Dev guild. |

### 8.2 Knobs that were invisible until 2026-09-07

Eleven tunables existed **only as C# defaults** and did not appear in `appsettings.json` at all —
including `SigilRerunDropChance`, the single most consequential number in the game. They have been
written into `appsettings.json` at their existing values, so behaviour is unchanged and they are now
editable without touching code.

| Key | Now | Does |
|---|---|---|
| `QuestConfig.SigilRerunDropChance` | 0.15 | rerun sigil drop rate — raid reachability |
| `QuestConfig.BossGemRewardAmount` | 2 | gems on a boss clear (flat, deliberately not scaled) |
| `QuestConfig.BossGemDropChance` | .030/.058/.083/.115 | per-difficulty **goal** chance, reached at chapter 6 |
| `QuestConfig.GemChanceFullChapter` | 6 | chapter at which the goal chance is reached |
| `QuestConfig.RareDropMaxBonus` | 0.045 | ceiling the Discernment rare-drop curve approaches |
| `QuestConfig.RareDropDiscernmentHalfway` | 111,111 | Discernment at which half that bonus is reached |
| `QuestConfig.RareDropDiscernmentCap` | 10,000,000 | hard ceiling; past this, rare drops stop improving |
| `CombatConfig.RaidHealthCostByDifficulty` | 5/10/20/40 | Health spent per raid hit |
| `CombatConfig.RaidHealthCostDefault` | 5 | fallback when a difficulty is unlisted |
| `CombatConfig.MaxThresholdDropChance` | 0.95 | clamp on scaled raid threshold drops |
| `ClassConfig.HealthRegenMinutes` | 10.0 | minutes per Health point regenerated |

> **The rare-drop curve is asymptotic, not linear.** `chance = base + RareDropMaxBonus × d / (d + Halfway)`.
> With the Pano base of 0.5%: zero Discernment gives 0.5%, 100k gives about 3.5%, and it approaches 5%
> without reaching it. `RareDropDiscernmentCap` gives the ladder a stated end rather than an asymptote
> nobody arrives at.

### 8.3 Combat and crit — `CombatConfig`

| Key | Now | Does |
|---|---|---|
| `BaseCritChance` | 0.05 | 5% floor |
| `MaxCritChanceBonus` | 0.1 | Discernment can add at most +10pp |
| `CritChancePerDiscernment` | 1e-06 | rate of approach to that cap |
| `BaseCritMultiplier` | 1.5 | crits hit for 150% |
| `MaxCritDamageBonus` | 1.0 | Discernment can add at most +100% |
| `XpPerStaminaRollMin/Max` | 1.0 / 5.0 | raid XP per stamina |
| `GoldPerStaminaRollMin/Max` | 3.0 / 8.0 | raid gold per stamina |

### 8.4 Levelling — `LevelingConfig`

`MilestoneFloors` sets a minimum XP-to-next-level at given levels (100 → 500, 500 → 3000,
1000 → 15000, and so on) so the curve cannot flatten late.

`PinnacleGemRewards` grants gems at pinnacle levels: `1000: 250`, `2500: 500`, `5000: 1500`,
`7500: 2000`, `10000: 2500`. Add a level here and it grants idempotently.

### 8.5 Gauntlet — `GauntletConfig`

| Key | Now | Does |
|---|---|---|
| `MaxLadderStage` | 250 | how deep the ladder goes |
| `StageHpBase` | 5000 | stage 1 HP |
| `StageHpGrowth` | 1.0493 | compounding per stage |
| `LateRampStartStage` | 200 | where the late ramp begins |
| `LateRampFinalGrowth` | 2.0 | growth by the final stage |
| `StrikesPerDefeat` | 10 | strikes to clear a stage |
| `StrikeGemPrice` | 1 | gems per strike |
| `MinEntryLevel` | 20 | level gate |

Stage HP is `StageHpBase × StageHpGrowth^(n−1)`. At the defaults, stage 250 lands near 80 million
break-even power. `GauntletCurveTests` prints the whole table if you want to see a change's effect.

### 8.6 Market — `MarketConfig`

| Key | Now | Does |
|---|---|---|
| `Enabled` | false | master switch |
| `ListingFeeRate` / `SaleFeeRate` | 0.02 / 0.08 | gold sinks |
| `TradeableItemTypes` | `["Material"]` | what may be listed |
| `GearTradeable` | true | gear may be listed |
| `Untradeable` | `[]` | explicit id blocklist — **use this for chase items** |
| `MinLevelToTrade` | 20 | anti-alt gate |
| `MinAccountAgeHours` | 48 | anti-alt gate |
| `MaxActiveListingsPerPlayer` | 10 | |
| `MaxGoldReceivedPerDay` / `Spent` | 50,000,000 | anti-RMT caps |
| `ListingDurationHours` | 48 | before expiry returns the stack |

### 8.7 Others

- **`GuildConfig`** — `MemberCap` 50, `LeaderInactivityDays` 14, daily sigil/ticket economy
  (`DailySigilClaimAmount` 1, `DailyTicketGrantAmount` 3, `DailyBuyCap` 3, `DailyDonateCap` 3).
- **`ClassConfig`** — `ClassUnlockLevels` (Tier2 at 5, Tier3 at 100), `ConvergenceLevels`
  (2000 Luminary → 25000 Eternal), per-class regen rates.
- **`MasteryConfig`** — `PledgeMultiplier` 2.0, `RespecGemCost` 150,
  `BulwarkMaxGuildDamagePercent` 1.0.
- **`ConsumableConfig`** — `InstantRefillGemCost` (Energy 20, Stamina 20, Health 15).
- **`LeaderboardConfig`** — `MinLevel` 20, `ExcludeAdmins` true, `PageSize` 200.
- **`RaidConfig`** — `ExpirySweepSeconds` 60.

---

## 9. Things that need code

These are hardcoded on purpose — they are structural, not tuning. Ask before changing them.

| Thing | Where |
|---|---|
| Raid difficulty HP multipliers (1.0 / 1.4 / 2.0 / 3.6) | `RaidService.cs:15–22` |
| Participant caps per size (1 / 10 / 25 / 50 / 250) | `RaidService.cs:26–33` |
| Contribution tier multipliers (Legendary ×1.5 … Participant ×0.25) | `RaidService.cs` |
| Hit sizes (1 / 5 / 20) | `RaidService.cs` |
| Quest difficulty multipliers (energy ×1/1.5/2/3, reward ×1/1.5/2/3.5) | quest difficulty code |
| The LSI cap (9.0) | `StatService` |
| Adding a **new field** to any content type | model + provider + validation |
| Adding a new rarity, item type, or raid tag | the enum, plus everything reading it |

**Also code, and deliberately so:** anything that changes what the server treats as authoritative.
The server resolves everything; the client only sends intent. That is not a knob.

---

## 10. Admin CLI

Run from the repo root. **Every one of these applies pending migrations before it runs** — that is a
schema change on a server that is behind.

```bash
dotnet run --project src/ROTA.Api -- gen-beta-key 10        # mint beta keys
dotnet run --project src/ROTA.Api -- promote <user> Moderator
dotnet run --project src/ROTA.Api -- demote  <user> Moderator
dotnet run --project src/ROTA.Api -- flag-dev <user>        # Developer flag + Dev guild
dotnet run --project src/ROTA.Api -- seed-admin             # reads Seed:AdminPassword
dotnet run --project src/ROTA.Api -- grant-gear <user> <gearId> [qty]
dotnet run --project src/ROTA.Api -- gauntlet-open <name> <startsAt> <endsAt> [neck|ring]
dotnet run --project src/ROTA.Api -- gauntlet-close <eventId>
dotnet run --project src/ROTA.Api -- gauntlet-settle <eventId>

dotnet run --project src/ROTA.Api -- beta-reset             # DRY RUN — writes nothing
dotnet run --project src/ROTA.Api -- beta-reset --confirm WIPE-BETA
```

`beta-reset` is documented in full in `docs/OPERATIONS.md` §9.1.

---

## 11. Quick recipes

**"This raid is too easy."**
`raids.json` → raise `baseHp` → **also** revisit its `thresholdRewards` in `loot_tables.json` (§5.3)
→ restart.

**"Nobody is finding the Orange set."**
`loot_tables.json` → find the `gearDrops` entries for that `setId` → raise `chance` → confirm
`rareScaling: true` → restart.

**"Chapter 4 is a slog."**
`appsettings.json` → `QuestConfig.ChapterScaling.4.EnergyCostMultiplier` down, or `XpMultiplier` up
→ restart.

**"Testers never see raids."**
`appsettings.json` → `QuestConfig.SigilRerunDropChance` up from 0.15 → restart. This is almost always
the answer, not more raid content.

**"I want the market on for this wave."**
`appsettings.json` → `MarketConfig.Enabled: true` → add any chase item ids to
`MarketConfig.Untradeable` → restart.

**"Levelling is too fast."**
`appsettings.json` → `LevelingConfig.XpBaseMultiplier` up (30 → 40 is ~33% slower) → restart.

**"I need a new boss for chapter 8."**
`raids.json` (the raid) → `items.json` (its four sigils) → `quests.json` (the boss node's `sigils`
map) → optionally `loot_tables.json` → restart. §4.3 has the long form.

---

*If the server will not start after an edit, read the last line of the log. It names the file and the
id. That message is the fastest debugging tool in this project.*
