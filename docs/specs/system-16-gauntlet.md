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
> raid, OUTSIDE the System-14 five-magic slot cap). (20) **Keep the self-ownership bonus** (Wrath +150% /
> Blessing +100% when owned, via a `Conditions` scalar). (21) The **+3 loot-rarity rider is DROPPED**.
> Numbers (locked): **Wrath of the Ancients** `procChance 0.24, procAmount 5.00` (rank 1); **Blessing of the
> Ancients** `procChance 0.13, procAmount 8.50` (ranks 2–10). **Slice-4 landmine:** the 850% proc must be
> **exempt from / above the shared `MaxAggregateProcBonus` magic cap** or it silently clamps — handle explicitly.
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

## OPEN QUESTIONS — ALL RESOLVED 2026-06-02 (answers locked in the STATUS block above; prose retained as rationale)

These are the decisions the locked notes do **not** pin down. I have deliberately **not invented
answers** — each one changes the schema or the scoring. Where I sketch options it is only to frame the
choice; pick one.

**Leagues & eligibility**
1. **Exact league boundaries.** DotD used **player level**: Whelpling 20–999 / Wyrm 1000–2499 /
   Dragon 2500+. ROTA's convergence tiers are far taller (Eternal at L25,000). Do we (a) copy DotD's
   level bands verbatim, (b) re-cut the 3 bands against ROTA's curve (e.g. by convergence tier:
   ≤ Ascendant / Luminary–Archon / Ancient+), or (c) band by *account power* (a computed metric) rather
   than level? **This is the #1 blocker — the band edges are stored on every entry and drive ranking.**
2. **Minimum level to enter** (DotD floor was L20). What is ROTA's floor, and are banned/soft-deleted
   players excluded (assume yes)?
3. **League assignment timing.** Is a player's league **locked at first entry** for the cycle, or
   **re-evaluated live** as they level mid-event? (Locking is simpler and prevents sandbagging; live
   re-eval matches "your level right now". Pick one — it changes whether `league` is stored on the
   entry or computed each hit.)

**Event lifecycle & cadence**
4. **Event duration + reset cadence.** North Star says "core, recurring … exact cadence TBD." Need a
   concrete default (e.g. 72-hour events on a weekly cadence? a permanent rolling ladder that snapshots
   weekly?). This decides whether we need an **event scheduler** (none exists today) or whether the
   admin opens/closes events manually via CLI/endpoint for the beta.
5. **Who opens/closes an event?** For the closed beta, is **admin-triggered open/close** (CLI + admin
   endpoint) acceptable as the v1, deferring an automatic scheduler? (Strong recommendation: yes —
   ship admin-gated lifecycle first, automate cadence later. Confirm.)
6. **Concurrent events.** At most **one active Gauntlet event at a time**, or can several run? (Assume
   one for v1 unless told otherwise — multiple events multiply the leaderboard keys.)
7. **Prize settlement trigger.** When the event closes, are prizes **settled automatically** at close,
   or by an **explicit admin "settle" action** (idempotent, re-runnable)? (Recommend explicit,
   idempotent settle so a botched close can be re-run without double-paying. Confirm.)

**Scoring & ranking**
8. **Score definition.** Is a player's Gauntlet score **cumulative damage dealt to Gauntlet raids
   during the event** (sum of `RaidParticipant.TotalDamageDealt` over event raids), **total Gauntlet
   raids defeated**, or **a points formula**? DotD's *prizes* were per-raid (10 Strikes + 1 Token each)
   but the *leaderboard* needs a single comparable score. **Confirm the exact metric** — it's the spine
   of the whole epic.
9. **Tie-breaking.** When two entries have equal score, rank by (a) earliest-to-reach-that-score
   (timestamp of last score-changing hit), (b) fewest strikes spent, or (c) account level? Need a
   deterministic, stored tiebreak key so ranks are stable.
10. **Leaderboard size / cutoff.** Top-500 **per league** win prizes (locked). Do we **store/return
    only the top N** (e.g. top 500 + the caller's own rank), or the full ladder? (Recommend store all
    entries, return top-500 + caller slice. Confirm N for the returned page.)
11. **Rank-read latency.** Is a **near-real-time rank** (recomputed on demand / cached in Redis) required
    during the event, or is a **periodically-refreshed snapshot** (e.g. every 60 s) acceptable? Affects
    whether ranking lives in Postgres (ORDER BY) or a Redis sorted set.

**Strikes (the Gauntlet action currency)**
12. **Does the Gauntlet use Strikes at all in v1, or reuse Stamina?** North Star §1/§4 names **Strikes**
    as a dedicated consumable currency (earned + buyable), separate from Stamina. The LOCKED notes for
    THIS epic don't mention Strikes. Decide: (a) v1 reuses the existing **Stamina** pool for Gauntlet
    hits (less new surface), or (b) v1 introduces **Strikes** as a new `ResourceType` + regen + the
    "defeat a raid → +10 Strikes, +1 Token" loop. **This is a model-shaping decision** — if Strikes are
    in, Slice 2 grows.
13. If Strikes are in: **starting/max Strikes, regen rate, and per-hit Strike cost.** DotD returned
    **10 Strikes per raid defeated**; what is the **per-attack** strike cost and the regen/cap?
14. If Strikes are in: are they **buyable with gems/premium** in v1, and at what rate (within the §3
    "buying accelerates but does not trivialize" cap discipline)?

