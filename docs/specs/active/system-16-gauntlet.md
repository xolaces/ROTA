# System 16 — Gauntlet (spec + sliced task queue)

*Spec drafted 2026-06-02. Grounded in the DotD wiki (`docs/research/dotd-wiki/_clean/Gauntlet.txt`,
`Proc.txt`, `Legions.txt`) and the Design North Star §4 (the Gauntlet is ROTA's competitive spine).
The Gauntlet is **tightly coupled to System 15 Legion** — legion power is the damage source, and the
Gauntlet's signature magics + trophies fold into the EXISTING legion-power / proc pipeline rather than a
parallel combat path. Build against this exactly. LARGE epic — one slice per branch, build+test green,
commit/merge/tag independently, never bundle. Auditor reviews after a batch.*

> **STATUS: DECISIONS LOCKED (2026-06-02) — READY TO BUILD.** All 28 open questions answered by the owner.
> This block is the canonical decision record; the "OPEN QUESTIONS" section below is retained as rationale.
> Where a decision changes the data model, the modeling call is stated here and supersedes any earlier
> option-sketch in the body.
>
> **Leagues & eligibility** — (1) 3 leagues by **convergence tier**: ≤Ascendant `L1–1999` / Luminary–Archon
> `L2000–9999` / Ancient+ `L10000+` (band edges stored on each entry). (2) Entry floor **L20**;
> banned/soft-deleted excluded. (3) League **locked at first entry** for the cycle.
>
> **Event lifecycle** — (4) **Fixed-duration** event windows. (5) **Admin-triggered** open/close (CLI +
> admin endpoint); auto-scheduler deferred. (6) **One** active event at a time. (7) **Auto-settle on close**,
> but settlement MUST be **internally idempotent** — a unique `referenceId` on every prize grant (the
> gem-buy lesson) so a re-triggered/retried close cannot double-pay.
>
> **Scoring & ranking** — (8) Score = **cumulative damage to event raids** (Σ `RaidParticipant.TotalDamageDealt`
> over the event's raids). (9) Tie-break = **earliest to reach the score** (timestamp of last score-changing
> hit; stored). (10) **Persist all** entries; leaderboard **returns top 200 + caller's rank**. (11) Ranking =
> **~60s Postgres snapshot** (`ORDER BY`); no Redis sorted-set in v1.
>
> **Strikes (Gauntlet action currency — IN)** — (12) Strikes are a dedicated currency. (13) **Earned-first &
> persistent**: NO passive regen; earned by defeating Gauntlet raids (+N) and (later) low-rate drops from
> special raids; **carry over across events, never reset/deplete**. → Model as a **`strike_transactions`
> ledger** (balance = SUM, idempotent referenceId) — NOT an Energy-style `ResourceType` pool. Per-hit cost
> **scales with hit size (1/5/20)**. (14) **Buyable with gems, UNCAPPED** (balanced by competitive earning +
> drops, not solely purchasable power).
>
> **Gauntlet raids** — (15) **Dedicated** event raids (`Tier="Event"`; a `gauntlet_event_id` stamped on the
> `active_raid` scopes scoring). (16) **Personal** instances (solo damage). (17) **Escalating ladder**: a
> sequence of rising-HP stages; the player climbs as far as Strikes + legion power allow; defeating a stage
> unlocks the next (tankier) one; cumulative damage across stages = score. (Roster = a tuned HP curve, not
> per-league content.)
>
> **Wrath / Blessing (rank magics)** — (18) **Per-event consumables** (DotD-exact): event-scoped grant that
> **expires at each event reset** — must re-place to keep. (19) **Off-cap raid aura** (applied to the Gauntlet
> raid, OUTSIDE the System-14 five-magic slot cap). (20) **Owned/honor bonus REDEFINED** (supersedes the old
> +150%/+100%): a **multiplier on the magic's effective rate** — **current owner ×1.25**, **former owner ×1.10**
> (a permanent "honor echo" that persists after the per-event consumable expires, recognizing past competition;
> "for the time being"). Excluded from the <100% base-rate target; may push an owner's effective over 100%.
> (21) The **+3 loot-rarity rider is DROPPED**.
> **Numbers (locked 2026-06-02 — damage halved & toned down; `effective = procChance × procAmount`; buff toward
> a <100% effective ceiling as the game matures; rank-1 Wrath is the more beneficial at every tier):**
> **Wrath of the Ancients** (rank 1): `procChance 0.27, procAmount 2.50` → base **67.5%** (owner 84.4% / former 74.3%).
> **Blessing of the Ancients** (ranks 2–10): `procChance 0.15, procAmount 4.25` → base **63.75%** (owner 79.7% / former 70.1%).
> Both are **off-cap auras** (19), so they do NOT interact with the shared `MaxAggregateProcBonus` magic cap.
> The former-owner "honor echo" combat mechanic (a persistent reduced proc after the consumable expires) is a Slice-4 detail to finalize.
>
> **Trophies** — (22) **Permanent**, **highest-only** stacking (own several → only the best applies; **+25%
> cap**, NOT additive). (23) Attach as a multiplier on `rawLegionPower` (`× (1 + highestTrophyPct)`) **before**
> `PowerScaling`; applies to **ALL** legion power (every raid, per "boosts ALL your legions"), not just Gauntlet.
>
> **Currencies & shop** — (24) Token shop is **power-focused** (units/legions/gear are the main draw) — accept
> some snowball risk; mitigated by competitive earning + Pitchfork being top-rank-only. (25) **Separate token
> ledger** (NOT the gem ledger). (26) **Pitchfork Tokens are IN** — a second currency. → Model both via one
> **`gauntlet_currency_transactions`** ledger with a `GauntletCurrency { Token, Pitchfork }` discriminator
> (balance = SUM per currency, idempotent referenceId); this is "separate from gems" per Q25 and carries both
> currencies cleanly.
>
> **Prizes** — (27) **Prizes reach top 500** (leaderboard *view* shows top 200; 201–500 see their own rank via
> the caller slice). **Gauntlet Tokens** earned **per raid defeat + a rank-band bonus** at settlement;
> **Pitchfork Tokens** awarded to top ranks at settlement only. Bands: rank 1 → **Wrath** + Trophy(+25%);
> ranks 2–10 → **Blessing**; rank 10 → Trophy(+10%); rank 500 → Trophy(+5%); ranks 11–500 → tiered Tokens
> (+ Pitchfork for the top bands). (28) **Per-event** settlement; ladder resets each event (no season
> aggregation in v1).

---

## Core insight

**Reuse, do not rebuild.** Three pieces already exist and the Gauntlet is an orchestration on top of them:

1. **The leaderboard is ranking over raid-participant damage.** `RaidParticipant.TotalDamageDealt`
   already accumulates per-player damage per raid (`RaidService.HitRaidAsync` → `RecordHit`). A Gauntlet
   score is the cumulative sum of `TotalDamageDealt` over all of the event's raids for the player (personal
   instances, `gauntlet_event_id`-linked). The leaderboard service **reads** existing damage records into a
   ranked list; it does **not** introduce a second combat path.