**Gauntlet raids**
15. **Dedicated event raid vs. existing raids.** Does the Gauntlet summon its **own event raid
    definition(s)** (`Tier="Event"`, which `RaidDefinition` already supports) that only count toward the
    leaderboard, or do **existing World/Event raids** count while an event is live? **Strong
    recommendation: dedicated Gauntlet raid definitions** so non-event raid activity never pollutes the
    ladder. Confirm — this decides whether scoring filters by a `gauntlet_event_id` stamped on the
    `active_raid` row (new column/link) or by raid-definition tag.
16. **Raid sizing / who shares a raid.** Are Gauntlet raids **personal** (each player vs. their own
    instance — purest competition) or **shared/co-op** (DotD-style multi-player raids)? This changes
    whether "score" is solo damage or contribution within a shared pool. (Recommend personal instances
    for clean per-player scoring; confirm.)
17. **Raid roster + difficulty.** How many Gauntlet bosses, at what HP/difficulty, and do they scale by
    league? (Content question — needs authoring, but the *count* affects Slice 1.)

**Wrath of the Ancients / Blessing of the Ancients (the rank magics)**
18. **Permanent vs. per-event consumable.** DotD **removes** SMITE / Blessing of Mathala **each time the
    Gauntlet is summoned** (i.e. they last one event and must be re-won). LOCKED says "acquired by
    Gauntlet rank." Decide: are Wrath/Blessing (a) **consumed/expired at each event reset** (DotD-exact —
    you must place top-1 / top-10 again to keep them), or (b) **permanent once earned**? **This changes
    the data model**: (a) needs an event-scoped grant with expiry/removal at reset; (b) is a plain
    permanent `PlayerMagic`. (My read of the design intent — "acquired by rank", prestige churn — leans
    DotD-exact consumable, but this is explicitly the owner's call.)
19. **Do they fold into the EXISTING magic slot cap** (the System 14 5-magic-per-raid cap), or are they
    **off-cap** (always active for the owner during the event, like a passive)? DotD wording — "Attacks
    by **any raid member**…" — implies a raid-wide aura placed on the raid, which matches the System 14
    `RaidMagic` "applied to the raid" model. Confirm they go through `ApplyMagicAsync`/the slot cap vs.
    being an always-on owner buff.
20. **"+X% extra if you own SMITE/Blessing" self-bonus.** DotD gives extra damage if you *own* the magic
    on top of the proc (SMITE: +150% self; Blessing: +100% self). Keep this self-ownership bonus, or
    drop it for ROTA simplicity? (It's a `Conditions`-style ownership scalar on the magic def — cheap to
    keep, but confirm the numbers/whether to include.)
21. **The "+3 to all loot rarity tiers" rider** on SMITE/Blessing in DotD — keep, drop, or defer? ROTA's
    loot model is threshold/chance-based, not rarity-tier-count; this rider does not map cleanly. Recommend
    **defer/drop** for v1 and document. Confirm.

**Gauntlet Trophies (the legion-power boosters)**
22. **Permanent (assumed) and stacking.** LOCKED: Trophy rank 1 / 10 / 500 → +25% / +10% / +5% to **ALL
    legion power**, each `Max: 1`. Confirm they are **permanent account items** (not event-scoped) and
    that a player can own **more than one** (e.g. someone who placed top-1 once and top-10 another time
    owns both → **do the percentages stack additively (+35%)** or does **only the highest apply (+25%)**?).
    DotD's separate "extra damage if Aureate/Argent/Bronzed owned" lines on the *magics* are additive, but
    the *legion-power* boost is the relevant one here — **state the stacking rule explicitly.**
23. **Where exactly in the legion-power term do trophies attach** — see Core Insight §"Attachment points".
    I propose multiplying `rawLegionPower` by `(1 + Σ trophyPct)`. Confirm that vs. folding trophyPct into
    `bonusFraction` (the two are NOT equivalent — see the math note). **This is a combat-money decision.**

**Token shop**
24. **Token-shop contents + prices.** LOCKED: currency = Gauntlet Tokens → a token shop. What does it
    sell, and at what token prices? (Candidates: cosmetic/prestige items, units/legions, gem bundles,
    Strike refills, stat bags.) Need at least a v1 catalogue to author Slice 6. **The Trophies and the
    Wrath/Blessing magics are awarded by *rank*, not bought — confirm they are NOT in the token shop.**
25. **Tokens: new ledger or reuse the gem ledger?** Gauntlet Tokens are a **distinct currency**
    (North Star §3 explicitly allows scoped mode currencies). Reusing `gem_transactions` with a new
    `GemTransactionType` would conflate balances (gem balance = SUM over all types). **Recommendation: a
    separate `gauntlet_token_transactions` append-only ledger** mirroring the gem ledger (balance = SUM,
    idempotent referenceId). Confirm — this is a Slice 5/6 model decision.
26. **Pitchfork Tokens** (DotD awarded a second currency, "Pitchfork Tokens", alongside Gauntlet Tokens to
    top-500). In/out for ROTA v1? (Recommend **out** — currency discipline, North Star §3. Confirm.)

**Prizes & rewards**
27. **Full prize table per rank band.** DotD's token award table was tiered by placement
    (rank 1 / 10 / 50 / 100 / 200 / 500). Confirm ROTA's **Gauntlet-Token award per band**, plus which
    bands grant **Trophies** (locked: 1 / 10 / 500) and which grant **Wrath/Blessing** (locked: 1 / 2–10).
    What do ranks 11–500 get besides tokens?
28. **Are prizes per-event or season-cumulative?** (Assume per-event settlement. Confirm.)

---

## Core insight

**Reuse, do not rebuild.** Three pieces already exist and the Gauntlet is an orchestration on top of them:

1. **The leaderboard is ranking over raid-participant damage.** `RaidParticipant.TotalDamageDealt`
   already accumulates per-player damage per raid (`RaidService.HitRaidAsync` → `RecordHit`). A Gauntlet
   "score" is an aggregation of that over the event's raids (exact metric = OPEN Q8). The leaderboard
   service **reads** existing damage records into a ranked list; it does **not** introduce a second combat
   path.
2. **Legion power is already the damage source.** System 15 folds `legionPowerTerm` into `HitRaidAsync`'s
   `preProc` (`RaidService.cs` lines ~357–395). The Gauntlet adds **no new damage math** — Trophies scale
   the existing legion-power term; Wrath/Blessing are **ordinary `DamageProc` magics** that ride the
   existing applied-magic loop (`RaidService.cs` lines ~409–454). The only genuinely new combat code is
   *one multiplier* (trophies) inserted into the legion-power computation.
3. **The magic, content-provider, and gem-shop machinery is reusable wholesale.** Wrath/Blessing are
   `MagicDefinition` rows (System 14). Trophies are a tiny new content+ownership concept that mirrors
   `PlayerMagic`. The token shop mirrors `BuyMagicAsync` exactly — including the idempotency discipline —
   but spends **Gauntlet Tokens** from a **separate ledger** (OPEN Q25) instead of gems.

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

**(A) Gauntlet Trophies → multiply `rawLegionPower`** (a clean outer multiplier on the legion term),
NOT folded into `bonusFraction`:
```
trophyMult       = 1.0 + Σ ownedTrophy.LegionPowerBonusFraction        // e.g. +0.25 (+0.10)(+0.05)
rawLegionPower   = unitSum × (1.0 + bonusFraction) × trophyMult
```
Rationale (state this in the slice): DotD's trophy text is *"boosts the power of all your legions by
X%"* — a multiplier on **legion power**, which is exactly `rawLegionPower`. Folding the trophy % into
`bonusFraction` instead would be **mathematically different** (it would add to the `(1 + bonusFraction)`
factor rather than multiply the whole), and would also let a low-`PowerBonus` legion dilute the trophy.
A separate multiplier on `rawLegionPower` keeps the boost a true "+X% to my legion's power" regardless of
the legion's own bonus, and composes cleanly with `PowerScaling`. The trophy multiplier is applied
**before** `PowerScaling` so `PowerScaling` remains the single master dominance dial. **Stacking rule =
OPEN Q22** (additive Σ vs. highest-only). Because the legion term flows into `preProc`, trophies also
amplify mount/magic/unit procs that scale off the combined base — matching DotD, where bigger legion
power → bigger everything.

**(B) Wrath of the Ancients (rank 1) & Blessing of the Ancients (ranks 2–10) → existing magic
`DamageProc` loop.** They are `MagicDefinition` rows with `effectType = DamageProc`, applied to the
Gauntlet raid (OPEN Q19: via `ApplyMagicAsync`/the slot cap, or off-cap). Per the existing loop, each
rolls `procChance`; on success it adds `procAmount × preProc` and accumulates under the magic cap
(`MagicConfig.MaxAggregateProcBonus × preProc`). The DotD numbers map directly to `procChance` /
`procAmount`:
- **Wrath of the Ancients**: `procChance = 0.24`, `procAmount = 5.00` (24% chance → 500% damage). Awarded
  rank 1.
- **Blessing of the Ancients**: `procChance = 0.13`, `procAmount = 8.50` (13% chance → 850% damage).
  Awarded ranks 2–10.
- The DotD trophy-scaled riders on these magics ("+150%/+100% if you own SMITE/Blessing; +X% per trophy")
  map to `Conditions` (ownership scalars) on the magic def — but **keep/drop = OPEN Q20**. **NOTE the
  cap interaction (call out in the slice):** `procAmount = 8.50` against the default
  `MaxAggregateProcBonus` would be clamped — the Gauntlet either needs these magics exempt from the
  shared magic cap or a higher cap during events. **This is the combat-correctness landmine of Slice 4**
  (resolve under OPEN Q19 + a config decision; do NOT let an 850% proc silently clamp to the magic cap).

**(C) Score / leaderboard → reads `RaidParticipant.TotalDamageDealt`.** Because `legionPowerTerm` already
lands in `damageFinal` (System 15) and `damageFinal` is what `RecordHit` accumulates, legion power (and
trophy/Wrath/Blessing amplification) is **already** reflected in the participant damage the leaderboard
ranks. The leaderboard slice introduces **no combat changes** — it aggregates existing damage rows scoped
to the event (exact metric = OPEN Q8).

> **No parallel combat path.** The only combat-code change in this entire epic is the trophy multiplier
> on `rawLegionPower` (Slice 4) and the registration/eligibility of the two rank magics in the existing
> magic loop. Everything else is orchestration, persistence, and content.

---

## Locked design decisions (from CURRENT_TASK.md + DESIGN_NORTHSTAR.md §4 — do NOT change these)

1. **Competitive leaderboard EVENT, tightly coupled to Legion.** Legion power is the damage source; the
   Gauntlet does not introduce a separate combat system.
2. **3 level-leagues.** (Exact boundaries = OPEN Q1; DotD reference: Whelpling 20–999 / Wyrm 1000–2499 /
   Dragon 2500+.)
3. **Top-500-per-league win prizes**, with tiered awards by placement band.
4. **Currency = Gauntlet Tokens → a token shop.**
5. **"Wrath of the Ancients"** (renamed from SMITE) — **rank 1** reward — **24% chance → 500% damage**,
   with trophy-scaled riders. Near-identical mechanics to DotD; acquired by Gauntlet rank.
6. **"Blessing of the Ancients"** (renamed from Blessing of Mathala) — **ranks 2–10** reward —
   **13% chance → 850% damage**, with trophy-scaled riders. Acquired by Gauntlet rank.
7. **Gauntlet Trophies** (rank **1 / 10 / 500**) **passively boost ALL legion power by +25% / +10% / +5%**.
   Permanent account boosters (assumed; confirm Q22). Each `Max: 1`.

**Naming guard (do not collide with existing content):** `content/magics.json` *already* contains a
`magic_smite` ("Smite", 10%→60%) and `magic_blessing_of_might` ("Blessing of Might", 8%→80%) — these are
ordinary raid-drop magics and are **NOT** the Gauntlet magics. The Gauntlet magics are **new**, distinct
ids (e.g. `magic_wrath_of_the_ancients`, `magic_blessing_of_the_ancients`), rank-acquired, and
event-scoped per OPEN Q18. Do not rename or repurpose the existing two.

**Deferred (document only, do NOT build — see Deferred section):** automatic event scheduler (admin-gated
lifecycle first), Pitchfork Tokens / second currency, the "+3 loot rarity tiers" magic rider, Strikes-as-
new-currency IF the owner chooses to reuse Stamina (Q12), per-league raid scaling, season-cumulative
prize aggregation, separate Gauntlet Battalion loadout (uses the existing active legion for v1),
multi-account bracket tooling.

---

## Data model (whole epic)

*snake_case for all tables/columns/indexes; every table has `id` (UUID `gen_random_uuid()`),
`created_at`, `updated_at`, `is_deleted`; every FK indexed; private setters, no EF attributes; Fluent-only
configs in `Infrastructure/Persistence/Configurations/`. Several shapes below are gated by OPEN QUESTIONS
and are marked accordingly — do not finalize the migration for a gated entity until the question is
answered.*

### Enums (`src/ROTA.Domain/Enums/`)
- `GauntletLeague { Whelpling, Wyrm, Dragon }` — the 3 leagues (Q1 sets the level/power edges; the enum
  is stable regardless of where the edges fall).
- `GauntletEventState { Scheduled, Active, Closed, Settled }` — lifecycle (Q4–Q7).
- `GauntletRewardKind { Tokens, Trophy, Magic /*, Strikes, PitchforkTokens — deferred */ }` — what a
  prize-band entry grants.
- `GauntletTrophyTier { Aureate /*rank1, +25%*/, Argent /*rank10, +10%*/, Bronzed /*rank500, +5%*/ }`
  *(or model trophies purely as content rows keyed by id — see "Trophies as content" note; pick one in
  Slice 1).*
- `GemTransactionType` already runs to `LegionPurchase = 9`. **If Tokens reuse the gem ledger (Q25 — NOT
  recommended)** add `TokenReward`/`TokenPurchase`. **If Tokens get their own ledger (recommended)** add a
  parallel `GauntletTokenTransactionType { RankReward, RaidDefeatReward, ShopPurchase }`.

### Content models (`src/ROTA.Application/Models/`, JSON in `content/`)

**`GauntletConfig`** (appsettings, `IOptions`; safe C# defaults) — the tuning surface:
```
LeagueBounds      { Whelpling:{Min,Max}, Wyrm:{Min,Max}, Dragon:{Min,Max} }   // Q1
PrizeRankCount    int     default 500                                          // top-N per league
EventDurationHours int    default 72                                           // Q4 (if not admin-manual)
ScoreMetric       enum    // Q8 — CumulativeDamage | RaidsDefeated | Points
TieBreak          enum    // Q9
MagicCapOverride  double? // Q19 — null = use MagicConfig cap; set higher so 850% proc isn't clamped
StrikesEnabled    bool    default ?  // Q12 — gates the whole Strikes subsystem
```

**`GauntletRaidDefinition`** — *Q15: if dedicated raids.* May be a plain `RaidDefinition` with
`Tier = "Event"` (already supported) **plus** a marker that ties it to the Gauntlet (a content tag or a
`gauntlet:` id prefix), so scoring can filter "this raid counts." Add to `content/gauntlet_raids.json` (or
extend `raids.json` with `Tier:"Event"` entries + a `gauntletScored: true` flag).

**`GauntletPrizeTable`** (`content/gauntlet_prizes.json`) — per placement band → rewards:
```
bands: [ { rankFrom, rankTo, tokens, trophyId?, magicId?, /* strikes?, items? */ } ]
       // e.g. {1,1, tokens:50, trophyId:"trophy_aureate", magicId:"magic_wrath_of_the_ancients"}
       //      {2,10, tokens:25, trophyId:null,            magicId:"magic_blessing_of_the_ancients"}
       //      {11,50,tokens:20}, {51,100,tokens:15}, {101,200,tokens:10}, {201,500,tokens:5}
       // exact numbers = OPEN Q27 (DotD token table is the reference)
```

**Trophies as content** (`content/gauntlet_trophies.json`): `{ id, name, tier (GauntletTrophyTier),
legionPowerBonusFraction (0.25/0.10/0.05), iconPath }`. Stacking rule (Q22) lives in the combat read, not
the content.

**Wrath/Blessing magics**: new rows appended to `content/magics.json` (reuse `MagicDefinition` exactly):
```
{ id:"magic_wrath_of_the_ancients",   name:"Wrath of the Ancients",   rarity:"Orange",
  category:"Damage", effectType:"DamageProc", procChance:0.24, procAmount:5.00,
  conditions:[...Q20 ownership/trophy scalars...], stacks:false, gemPrice:0,
  acquisition:"Gauntlet rank 1" }
{ id:"magic_blessing_of_the_ancients", name:"Blessing of the Ancients", rarity:"Orange",
  category:"Damage", effectType:"DamageProc", procChance:0.13, procAmount:8.50,
  conditions:[...], stacks:false, gemPrice:0, acquisition:"Gauntlet rank 2-10" }
```
`gemPrice:0` ⇒ never in the gem shop (rank-acquired only). `MagicDefinitionProvider` already validates
`procChance ∈ [0,1]` and `procAmount ≥ 0` — these pass. **The 850% proc vs. the shared magic cap is the
Slice 4 landmine (Core Insight §B + Q19).**

### Entities (`src/ROTA.Domain/Entities/`) — private setters, no EF attributes, snake_case

- **`GauntletEvent`** — `Id, Name, State (GauntletEventState), StartsAt, EndsAt, SettledAt?,
  created/updated/IsDeleted`. One active at a time (Q6). Domain methods: `Create(...)`, `Activate()`,
  `Close()`, `MarkSettled()`. Migration: `AddGauntletEvent`.
- **`GauntletEntry`** — a player's standing in an event. `Id, GauntletEventId (FK, idx),
  PlayerId (FK, idx), League (GauntletLeague), Score (long — Q8), TieBreakKey (Q9 — e.g. last-progress
  `DateTimeOffset` or a derived long), LastRank (int? — settled/snapshot rank),
  created/updated/IsDeleted`. Unique `(gauntlet_event_id, player_id)`. League stored if locked at entry,
  else recomputed (Q3). Domain methods: `Create(...)`, `AddScore(long, DateTimeOffset)`,
  `SetRank(int)`. Migration: `AddGauntletEntry`.
  - *Scoring source:* `Score` is **derived from / kept in sync with** `RaidParticipant` damage on the
    event's raids (Q8). Whether `Score` is a denormalized running total updated per qualifying hit, or
    computed on read by summing participant rows, is a Slice-3 decision — but **either way the authority
    is the participant damage already written by `HitRaidAsync`** (do not invent a second damage write).
- **`PlayerGauntletTrophy`** — permanent ownership of a trophy. `Id, PlayerId (FK, idx),
  GauntletTrophyId (string, or tier enum), created/updated/IsDeleted`. Unique `(player_id,
  gauntlet_trophy_id)` (each `Max:1`). Mirrors `PlayerMagic`. Domain: `Create(...)`, `Restore()`.
  Migration: `AddGauntletTrophy`. **Combat reads these every Gauntlet hit (and arguably every raid hit,
  since trophies boost ALL legion power — Q: do trophies apply outside Gauntlet raids too? They say "all
  your legions", implying always-on → fold into the Slice-4 legion read unconditionally; confirm under
  Q22/Q23).**
- **`GauntletRaidLink`** *(Q15/Q16 — only if needed)* — if existing `active_raid` rows must be tagged to
  an event, add a nullable `gauntlet_event_id` column to `active_raid` (migration `AddGauntletRaidLink`)
  rather than a join table, so `HitRaidAsync` can stamp the event id at summon and scoring can filter by
  it. (Cleaner than re-deriving "is this raid a Gauntlet raid" from the definition each time.)
- **`PlayerEventMagic`** *(Q18 — only if Wrath/Blessing are per-event consumables)* — event-scoped magic
  ownership with removal at reset: `Id, PlayerId, GauntletEventId, MagicDefinitionId,
  created/updated/IsDeleted`. If Wrath/Blessing are **permanent** (Q18 = b), this entity is unnecessary —
  reuse `PlayerMagic`. **Do not build this until Q18 is answered.**
- **Token ledger** *(Q25 — recommended path)*: **`GauntletTokenTransaction`** — append-only, mirrors
  `GemTransaction`: `Id, PlayerId (FK, idx), Amount (int, +credit/−debit),
  TransactionType (GauntletTokenTransactionType), ReferenceId (string?, idempotency),
  created_at`. Balance = `SUM(amount)`. Unique partial index on `(player_id, transaction_type,
  reference_id)` (idempotency, mirrors the gem ledger). Migration: `AddGauntletTokenLedger`.
- **Strikes** *(Q12 — only if Strikes are a new currency)*: extend `ResourceType` with `Strikes` and add
  a `PlayerResource` row of that type (regen via `ClassConfig`/a new config). If Q12 = reuse Stamina,
  **no new entity** — Gauntlet hits spend Stamina like any raid.

### Config: `GauntletConfig` (appsettings, `IOptions`) — see Content models above.

---

## Scoring / ranking formula

*(Exact metric, tiebreak, league edges, and rank-read latency are OPEN Q8/Q9/Q1/Q11 — the structure below
holds regardless of which option is chosen.)*

```
For each active GauntletEvent E:
  eligible raids    = active_raid rows where gauntlet_event_id = E.Id           // Q15 link
                      (or: raid-definition tagged gauntletScored, summoned during [E.Start, E.End])
  per-player score  = aggregate over the player's RaidParticipant rows on eligible raids   // Q8:
                        CumulativeDamage → Σ TotalDamageDealt
                        RaidsDefeated    → count of defeated eligible raids the player hit
                        Points           → a defined formula
  league(player)    = band(player.Level)  [locked at entry OR live — Q3]         // Q1 edges
  rank within league= ORDER BY score DESC, tieBreakKey ASC                        // Q9
  prize(rank)       = GauntletPrizeTable band containing rank (top PrizeRankCount only)  // Q27
```

**Where the combat amplifiers enter the score (already, for free):**
`damageFinal` (the value `RecordHit` accumulates into `TotalDamageDealt`) already includes —
- the legion-power term (`legionPowerTerm`), **× the Gauntlet Trophy multiplier** (Slice 4 change A),
- the Wrath/Blessing `DamageProc` bonuses when those magics are active on the raid (Core Insight §B),
- crit, mount procs, unit procs, magic procs — all of which scale off `preProc = charBase +
  legionPowerTerm`, so they too are amplified by trophies.

⇒ **A higher-ranked player's trophies and rank-magics make every hit score more, which is exactly the
intended "grind → rank → bigger hits → rank higher" flywheel.** No score-side code computes any of this;
it is the natural consequence of those effects landing in `damageFinal` upstream.

**Attachment summary (precise, per the locked numbers):**

| Effect | DotD source | ROTA attach point | Value |
|---|---|---|---|
| Wrath of the Ancients (rank 1) | SMITE 24%→500% | existing magic `DamageProc` loop on the Gauntlet raid | `procChance 0.24`, `procAmount 5.00` |
| Blessing of the Ancients (ranks 2–10) | Blessing of Mathala 13%→850% | same magic loop | `procChance 0.13`, `procAmount 8.50` |
| Trophy Aureate (rank 1) | +25% all legion power | `rawLegionPower ×= (1 + 0.25 …)` **before** `PowerScaling` | `+0.25` |
| Trophy Argent (rank 10) | +10% all legion power | same multiplier | `+0.10` |
| Trophy Bronzed (rank 500) | +5% all legion power | same multiplier | `+0.05` |

(Trophy stacking when a player owns several = OPEN Q22; the 850% proc vs. magic cap = OPEN Q19 +
`MagicCapOverride`.)

---

## SLICE 1 — Gauntlet content + definitions  *(additive · LIGHT)*

- Enums: `GauntletLeague`, `GauntletEventState`, `GauntletRewardKind`, `GauntletTrophyTier` (and the token
  transaction-type enum per Q25).
- Models: `GauntletConfig`, `GauntletPrizeTable` (+ band shape), trophy content model, and the **two new
  magic rows** (`magic_wrath_of_the_ancients`, `magic_blessing_of_the_ancients`) appended to
  `content/magics.json`.
- `content/gauntlet_prizes.json` + `content/gauntlet_trophies.json` (+ `content/gauntlet_raids.json` if
  Q15 = dedicated).
- `IGauntletContentProvider` (singleton; pattern = `MagicDefinitionProvider`; startup validation: unique
  ids; prize bands non-overlapping and cover 1..PrizeRankCount; every `trophyId`/`magicId` referenced by a
  band resolves; trophy `legionPowerBonusFraction` ≥ 0; league bounds non-overlapping and ordered).
- Register provider + `GauntletConfig` from appsettings.
- **Naming guard test:** assert the new magic ids do **not** equal `magic_smite` / `magic_blessing_of_might`.
- Tests: content loads; duplicate id throws; overlapping prize bands throw; band references a missing
  trophy/magic → throws; the two Gauntlet magics load with `procChance 0.24/0.13` and `procAmount
  5.00/8.50` and `gemPrice 0`.
- **Acceptance:** providers load and validate; build 0 warnings; tests green. **Commit independently.**
- **Review depth:** LIGHT (content/validation only).

## SLICE 2 — Event + entry ownership/state + admin lifecycle  *(additive + migration · MODERATE)*

- Entities: `GauntletEvent`, `GauntletEntry` (+ token ledger entity per Q25; + `gauntlet_event_id` column
  on `active_raid` per Q15) + Fluent configs (snake_case, unique indexes, FK indexes); migrations
  `AddGauntletEvent`, `AddGauntletEntry` (+ `AddGauntletTokenLedger`, `AddGauntletRaidLink` as needed).
  `DbSet`s. **Do NOT run `dotnet ef database update`.**
- Repositories: `IGauntletEventRepository` (GetActive, FindById, Create, Update — enforce ≤1 active),
  `IGauntletEntryRepository` (FindByEventAndPlayer, GetForEvent, Upsert), and the token ledger repo
  (`GetBalanceAsync`, `CreateAsync`, `ReferenceExistsAsync` — mirror `IGemTransactionRepository`).
- `IGauntletService` (initial): `GetCurrentEventAsync`, `JoinEventAsync(playerId)` (creates the
  `GauntletEntry`, assigns league per Q1/Q3), `GetMyEntryAsync`.
- **Admin lifecycle (Q5/Q7):** `IGauntletAdminService` + admin endpoints `[AdminOnly]`:
  `POST /api/admin/gauntlet/events` (open), `POST /api/admin/gauntlet/events/{id}/close`,
  `POST /api/admin/gauntlet/events/{id}/settle` (idempotent — Slice 5 fills in settlement payout; here it
  just transitions state). CLI hooks (`gauntlet-open` / `gauntlet-close` / `gauntlet-settle`) mirroring the
  existing `AdminCli` pattern.
- `GauntletController` `[Authorize]`: `GET /api/gauntlet` (current event + my entry/league),
  `POST /api/gauntlet/join`.
- DTOs: `GauntletEventResponse`, `GauntletEntryResponse`, token-balance DTO.
- Tests: open event (≤1 active enforced); join assigns correct league at each band edge (Q1 boundaries);
  double-join is idempotent; close/settle state transitions; token ledger balance = SUM, idempotent ref.
- **Acceptance:** an admin can open an event, a player can join and is placed in the right league, state
  transitions are guarded. **Commit independently.**
- **Review depth:** MODERATE (state machine + ≤1-active guard + league-edge correctness).

## SLICE 3 — Leaderboard / scoring  *(read-aggregation · MODERATE)*

- Decide score storage (denormalized running total on `GauntletEntry` vs. computed-on-read from
  `RaidParticipant`) per Q8 — **authority stays the participant damage written by `HitRaidAsync`.**
- `IGauntletScoringService` (or extend `IGauntletService`):
  `GetLeaderboardAsync(eventId, league, page)` → ranked top-`PrizeRankCount` + caller's own rank/score;
  `RecomputeRanksAsync(eventId)` (snapshot ranks into `GauntletEntry.LastRank`, used by settlement and by
  the cached read per Q11). Ranking = `ORDER BY score DESC, tieBreakKey ASC` within league (Q9).
- **Score-update hook:** when a Gauntlet-eligible raid hit lands, the player's `GauntletEntry.Score` (and
  `TieBreakKey`) updates from the participant total. Wire this where the participant damage is recorded
  for an event-linked raid (see Slice 4 integration) — but the **leaderboard read itself is pure
  aggregation, no combat.**
- Endpoint: `GET /api/gauntlet/leaderboard?league=` (`[Authorize]`) → page + `YourRank`/`YourScore`.
- Redis caching of the ranked page per Q11 (optional for v1 if Postgres `ORDER BY` is fast enough at beta
  scale — note the decision).
- Tests: ranking order by score then tiebreak; caller-rank correctness when outside top-N; empty league;
  league isolation (a Wyrm never appears in the Whelpling board); tie resolved deterministically.
- **Acceptance:** leaderboard returns correct per-league ranks and the caller's own standing. **Commit
  independently.**
- **Review depth:** MODERATE (ranking correctness + tiebreak determinism + league isolation).

## SLICE 4 — Combat integration  *(DEEP / combat-money path)*

**This is the deep / money-path review slice.** It touches `HitRaidAsync` and the score that drives all
prizes. The whole point is to add the Gauntlet amplifiers **without forking the combat path** and
**without breaking** existing mount/magic/unit/crit assertions.

- **Trophy multiplier (the one true combat change):** inject `IPlayerGauntletTrophyRepository` (+ the
  trophy content provider) into `RaidService` (scoped). Inside the advisory-lock callback, in the
  legion-power block, after `rawLegionPower = unitSum × (1 + bonusFraction)` and **before** applying
  `PowerScaling`:
  ```
  trophyMult     = 1.0 + ΣtrophyFraction(owned trophies)      // Q22 stacking: additive Σ OR max-only
  rawLegionPower = rawLegionPower × trophyMult
  ```
  Apply unconditionally to legion power (trophies say "all your legions" → always-on, not Gauntlet-only)
  **unless Q23 restricts it to Gauntlet raids.** `PowerScaling` stays the master dial applied after.
- **Wrath/Blessing magics:** ensure the two rank magics participate in the existing applied-magic
  `DamageProc` loop on Gauntlet raids (via `ApplyMagicAsync` + the slot cap, or off-cap auto-apply for the
  owner — Q19). **Resolve the cap landmine:** if `MagicCapOverride` is set for the event, use it instead
  of `MagicConfig.MaxAggregateProcBonus` so `procAmount 8.50` is not silently clamped. **Document and test
  the cap behavior explicitly.**
- **Event linkage + score update:** when a hit lands on a raid with `gauntlet_event_id` set (Q15), update
  the player's `GauntletEntry.Score`/`TieBreakKey` from the participant total (Slice 3 hook). No second
  damage computation — read the participant row that `HitRaidAsync` already wrote.
- **No regression:** existing `RaidService` tests (mount proc, magic procs, unit procs, crit,
  contribution tiers, idempotency) must still pass unchanged. Trophy/Wrath/Blessing effects are **additive
  to the existing formula**, gated on ownership/event, so a player with no trophies and no rank magics
  hits **identically** to today.
- Tests (seeded RNG, mocked repos): no trophies → legion term unchanged; one Aureate trophy → legion term
  ×1.25 (and downstream procs scale accordingly); trophy stacking per Q22; Wrath proc fires/doesn't at
  24%/500% and lands in `damageFinal`; Blessing 13%/850% with `MagicCapOverride` is **not** clamped (and
  IS clamped without it, proving the override matters); Gauntlet hit updates `GauntletEntry.Score`;
  non-Gauntlet hit does not; all prior combat assertions still green.
- **Acceptance:** trophies and rank magics amplify damage exactly per the locked numbers, the score
  reflects it, and no existing combat behavior changes for players without Gauntlet items. **Commit
  independently.**
- **Review depth:** DEEP (formula order — trophy before `PowerScaling`; the 850% cap interaction; no
  double-count with mount/magic/unit; score authority = the single participant write; no parallel path).

## SLICE 5 — Rewards / settlement + Trophy & rank-magic grants  *(MODERATE)*

- **Settlement (`settle` from Slice 2, now with payout):** `IGauntletAdminService.SettleEventAsync(eventId)`
  — **idempotent and re-runnable.** Steps: snapshot ranks (`RecomputeRanksAsync`), then for each league,
  for each entry in rank order within `PrizeRankCount`, look up the prize band (Q27) and grant:
  - **Gauntlet Tokens** → credit the token ledger with an **idempotent referenceId**
    (`gauntletsettle:{eventId}:{playerId}:tokens`) so re-running settle never double-pays.
  - **Trophy** (bands 1 / 10 / 500) → `PlayerGauntletTrophy` upsert (idempotent; `Max:1`).
  - **Rank magic** → grant Wrath (rank 1) / Blessing (ranks 2–10) via `GrantMagicAsync` (permanent) **or**
    an event-scoped grant with removal at next event (Q18). Idempotent either way.
  - Mark `GauntletEvent.MarkSettled()` only after all grants commit; re-running on an already-settled
    event re-verifies grants (idempotent) and is a no-op for already-granted prizes.
- **Per-raid-defeat reward loop (DotD: +10 Strikes + 1 Token per raid defeated)** — wire into the Gauntlet
  raid kill path: on defeating an eligible raid, credit **1 Gauntlet Token** (idempotent referenceId
  `gauntletraid:{activeRaidId}:{playerId}`) and, if Strikes are in (Q12/Q13), refund Strikes. (If Strikes
  reuse Stamina, only the token credit applies.)
- DTOs: settlement summary; `GauntletRewardResponse`.
- Tests: settle grants tokens/trophy/magic to the right rank bands; **settle-twice pays once**
  (idempotency — the magic money-bug class); rank-1 gets Wrath + Aureate + tokens; ranks 2–10 get Blessing
  + tokens (no trophy); rank 500 gets Bronzed; ranks 11–499 get tokens only; raid-defeat credits exactly 1
  token and is idempotent on duplicate kill processing.
- **Acceptance:** closing+settling an event distributes exactly the locked prizes, idempotently. **Commit
  independently.**
- **Review depth:** MODERATE→DEEP-ish (idempotent settlement is the correctness point — same class as the
  magic/gem buy money-bug; a botched re-run must not double-pay).

## SLICE 6 — Token economy / shop  *(MODERATE)*

- `content/gauntlet_shop.json` — the token-shop catalogue (Q24): `{ id, rewardKind, payloadId, tokenPrice,
  ... }`. Validated at startup by the content provider.
- `IGauntletService.BuyFromShopAsync(playerId, shopEntryId)` — **mirror `BuyMagicAsync` exactly,
  including its idempotency discipline**, but spend **Gauntlet Tokens** from the token ledger (Q25), not
  gems:
  - ownership/eligibility pre-check first (reject `AlreadyOwned` for one-per-account items **without
    charging**),
  - spend tokens with an idempotent `referenceId` (`gauntletshop:{playerId}:{shopEntryId}`),
  - then grant the payload (unit/legion/item/gem-bundle/Strike-refill per catalogue).
  - **Heed the known shop money-bug:** the current `SpendGemsAsync` returns `false` for BOTH
    "insufficient" and "already-charged" (see `CURRENT_TASK.md` open item). The token ledger's spend
    **must distinguish** "insufficient balance" from "already charged" (tri-state result, or rely on the
    ownership pre-check the way `BuyMagicAsync` documents) so a charged-but-not-granted retry does not lose
    the purchase. **Do NOT repeat the magic money-bug.**
- Endpoints: `GET /api/gauntlet/shop` (catalogue + token balance), `POST /api/gauntlet/shop/{entryId}/buy`.
- DTOs: `GauntletShopEntryResponse`, `BuyShopResult` (with `AlreadyOwned`/`InsufficientTokens` codes).
- Tests: buy success debits tokens and grants payload; insufficient tokens → no charge; **buy-twice
  charges once** (idempotency); already-owned one-per-account item → `AlreadyOwned` without charge;
  catalogue lists with live token balance.
- **Acceptance:** tokens earned from rank/raids can be spent in the shop, idempotently, with no
  double-charge. **Commit independently.**
- **Review depth:** MODERATE (economy idempotency — same class as the magic/gem money bug; the tri-state
  spend is the explicit fix).

---

## Deferred items (document, do NOT build this epic)

- **Automatic event scheduler / cadence engine.** v1 lifecycle is admin-gated (open/close/settle via
  CLI + admin endpoints). A cron/Quartz-style auto-cadence (e.g. weekly events) is Phase 2+.
- **Strikes as a distinct buyable currency** — IF Q12 chooses to reuse Stamina for v1, the full Strikes
  subsystem (new `ResourceType`, regen, premium purchase) is deferred.
- **Pitchfork Tokens / any second Gauntlet currency** (DotD awarded these to top-500). Out for v1 by
  currency discipline (North Star §3).
- **The "+3 to all loot rarity tiers" rider** on Wrath/Blessing — does not map to ROTA's threshold/chance
  loot model; deferred/dropped (Q21).
- **Separate Gauntlet Battalion loadout with paid expansion slots** (DotD). v1 uses the player's existing
  **active legion**; a dedicated Gauntlet loadout is Phase 2+ (depends on the fuller collection/fodder
  layer, North Star §5).
- **Per-league raid scaling / per-league boss rosters**, season-cumulative (cross-event) prize
  aggregation, and multi-account bracket tooling.
- **Real-time leaderboard push** (SignalR) — v1 is pull (`GET …/leaderboard`); SignalR hubs are not yet
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
- Every state change writes to `audit_log` (event open/close/settle, join, prize grants, shop buys).
- **Do NOT run `dotnet ef database update`.** Build **0 warnings**; **all tests green** before committing a
  slice. Update `docs/PROJECT_STATE.md` count + `docs/ROTA_Function_Reference.md` as you go.
- **No co-author trailer.** **One branch + one merge + one tag per slice; never bundle.** Do **not** push
  until the owner says so. Auditor reviews after a batch (DEEP review mandatory on Slice 4; settlement
  idempotency on Slice 5; shop idempotency on Slice 6).