2. **Legion power is already the damage source.** System 15 folds `legionPowerTerm` into `HitRaidAsync`'s
   `preProc` (`RaidService.cs` lines ~357–395). The Gauntlet adds **no new damage math** — Trophies scale
   the existing legion-power term via a highest-only multiplier; Wrath/Blessing are **off-cap auras**
   applied outside the five-magic slot cap, injected directly into the Gauntlet raid's proc resolution.
   The only genuinely new combat code is *one multiplier* (trophies, highest-only) inserted into the
   legion-power computation before `PowerScaling`.

3. **The magic, content-provider, and gem-shop machinery is reusable wholesale.** Wrath/Blessing are
   `MagicDefinition` rows (System 14) with their own per-event ownership entity (`PlayerEventMagic`).
   Trophies are a tiny new content + ownership concept mirroring `PlayerMagic`. The token shop mirrors
   `BuyMagicAsync` exactly — including the idempotency discipline — but spends **Gauntlet Tokens** from the
   `gauntlet_currency_transactions` ledger instead of gems.

### Attachment points in `HitRaidAsync` (be precise — consistent with Legion Slice 4)

The current legion-power computation (`RaidService.cs`):
```
unitSum          = Σ filled slots ( coeff[type].Atk × unit.BaseAttack + coeff[type].Def × unit.BaseDefense )
totalLegionBonus = legionDef.PowerBonus + Σ general.LegionBonus
bonusFraction    = totalLegionBonus / 100.0
rawLegionPower   = unitSum × (1.0 + bonusFraction)
legionPowerTerm  = rawLegionPower × LegionConfig.PowerScaling × hitSize × multiplier
preProc          = charBase + legionPowerTerm
```

**(A) Gauntlet Trophies → multiply `rawLegionPower` (highest-only; applies to every raid)**

Trophies are permanent. A player can own multiple (Aureate +25%, Argent +10%, Bronzed +5%) but only the
**highest-fraction trophy applies** — highest-only, not additive. The multiplier is inserted before
`PowerScaling` and is unconditional (every raid, not only Gauntlet raids):

```
trophyMult     = 1.0 + Max(ownedTrophies.Select(t => t.LegionPowerBonusFraction))
                 // or 1.0 if player owns no trophies
rawLegionPower = unitSum × (1.0 + bonusFraction) × trophyMult
```

**Why a separate multiplier on `rawLegionPower` (not folded into `bonusFraction`):**
DotD's trophy text is *"boosts the power of all your legions by X%"* — a multiplier on **legion power**,
which is exactly `rawLegionPower`. Folding the trophy pct into `bonusFraction` would produce a different
result: it adds to the `(1 + bonusFraction)` factor rather than multiplying the whole, and would let a
low-`PowerBonus` legion dilute the trophy. A separate multiplier keeps the boost a true "+X% to my legion's
power" regardless of the legion's own bonus, and composes cleanly with `PowerScaling`. Because
`legionPowerTerm` flows into `preProc`, trophies also amplify mount/magic/unit procs that scale off the
combined base — matching DotD, where bigger legion power → bigger everything.

**(B) Wrath of the Ancients (rank 1) & Blessing of the Ancients (ranks 2–10) → off-cap auras**

These are per-event consumable `MagicDefinition` rows applied to the Gauntlet raid **outside** the
five-magic slot cap (`MaxAggregateProcBonus` does not govern them). They roll their `procChance` once per
hit; on success they add `procAmount × preProc` to the raw damage. Because they are off-cap, no
`MagicCapOverride` config is needed — the cap simply does not apply to them.

**Locked numbers:**
- **Wrath of the Ancients** (rank 1): `procChance 0.27, procAmount 2.50` → base effective **67.5%**
  - Current owner ×1.25 → effective 84.4%; former owner ×1.10 → effective 74.3%
- **Blessing of the Ancients** (ranks 2–10): `procChance 0.15, procAmount 4.25` → base effective **63.75%**
  - Current owner ×1.25 → effective 79.7%; former owner ×1.10 → effective 70.1%

Rank 1 Wrath beats ranks 2–10 Blessing at every ownership tier. Both are off-cap so neither multiplier
interacts with `MaxAggregateProcBonus`.

**Owner / honor-echo multipliers:**
- **Current owner** (holds an active `PlayerEventMagic` for the current event): proc resolves with
  `procChance × 1.25` and `procAmount × 1.25`.
- **Former owner** (holds a permanent `PlayerMagicHonor` record but no active `PlayerEventMagic` for
  the current event): proc resolves with `procChance × 1.10` and `procAmount × 1.10`. Persists
  indefinitely after the per-event consumable expires. Current-owner status trumps former-owner.
- **Neither**: base values, no multiplier.

The former-owner honor echo record is written at settlement when a `PlayerEventMagic` is revoked — see Slice 5.

**(C) Score / leaderboard → reads `RaidParticipant.TotalDamageDealt`**

Because `legionPowerTerm` already lands in `damageFinal` (System 15) and `damageFinal` is what `RecordHit`
accumulates into `TotalDamageDealt`, the trophy multiplier and Wrath/Blessing proc bonuses are **already
reflected** in the participant damage the leaderboard ranks. The leaderboard slice introduces **no combat
changes** — it aggregates existing damage rows scoped to the event via `gauntlet_event_id`.

> **No parallel combat path.** The only combat-code change in this entire epic is the trophy multiplier on
> `rawLegionPower` (Slice 4) and the off-cap aura resolution for Wrath/Blessing. Everything else is
> orchestration, persistence, and content.

---

## Data model (whole epic)

*snake_case for all tables/columns/indexes; every table has `id` (UUID `gen_random_uuid()`),
`created_at`, `updated_at`, `is_deleted`; every FK indexed; private setters, no EF attributes; Fluent-only
configs in `Infrastructure/Persistence/Configurations/`.*

### Enums (`src/ROTA.Domain/Enums/`)

- `GauntletLeague { Whelpling, Wyrm, Dragon }` — the 3 leagues:
  - `Whelpling`: L1–1999 (≤Ascendant)
  - `Wyrm`: L2000–9999 (Luminary–Archon)
  - `Dragon`: L10000+ (Ancient+)
- `GauntletEventState { Scheduled, Active, Closed, Settled }` — lifecycle.
- `GauntletRewardKind { Tokens, Pitchfork, Trophy, Magic }` — what a prize-band entry grants.
- `GauntletTrophyTier { Aureate /*rank1, +25%*/, Argent /*rank10, +10%*/, Bronzed /*rank500, +5%*/ }`.
- `GauntletCurrency { Token, Pitchfork }` — discriminator on the shared currency ledger.
- `GauntletCurrencyTransactionType { RankReward, RaidDefeatReward, ShopPurchase, GemPurchase }` —
  discriminator for currency-ledger rows (parallel to `GemTransactionType`; does NOT touch the gem ledger).
- `StrikeTransactionType { RaidDefeat, GemPurchase, HitSpend, SpecialRaidDrop }` — discriminator for
  the strike ledger.

### Content models (`src/ROTA.Application/Models/`, JSON in `content/`)

**`GauntletConfig`** (appsettings, `IOptions`; safe C# defaults) — the tuning surface:
```
LeagueBounds         { Whelpling:{Min:1, Max:1999}, Wyrm:{Min:2000, Max:9999}, Dragon:{Min:10000} }
MinEntryLevel        int     default 20
PrizeRankCount       int     default 500
LeaderboardPageSize  int     default 200
ScoreSnapshotSeconds int     default 60
StrikeRatePerSize    { Small:1, Medium:5, Large:20 }   // strike cost scales with hit size (1/5/20)
StrikesPerDefeat     int     default 10                // strikes earned per Gauntlet raid stage defeated
```

**`GauntletRaidDefinition`** — a plain `RaidDefinition` with `Tier = "Event"` and a `gauntletScored: true`
flag in `content/gauntlet_raids.json`. At summon, `active_raid.gauntlet_event_id` is stamped with the
current active event's `Id` so scoring can filter without re-inspecting the definition each hit. The
escalating ladder is encoded as a sequence of boss entries ordered by `ladderStage` (rising HP); defeating
stage N unlocks stage N+1 for the same player.

**`GauntletPrizeTable`** (`content/gauntlet_prizes.json`) — per placement band → rewards. Bands MUST be
non-overlapping, contiguous, and together cover exactly 1..`PrizeRankCount`:
```json
"bands": [
  { "rankFrom": 1,   "rankTo": 1,   "tokens": 50, "pitchfork": 10,
    "trophyId": "trophy_aureate", "magicId": "magic_wrath_of_the_ancients" },
  { "rankFrom": 2,   "rankTo": 10,  "tokens": 25, "pitchfork": 5,
    "trophyId": "trophy_argent",  "magicId": "magic_blessing_of_the_ancients" },
  { "rankFrom": 11,  "rankTo": 50,  "tokens": 20, "pitchfork": 2 },
  { "rankFrom": 51,  "rankTo": 100, "tokens": 15, "pitchfork": 1 },
  { "rankFrom": 101, "rankTo": 200, "tokens": 10 },
  { "rankFrom": 201, "rankTo": 499, "tokens": 5 },
  { "rankFrom": 500, "rankTo": 500, "tokens": 5, "trophyId": "trophy_bronzed" }
]
```
*(Exact token/Pitchfork amounts are reference values; tune via JSON without code changes.)*

**Trophies as content** (`content/gauntlet_trophies.json`):
```json
[
  { "id": "trophy_aureate", "name": "Aureate Trophy", "tier": "Aureate", "legionPowerBonusFraction": 0.25 },
  { "id": "trophy_argent",  "name": "Argent Trophy",  "tier": "Argent",  "legionPowerBonusFraction": 0.10 },
  { "id": "trophy_bronzed", "name": "Bronzed Trophy", "tier": "Bronzed", "legionPowerBonusFraction": 0.05 }
]
```
Stacking rule is highest-only:
`trophyMult = 1.0 + Max(ownedTrophies.Select(t => t.LegionPowerBonusFraction))`.

**Wrath/Blessing magics** — new rows appended to `content/magics.json` (reuse `MagicDefinition` exactly).
The `offCap: true` field marks them as off-cap auras; `gemPrice: 0` means they are rank-acquired only:
```json
{ "id": "magic_wrath_of_the_ancients",    "name": "Wrath of the Ancients",
  "rarity": "Orange", "category": "Damage", "effectType": "DamageProc",
  "procChance": 0.27, "procAmount": 2.50,
  "offCap": true, "stacks": false, "gemPrice": 0, "acquisition": "Gauntlet rank 1" },
{ "id": "magic_blessing_of_the_ancients", "name": "Blessing of the Ancients",
  "rarity": "Orange", "category": "Damage", "effectType": "DamageProc",
  "procChance": 0.15, "procAmount": 4.25,
  "offCap": true, "stacks": false, "gemPrice": 0, "acquisition": "Gauntlet rank 2-10" }
```

**Naming guard:** `content/magics.json` already contains `magic_smite` ("Smite", 10%→60%) and
`magic_blessing_of_might` ("Blessing of Might", 8%→80%). These are ordinary raid-drop magics and are
**NOT** the Gauntlet magics. A startup-validation assertion enforces that the new ids do not collide
with them.

### Entities (`src/ROTA.Domain/Entities/`) — private setters, no EF attributes, snake_case

**`GauntletEvent`** — `Id, Name, State (GauntletEventState), StartsAt, EndsAt, SettledAt?,
created_at/updated_at/is_deleted`. One active at a time (enforced by the repo). Domain methods:
`Create(name, startsAt, endsAt)`, `Activate()`, `Close()`, `MarkSettled()`. Migration: `AddGauntletEvent`.

**`GauntletEntry`** — a player's standing in an event. `Id, GauntletEventId (FK, idx),
PlayerId (FK, idx), League (GauntletLeague — locked at first entry, never re-evaluated),
Score (long — cumulative damage to event raids), TieBreakAt (DateTimeOffset — timestamp of last
score-changing hit), LastRank (int? — settled/snapshot rank), created_at/updated_at/is_deleted`.
Unique index `(gauntlet_event_id, player_id)`. Domain methods: `Create(eventId, playerId, league)`,
`AddScore(long delta, DateTimeOffset hitAt)`, `SetRank(int)`. Migration: `AddGauntletEntry`.

**`StrikeTransaction`** — append-only strike ledger. `Id, PlayerId (FK, idx), Amount (int, +credit/−debit),
TransactionType (StrikeTransactionType), ReferenceId (string?, idempotency), created_at`.
Balance = `SUM(amount)`. Unique partial index on `(player_id, transaction_type, reference_id)` where
`reference_id IS NOT NULL`. Strikes carry over across events and are never reset.
Migration: `AddStrikeLedger`.

**`GauntletCurrencyTransaction`** — append-only token + Pitchfork ledger. `Id, PlayerId (FK, idx),
Currency (GauntletCurrency — discriminator), Amount (int, +credit/−debit),
TransactionType (GauntletCurrencyTransactionType), ReferenceId (string?, idempotency), created_at`.
Balance per currency = `SUM(amount) WHERE currency = X`. Unique partial index on
`(player_id, currency, transaction_type, reference_id)` where `reference_id IS NOT NULL`.
Migration: `AddGauntletCurrencyLedger`.

**`PlayerGauntletTrophy`** — permanent ownership of a trophy. `Id, PlayerId (FK, idx),
GauntletTrophyId (string), created_at/updated_at/is_deleted`. Unique `(player_id, gauntlet_trophy_id)`.
Mirrors `PlayerMagic`. Domain: `Create(playerId, trophyId)`. Migration: `AddGauntletTrophy`.

**`PlayerEventMagic`** — per-event consumable ownership of Wrath/Blessing. `Id, PlayerId (FK, idx),
GauntletEventId (FK, idx), MagicDefinitionId (string), created_at/updated_at/is_deleted`. Unique
`(player_id, gauntlet_event_id, magic_definition_id)`. Revoked (soft-deleted) at event close. Domain:
`Create(playerId, eventId, magicId)`, `Revoke()`. Migration: `AddPlayerEventMagic`.

**`PlayerMagicHonor`** — permanent "honor echo" record written when a player's `PlayerEventMagic` expires.
`Id, PlayerId (FK, idx), MagicDefinitionId (string), created_at/updated_at/is_deleted`. Unique
`(player_id, magic_definition_id)`. Read by combat integration to apply the ×1.10 former-owner proc
multiplier. Domain: `Create(playerId, magicId)`. Migration: `AddPlayerMagicHonor`.

**`active_raid.gauntlet_event_id` (nullable FK column)** — stamped at summon on Gauntlet-eligible raids;
links the raid to a specific event for scoring and off-cap aura resolution.
Migration: `AddGauntletRaidLink`.

### Config: `GauntletConfig` (appsettings, `IOptions`) — see Content models above.

---

## Scoring / ranking formula

```
For each active GauntletEvent E:
  eligible raids    = active_raid rows where gauntlet_event_id = E.Id
  per-player score  = Σ TotalDamageDealt over the player's RaidParticipant rows on eligible raids
  league(player)    = GauntletEntry.League (locked at first join, never re-evaluated mid-event)
  rank within league= ORDER BY score DESC, TieBreakAt ASC (earliest to reach the score wins)
  prize(rank)       = GauntletPrizeTable band containing rank (top PrizeRankCount = 500 only)
```

**Leaderboard read:** `GET /api/gauntlet/leaderboard?league=` returns the top 200 entries for the
requested league, plus the caller's own rank and score regardless of position. Ranks are drawn from the
~60-second Postgres snapshot (`ORDER BY score DESC, tiebreak_at ASC` within league, materialized into
`GauntletEntry.LastRank` by a background hosted service). No Redis sorted-set in v1.

**Where the combat amplifiers enter the score (already, for free):**
`damageFinal` (the value `RecordHit` accumulates into `TotalDamageDealt`) already includes:
- the legion-power term (`legionPowerTerm`), **multiplied by the Gauntlet Trophy multiplier** (Slice 4
  change A — highest-only trophy before `PowerScaling`),
- the Wrath/Blessing off-cap aura proc bonuses when those magics are active on the raid,
- crit, mount procs, unit procs, in-cap magic procs — all of which scale off `preProc = charBase +
  legionPowerTerm`, so they too are amplified by trophies.

A higher-ranked player's trophies and rank-magics make every hit score more — this is the intended
"grind → rank → bigger hits → rank higher" flywheel. No score-side code computes any of this; it is the
natural consequence of those effects landing in `damageFinal` upstream.

**Attachment summary (locked numbers):**

| Effect | ROTA attach point | Locked value |
|---|---|---|
| Wrath of the Ancients (rank 1) | off-cap aura on Gauntlet raid | `procChance 0.27`, `procAmount 2.50` → 67.5% base |
| Blessing of the Ancients (ranks 2–10) | off-cap aura on Gauntlet raid | `procChance 0.15`, `procAmount 4.25` → 63.75% base |
| Current-owner ×1.25 | multiplied into proc values at hit resolution | active `PlayerEventMagic` for event |
| Former-owner ×1.10 honor echo | multiplied into proc values at hit resolution | permanent `PlayerMagicHonor` record |
| Trophy Aureate (rank 1) | `rawLegionPower ×= (1 + 0.25)` before `PowerScaling` | highest-only; +0.25 |
| Trophy Argent (rank 10) | same multiplier (highest-only) | +0.10 |
| Trophy Bronzed (rank 500) | same multiplier (highest-only) | +0.05 |

---

## SLICE 1 — Gauntlet content + definitions  *(additive · LIGHT)*

**Scope:** All enums, content models, provider, and JSON files. No entities, no migrations, no endpoints.

- Enums: `GauntletLeague`, `GauntletEventState`, `GauntletRewardKind`, `GauntletTrophyTier`,
  `GauntletCurrency`, `GauntletCurrencyTransactionType`, `StrikeTransactionType`.
- Models: `GauntletConfig` (registered from appsettings), `GauntletPrizeTable` (+ band shape), trophy
  content model, and the **two new magic rows** appended to `content/magics.json` with `offCap: true` and
  the locked proc numbers.
- JSON files: `content/gauntlet_prizes.json`, `content/gauntlet_trophies.json`,
  `content/gauntlet_raids.json`.
- `IGauntletContentProvider` (singleton; pattern = `MagicDefinitionProvider`). Startup validation throws on:
  - Duplicate ids across any content type.
  - Prize bands overlapping, non-contiguous, or not covering exactly 1..`PrizeRankCount`.
  - Band referencing a `trophyId` or `magicId` not present in the provider.
  - Trophy `legionPowerBonusFraction` ≤ 0.
  - League bounds overlapping, non-ordered, or not covering the full valid level range.
  - `procChance` outside (0, 1] or `procAmount` ≤ 0 for any DamageProc magic.
  - Either Gauntlet magic missing `offCap: true`.
  - **Naming guard:** `magic_wrath_of_the_ancients` or `magic_blessing_of_the_ancients` id collides
    with `magic_smite` or `magic_blessing_of_might`.
- Register provider + `GauntletConfig` from appsettings.
- Tests: content loads; duplicate id throws; overlapping bands throw; gap in bands throws; band with
  missing trophyId/magicId throws; Wrath loads with `procChance 0.27`, `procAmount 2.50`, `offCap true`,
  `gemPrice 0`; Blessing loads with `procChance 0.15`, `procAmount 4.25`, `offCap true`, `gemPrice 0`;
  naming guard assertion fires on collision; league bounds validated.
- **Acceptance:** providers load and validate at startup; build 0 warnings; tests green. **Commit independently.**
- **Review depth:** LIGHT (content/validation only).

---

## SLICE 2 — Ownership + ledgers (Strike + Gauntlet currency) + event lifecycle / admin  *(additive + migrations · MODERATE)*

**Scope:** All persistence entities, repositories, admin lifecycle, and join flow. No combat changes.

- Entities + Fluent configs (snake_case, unique indexes, FK indexes):
  - `GauntletEvent`, `GauntletEntry`, `StrikeTransaction`, `GauntletCurrencyTransaction`,
    `PlayerGauntletTrophy`, `PlayerEventMagic`, `PlayerMagicHonor`.
  - Nullable `gauntlet_event_id` column on `active_raid` (FK to `gauntlet_event`, nullable, indexed).
- Migrations (in order): `AddGauntletEvent`, `AddGauntletEntry`, `AddStrikeLedger`,
  `AddGauntletCurrencyLedger`, `AddGauntletTrophy`, `AddPlayerEventMagic`, `AddPlayerMagicHonor`,
  `AddGauntletRaidLink`. **Do NOT run `dotnet ef database update`.**
- `DbSet` additions in `RotaDbContext`.
- Repositories:
  - `IGauntletEventRepository`: `GetActiveAsync()`, `FindByIdAsync()`, `CreateAsync()`, `UpdateAsync()` —
    enforces ≤1 active.
  - `IGauntletEntryRepository`: `FindByEventAndPlayerAsync()`, `GetForEventAsync()`, `UpsertAsync()`.
  - `IStrikeRepository`: `GetBalanceAsync(playerId)`, `CreateAsync()`, `ReferenceExistsAsync()`,
    `SpendAsync(playerId, amount, referenceId)` (tri-state: `Charged | Insufficient | AlreadyCharged`).
  - `IGauntletCurrencyRepository`: `GetBalanceAsync(playerId, currency)`, `CreateAsync()`,
    `ReferenceExistsAsync()`, `SpendAsync(playerId, currency, amount, referenceId)` (same tri-state).
  - `IPlayerGauntletTrophyRepository`: `GetForPlayerAsync()`, `UpsertAsync()`.
  - `IPlayerEventMagicRepository`: `FindAsync(playerId, eventId, magicId)`, `GrantAsync()`,
    `RevokeAllForEventAsync(eventId)`.
  - `IPlayerMagicHonorRepository`: `HasHonorAsync(playerId, magicId)`, `GrantAsync()`.
- `IGauntletService` (initial): `GetCurrentEventAsync()`, `JoinEventAsync(playerId)` (creates
  `GauntletEntry` with league locked by convergence tier; rejects L<20, banned/soft-deleted; idempotent
  on re-join), `GetMyEntryAsync(playerId, eventId)`.
- **Admin lifecycle:** `IGauntletAdminService` + admin endpoints `[AdminOnly]`:
  - `POST /api/admin/gauntlet/events` → open a new event (name, startsAt, endsAt; enforces ≤1 active).
  - `POST /api/admin/gauntlet/events/{id}/close` → close and auto-trigger settlement.
  - `POST /api/admin/gauntlet/events/{id}/settle` → idempotent re-settle (Slice 5 fills payout; here
    transitions state only; no-op if already settled).
  - CLI commands: `gauntlet-open`, `gauntlet-close`, `gauntlet-settle` — mirrors `AdminCli.cs` pattern.
- **Gem → Strikes purchase:** `POST /api/gauntlet/strikes/buy` — deducts gems (via `ISpendGemsAsync`) and
  credits the `strike_transactions` ledger. Uncapped. Idempotent referenceId:
  `strikebuy:{playerId}:{gemTransactionId}`.
- `GauntletController` `[Authorize]`:
  - `GET /api/gauntlet` — current event + caller's entry/league + strike balance + Token/Pitchfork balance.
  - `POST /api/gauntlet/join` — creates `GauntletEntry`; idempotent.
  - `POST /api/gauntlet/strikes/buy` — gem-to-strikes purchase.
- DTOs: `GauntletEventResponse`, `GauntletEntryResponse`, `StrikeBalanceResponse`,
  `GauntletCurrencyBalanceResponse`.
- Tests: open event (≤1 active enforced; second open fails); join assigns correct league at each band edge
  (L1999 → Whelpling, L2000 → Wyrm, L9999 → Wyrm, L10000 → Dragon); L19 join rejected; double-join
  idempotent; close/settle state transitions guarded; strike ledger balance = SUM, idempotent referenceId;
  currency ledger balance computed per `GauntletCurrency` discriminator, idempotent referenceId; gem
  purchase credits strikes; league locked at entry even if player levels mid-event.
- **Acceptance:** admin can open an event; player can join and is placed in the correct league; ledgers
  enforce idempotency; state transitions are guarded. **Commit independently.**
- **Review depth:** MODERATE (state machine + ≤1-active guard + league-edge correctness + dual-ledger
  idempotency).

---

## SLICE 3 — Leaderboard / scoring  *(read-aggregation · MODERATE)*

**Scope:** Scoring aggregation, snapshot rank, leaderboard endpoint. No combat changes.

- **Score storage:** `GauntletEntry.Score` is a denormalized running total updated per qualifying hit
  (Slice 4 wires the update). The authority is always the participant damage written by `HitRaidAsync`; the
  denormalized field is a read-optimized projection. `TieBreakAt` is set to the `DateTimeOffset` of the
  last hit that increased `Score` — not updated on zero-delta hits.
- `IGauntletScoringService`:
  - `UpdateScoreAsync(playerId, eventId, deltaScore, hitAt)` — increments `GauntletEntry.Score` and
    updates `TieBreakAt` if `deltaScore > 0`. Called by Slice 4's combat-integration hook.
  - `RecomputeRanksAsync(eventId)` — snapshots `ORDER BY score DESC, tiebreak_at ASC` within each league
    into `GauntletEntry.LastRank`. Runs on a ~60-second `IHostedService` cadence and at settlement.
    Idempotent.
  - `GetLeaderboardAsync(eventId, league, callerId)` → `GauntletLeaderboardResponse`: top 200 entries
    for the league ordered by `LastRank`, plus the caller's own rank/score regardless of position.
    Reads from the snapshot.
- Endpoint: `GET /api/gauntlet/leaderboard?league=` (`[Authorize]`) → top-200 page + `YourRank` /
  `YourScore`.
- Tests: ranking order by score then `TieBreakAt`; caller rank returned when outside top-200; empty
  league returns empty list; league isolation (Wyrm entry never appears in Whelpling board); tie resolved
  deterministically (earlier `TieBreakAt` wins); `RecomputeRanksAsync` updates `LastRank`;
  `UpdateScoreAsync` does not update `TieBreakAt` on zero-delta hit.
- **Acceptance:** leaderboard returns correct per-league ranks and the caller's own standing. **Commit independently.**
- **Review depth:** MODERATE (ranking correctness + tiebreak determinism + league isolation + snapshot
  freshness).

---

## SLICE 4 — Combat integration  *(DEEP / combat-money path)*

**This is the deep / money-path review slice.** It touches `HitRaidAsync` and the score that drives all
prizes. The whole point is to add the Gauntlet amplifiers **without forking the combat path** and
**without breaking** existing mount/magic/unit/crit assertions.

**Trophy multiplier (highest-only; applies to every raid):**
Inject `IPlayerGauntletTrophyRepository` + trophy content provider into `RaidService` (scoped). Inside
the advisory-lock callback, in the legion-power block, after
`rawLegionPower = unitSum × (1 + bonusFraction)` and **before** applying `PowerScaling`:

```csharp
var ownedFractions = await _trophyRepo.GetForPlayerAsync(playerId);
double trophyMult = ownedFractions.Any()
    ? 1.0 + ownedFractions.Max(t => t.LegionPowerBonusFraction)
    : 1.0;
rawLegionPower = rawLegionPower * trophyMult;
```

Applied unconditionally to every raid. A player with no trophies gets `trophyMult = 1.0` — zero change
to existing behavior.

**Off-cap aura resolution (Gauntlet raids only, when `raid.GauntletEventId != null`):**
After `preProc` is computed and before the capped-proc accumulation loop:

1. Load `PlayerEventMagic` for player + current event to determine current-owner status.
2. Load `PlayerMagicHonor` for the player to determine former-owner status.
3. Ownership tier (current trumps former): current owner → ×1.25; former only → ×1.10; neither → ×1.00.
4. For each off-cap magic the player is eligible for (Wrath if rank-1 owner/former, Blessing if rank-2–10):
   - `effectiveProcChance = magic.ProcChance × ownershipMult`
   - `effectiveProcAmount = magic.ProcAmount × ownershipMult`
   - Roll `effectiveProcChance`; on success add `effectiveProcAmount × preProc` to `offCapBonus`.
5. `damageFinal += offCapBonus` — added after the capped proc pass, never governed by
   `MaxAggregateProcBonus`.

**Strike spend (before hit is accepted):**
Verify sufficient strikes for the requested hit size (cost: Small=1, Medium=5, Large=20). Deduct via
`IStrikeRepository.SpendAsync` with idempotent referenceId reusing the per-hit key
(`strikespend:{activeRaidId}:{hitKey}`). Reject 422 `InsufficientStrikes` if balance < cost.

**Event linkage + score update:**
When a hit lands on a raid with `gauntlet_event_id` set, after `RecordHit` writes `TotalDamageDealt`,
call `IGauntletScoringService.UpdateScoreAsync(playerId, eventId, damageDelta, hitAt)`. No second damage
computation — reads the participant row already written.

**No regression:** a player with no trophies and no rank magics hits **identically** to today. All existing
`RaidService` tests must pass unchanged.

Tests (seeded RNG, mocked repos):
- No trophies → `trophyMult = 1.0` → legion term unchanged.
- One Aureate trophy → legion term ×1.25; downstream procs scale accordingly.
- Player owns Aureate + Argent + Bronzed → `trophyMult = 1.25` (highest-only; NOT 1.40 additive).
- No rank magic, no honor → `offCapBonus = 0`.
- Current Wrath owner → proc at `0.27 × 1.25 = 0.3375`, amount `2.50 × 1.25 = 3.125`; not added to
  capped-proc accumulator.
- Former Wrath owner (no active) → proc at `0.27 × 1.10 = 0.297`, amount `2.50 × 1.10 = 2.75`.
- Wrath base (neither) → `procChance 0.27`, amount `2.50`.
- Blessing base → `procChance 0.15`, amount `4.25`; off-cap regardless of in-cap procs already fired.
- Gauntlet hit updates `GauntletEntry.Score` and `TieBreakAt`; non-Gauntlet hit does not.
- Insufficient strikes → 422 `InsufficientStrikes`; sufficient → strikes deducted.
- Duplicate hit key → idempotent (no double-strike deduction).
- All prior combat assertions still green.
- **Acceptance:** trophies (highest-only) and rank magics (off-cap, honor-multiplied) amplify damage per
  the locked numbers; score reflects it; no existing combat behavior changes for players without Gauntlet
  items. **Commit independently.**
- **Review depth:** DEEP (formula order — trophy before `PowerScaling`, highest-only not additive;
  off-cap separation from `MaxAggregateProcBonus`; honor-echo multiplier resolution; strike deduction
  idempotency; score authority = single participant write; no parallel path).

---

## SLICE 5 — Settlement (idempotent)  *(MODERATE → DEEP on idempotency)*

**Scope:** Auto-settle on event close, all prize distribution, honor-echo write-back.

**Settlement (`IGauntletAdminService.SettleEventAsync(eventId)`) — idempotent and re-runnable:**

1. Snapshot ranks: `RecomputeRanksAsync(eventId)` (idempotent).
2. For each league, for each entry in rank order within `PrizeRankCount` (top 500):
   a. Look up the prize band from `GauntletPrizeTable`.
   b. **Gauntlet Tokens** → credit `gauntlet_currency_transactions` (Token) with referenceId
      `gauntletsettle:{eventId}:{playerId}:tokens`.
   c. **Pitchfork Tokens** (where applicable per band) → credit same ledger (Pitchfork) with referenceId
      `gauntletsettle:{eventId}:{playerId}:pitchfork`.
   d. **Trophy** (rank 1 / 2–10 / 500) → `PlayerGauntletTrophy` upsert; idempotent (`Max:1` per tier).
   e. **Rank magic** (Wrath rank 1, Blessing ranks 2–10) → grant `PlayerEventMagic` scoped to the
      **next** active event (opened after this close). Idempotent: no duplicate if record already exists
      for `(player, nextEventId, magicId)`.
3. **Honor-echo write-back:** `RevokeAllForEventAsync(eventId)` soft-deletes all `PlayerEventMagic` rows
   for the closing event. For each revoked holder, create a `PlayerMagicHonor` record if not already
   present — idempotent.
4. Mark `GauntletEvent.MarkSettled()` only after all grants commit.
5. Re-running on an already-settled event: all steps are idempotent; no exception; returns summary.

**Per-raid-defeat reward loop:**
Wired into `HitRaidAsync` → on defeat of a `gauntlet_event_id`-linked raid:
- `+StrikesPerDefeat` strikes to `strike_transactions` with referenceId
  `gauntletdefeat:{activeRaidId}:{playerId}:strikes`. Idempotent.
- `+1 Token` to `gauntlet_currency_transactions` (Token) with referenceId
  `gauntletdefeat:{activeRaidId}:{playerId}:token`. Idempotent.

DTOs: `GauntletSettlementSummaryResponse` (counts: ranks settled, tokens granted, trophies granted,
magics granted).

Tests:
- Settle grants correct tokens/trophy/magic to each rank band.
- **Settle-twice pays once** (idempotency — the magic money-bug class).
- Rank 1: Wrath + Aureate trophy + tokens + Pitchfork; correct amounts.
- Ranks 2–10: Blessing + Argent trophy (band upper edge includes rank 10) + tokens + Pitchfork.
- Rank 500: Bronzed trophy + tokens (no Pitchfork, no magic).
- Ranks 11–499: tokens only (tiered per band; top sub-bands also Pitchfork).
- Raid defeat credits `StrikesPerDefeat` strikes and 1 token; idempotent on duplicate kill processing.
- Honor-echo write-back: `PlayerMagicHonor` created for revoked Wrath holder; idempotent on re-settle.
- Re-settle on already-settled event: no exception, no double-pay, returns summary.
- **Acceptance:** closing + settling distributes exactly the locked prizes, idempotently, and writes
  honor-echo records. **Commit independently.**
- **Review depth:** MODERATE → DEEP (idempotent settlement — same class as magic/gem money-bug; a botched
  re-run must not double-pay tokens, trophies, or magics).

---

## SLICE 6 — Token shop  *(MODERATE)*

**Scope:** Shop catalogue, purchase flow with tri-state spend, endpoints.

- `content/gauntlet_shop.json` — token-shop catalogue (power-focused: units, legions, gear, gem bundles,
  Strike refills): `{ id, rewardKind, payloadId, currency (Token|Pitchfork), price, maxOwned? }`.
  Validated at startup (referential integrity on `payloadId`; price > 0; currency valid; no duplicate ids).
- `IGauntletService.BuyFromShopAsync(playerId, shopEntryId)` — **mirrors `BuyMagicAsync` exactly,
  including its idempotency discipline**, but spends from the `gauntlet_currency_transactions` ledger:
  1. Ownership pre-check: reject `AlreadyOwned` for `maxOwned: 1` items **without charging**.
  2. Spend with tri-state result (`Charged | Insufficient | AlreadyCharged`) and idempotent referenceId
     `gauntletshop:{playerId}:{shopEntryId}`. `AlreadyCharged` means the spend row already exists — grant
     the payload without re-charging. `Insufficient` returns `InsufficientTokens` with no write.
  3. Grant the payload.
  4. **Tri-state is the explicit safeguard against the magic money-bug** — "insufficient" and "already
     charged" must never be conflated.
- Endpoints:
  - `GET /api/gauntlet/shop` — catalogue + caller's Token and Pitchfork balances.
  - `POST /api/gauntlet/shop/{entryId}/buy` — purchase item.
- DTOs: `GauntletShopEntryResponse`, `GauntletShopResponse` (catalogue + balances),
  `BuyShopResult { Success, AlreadyOwned, InsufficientTokens, AlreadyCharged }`.
- Tests:
  - Buy success: tokens debited, payload granted.
  - Insufficient tokens: no charge, no grant.
  - **Buy-twice charges once** (idempotency): second call returns `AlreadyCharged`, re-grants, no
    double-debit.
  - Already-owned `maxOwned: 1` item: `AlreadyOwned` without charge.
  - Catalogue lists with live Token and Pitchfork balances.
  - Pitchfork-priced item attempted with Token balance: `InsufficientTokens` (wrong currency).
  - Startup throws on bad `payloadId` or price = 0.
- **Acceptance:** Tokens and Pitchfork earned from rank/raids can be spent in the shop, idempotently,
  with no double-charge. **Commit independently.**
- **Review depth:** MODERATE (economy idempotency — same class as magic/gem money-bug; tri-state spend is
  the explicit fix; Token vs. Pitchfork currency isolation).

---

## Deferred items (document, do NOT build this epic)

- **Automatic event scheduler / cadence engine.** v1 lifecycle is admin-gated (open/close/settle via CLI
  + admin endpoints). A cron/Quartz-style auto-cadence (e.g. weekly events) is Phase 2+.
- **Low-rate Strike drops from special (non-Gauntlet) raids.** Strikes are earned from Gauntlet raid
  defeats in v1; the "special raid drop" earning path mentioned in the locked decisions is Phase 2+.
- **The "+3 to all loot rarity tiers" rider** on Wrath/Blessing — does not map to ROTA's
  threshold/chance loot model; dropped per decision 21.
- **Separate Gauntlet Battalion loadout with paid expansion slots** (DotD). v1 uses the player's existing
  active legion; a dedicated Gauntlet loadout is Phase 2+ (depends on the fuller collection/fodder layer,
  North Star §5).
- **Per-league raid scaling / per-league boss rosters.** v1 uses a single HP curve for all leagues;
  competitive separation is via score ranking, not content gating.
- **Season-cumulative (cross-event) prize aggregation.** v1 is per-event only.
- **Multi-account bracket tooling.**
- **Real-time leaderboard push** (SignalR). v1 is pull (`GET .../leaderboard`); SignalR hubs are not yet
  mapped.

---

## Constraints (every slice — copied from the Legion spec, binding)

- Domain entities: private setters, no EF attributes; state via methods/factories.
- EF Fluent only, snake_case; every table `id`/`created_at`/`updated_at`/`is_deleted`; FKs indexed.
  Heed the **EF enum + store-default rule** (`HasSentinel`) for any enum column with a non-zero
  `HasDefaultValue` (learned via the `RaidSize`/`PlayerRoles` bug).
- Content providers singletons; throw at startup on invalid data (overlapping prize bands, dangling
  trophy/magic ids, bad league bounds, out-of-range proc values).
- Controllers thin; `PlayerId` from JWT `sub`; server-authoritative. Admin endpoints `[AdminOnly]` with
  DB actor re-verify where they mutate.
- Every state change writes to `audit_log` (event open/close/settle, join, prize grants, shop buys,
  strike spends).
- **Do NOT run `dotnet ef database update`.** Build **0 warnings**; **all tests green** before committing a
  slice. Update `docs/PROJECT_STATE.md` count + `docs/ROTA_Function_Reference.md` as you go.
- **No co-author trailer.** **One branch + one merge + one tag per slice; never bundle.** Do **not** push
  until the owner says so. Auditor reviews after a batch (DEEP review mandatory on Slice 4; settlement
  idempotency on Slice 5; shop idempotency on Slice 6).
