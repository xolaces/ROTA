# System 22 — The Ancients: The Rise (collective) + Masteries (individual)

*Draft spec, 2026-06-07. Grounded in three research passes (meta-events, breadth-rating systems,
PoE-depth progression) + owner decisions in-session (all 4 open decisions resolved — see §"Resolved
decisions"). Makes the game title literal: the Ancients become a real mechanic. Phased so a viable
core ships first and PoE-depth layers in over later content updates.*

---

## 0. One fiction, two systems

The Ancients are dormant primordial powers. Two systems hang off them, sharing one fiction:

- **The Rise** — the *server* collectively stirs an Ancient awake (collective pillar; the "Rise of
  the Ancients" made literal). §B.
- **Masteries** — a *player* deepens devotion to an Ancient for a small permanent bonus (individual
  identity/power). §A.

**The interlock:** all 4 launch Ancients are pledgeable from day 1 (so Masteries work immediately on a
fresh server). The Rise is the recurring server event that periodically wakes an Ancient (for a
community raid + rewards) and is the vehicle for introducing *new* Ancients (5th, 6th…) over the game's
lifetime.

---

# PART A — MASTERIES

## A1. Locked model (owner-confirmed)

> **Masteries are small bonus *modifiers*, never mechanic-changers.** Every mastery level grants a
> permanent **global** buff (always on, even when not your active mastery). Your **one pledged
> (active)** mastery has its buff **amplified** — pledging an Ancient roughly **doubles** its bonus.
> All values are **single-digit %**, tuned to sit inside ROTA's global power cap.

Each mastery therefore has two stacked components:

| Component | When it applies | Scales with |
|---|---|---|
| **Global** (passive) | always — regardless of which mastery is active | that mastery's **level** (1–5) |
| **Pledge** (active focus) | only on your **one** currently-pledged mastery | a bonus that ≈ **doubles** that Ancient's bonus at its current level |

This is the heart of "you are never punished for specializing": leveling *any* Ancient helps you
*globally and permanently*; pledging one only sharpens it. A generalist who has leveled all four
carries four global buffs at once; a specialist carries fewer-but-deeper plus a bigger pledge bonus.
Neither loses.

**Rule of thumb: pledging an Ancient roughly *doubles* its bonus.** That makes your active choice
genuinely meaningful while every leveled Ancient still pays out globally, forever — and re-pledging is
lossless (§A5), so you flex which one is "lit" without ever forfeiting the others.

## A2. The four Ancients (owner-confirmed domains + magnitudes)

Each owns a **different pillar** (orthogonal — no single sim can rank them against each other; the
structural defence against the WoW-Shadowlands "one optimal pick" trap). Magnitudes below are the
owner-raised starting values, still **TUNE** dials; the shape is locked.

| Ancient (Mastery) | Theme | Buff (modifier only) | Global @ L5 | Pledged @ L5 (≈ ×2) | Leveled by |
|---|---|---|---|---|---|
| **Wrath** — the Wrathfire | rage / war | **+% Legion power** | +2.5% | ~5% | legion/raid combat activity |
| **Bulwark** — the Mountain | guild / defence | **+% guild-raid damage** (guild raids only) | +0.5% | ~1% (**hard cap** — it's a direct combat %) | guild-raid contribution |
| **Hoard** — the Greed | plunder | **+% global drop rate / gold** | +4% | ~8% | quests/activity completed |
| **Discernment** — the Veiled Eye | sight / discovery | **+% drop *quality* (rarity-upgrade) + sigil find** | +4% | ~8% | breadth of content cleared |

Notes:
- **Wrath does NOT touch the Gauntlet directly** (owner call — the dedicated "Wrath Gauntlet" idea is
  **cut**). It only nudges Legion power; even at ~5% pledged it's a minor slice of a hit (legion is
  ~50–70% of damage), available to everyone in a league band, so the Gauntlet ladder stays a near-pure
  measure of account power.
- **Bulwark is the deliberate exception — hard-capped at ~1% guild-raid damage** (owner number),
  scoped to guild raids only. A direct combat % is potent per point, so it stays tiny; the other three
  (drops/quality/legion) are mostly horizontal, so they can be more generous.
- **Hoard vs Discernment** are distinct modifiers: Hoard = *more* drops (quantity/gold), Discernment =
  *better* drops (rarity/quality) + sigil find. The literal "discover hidden nodes / secret bosses"
  flavour for Discernment is a **deferred content layer** (§C), not a v1 mechanic-changer.
- These globals stack across *different lanes* (drop-rate, drop-quality, legion, guild) — never into one
  runaway number — so a fully-maxed account stays within the capped-power spirit.

## A3. Leveling 1→5 (challenge checklists, not flat XP)

Each Ancient levels **1→5 over ~3–6 months** of real play (longer-tenured players naturally have
more). Tiers advance via a **per-Ancient challenge checklist** of deterministic activity counters,
not a flat XP bar (PoE-challenge-league lesson; avoids the D4-Paragon "grind with no shape").

- **Graduated breadth curve:** early tiers (1→2, 2→3) fall out of *normal play* so everyone progresses;
  late tiers (3→4, **4→5**) demand **deliberate, cross-system effort** (e.g. a raid milestone **and** a
  guild contribution **and** a Gauntlet placement). This makes L5 a months-long flex and paces it to
  the 3–6-month target.
- **Deterministic counters, round numbers, visible "X / Y"** (e.g. "Hoard T4: 320/500 quests
  cleared"). Prefer counts over RNG goals; reserve any luck-flavoured goals for the optional tail.

## A4. Overall Mastery Rating (the breadth lever)

A single account-wide rating from all four mastery levels, designed so **breadth pays** while a
**specialist is never punished** (owner ask: "make the mastery rating of all meaningful… swap between
them as they level up, fated by levels of all"). Research recommendation = **Formula B** (additive base
+ breadth thresholds + depth bonus), math-verified monotonic so more is always more.

```
Let Mᵢ ∈ {1..5} be the four mastery levels.

Rating = Σ Mᵢ                                   (raw, 4..20)
       + breadth thresholds:
            +3   if all four ≥ 2                 ("Touched everything")
            +5   if all four ≥ 3                 ("Well-rounded")
            +8   if all four ≥ 4
            +12  if all four ≥ 5                 ("Ascendant" — the jackpot)
       + depth bonus:  +2 per mastery at level 5   (specialists rewarded too)
       [+ optional weakest-pillar floor: + 2 × min(Mᵢ)  — nudges "swap to your weakest"]
```

Worked: Specialist (5,1,1,1)=10 · Generalist (3,3,3,2)=14 · maxed (5,5,5,5)=56. Breadth beats a same-
total specialist; the all-≥5 jackpot is the clear ceiling; every level always *adds*.

- **Active vs Lifetime split** (Destiny Triumph pattern): *Active* is computed live from current levels
  (drives unlocks); *Lifetime* is a monotonic high-water mark — the tenure marker on the leaderboard.
- **Teeth — gate access / QoL / identity, NEVER raw competitive power** (Warframe-MR principle):
  **titles** at each breadth threshold + a specialist title (`Master of <Ancient>`); **QoL** per
  threshold; a small **hard-capped "Renown-style" micro-bonus** at top thresholds, inside the global
  cap, earned by play only (paywall-proof, non-runaway).
- **Non-coercion rule:** every threshold is a *carrot beside* the game, never a *wall in front* of a
  pillar's core loop.

## A5. Re-spec economy (owner-confirmed)

- **Free swap once per month.** (Generous floor — the anchor that makes paid purely optional.)
- **Paid swap with gems, capped once per week**, sold in the **Bazaar**. **Flat, predictable gem cost
  — never scaling with power** (PoE "respec-hell" anti-pattern avoided).
- **A free swap every time a new Ancient is awakened.**
- **Swapping is LOSSLESS** — it only changes which mastery is *pledged*; all four leveled tracks stay
  banked exactly where activity left them (the #1 Shadowlands sin — destroyed progress — never broken).

## A6. Why specializing never feels like a loss (the guarantees)

1. **Leveling any mastery is a permanent global buff** — kept whether or not it's pledged.
2. **Horizontal, inside the cap** — masteries change *how much you get from* a pillar, not your ceiling.
3. **Every pillar's content is clearable by anyone** — a mastery is never a gate or a benching risk.
4. **All tracks bank; swaps are lossless & cheap** — pledging one never forfeits the others.
5. **Both breadth and depth have titles/rewards** — generalist *and* specialist each have a flex.

---

# PART B — THE RISE + THE ANCIENT RAID

## B1. Two-phase lifecycle

**Phase 1 — Charge (~1–2 months, owner number).** A **server-wide Awakening meter** fills from
*normal play* — no new grind. Every existing combat action contributes "Awakening Essence" (raid hits
scaled by hit size 1/5/20; quest/boss clears; Gauntlet strikes; guild-raid contribution). Show a
server progress bar + ETA. Scale the meter target with active population so a **healthy server lands
the awakening in the ~1–2-month window**. **No contribution cap** (owner) — heavy spenders/players can
*accelerate* the awakening, which is pro-social (everyone's Rise comes sooner; see B4). Keep a **soft
minimum window (~2 weeks)** so there's always a charge phase casuals can join before it wakes.

**Phase 2 — The Ancient Walks (raid window, ~7–10 days).** On awakening, the Ancient spawns as a
**server-shared world-raid in its own category** with a huge HP pool. The **community damage ladder**
goes live. Window ends at the earlier of the time limit or the final tier; rewards settle at close.

## B2. Two stacked reward axes (Elite-Dangerous model)

> **Final reward = Community-Tier bundle (set by *aggregate* server damage) × Personal-Bracket
> multiplier (set by *your share* of damage).**

**Axis 1 — Community Damage Ladder (lifts EVERYONE equally).** As aggregate server damage crosses
thresholds, it permanently raises the reward tier that **every qualifying participant** receives — a
casual who landed 3 hits and a whale unlock the *same* community bundle. Template (thresholds as % of
the Ancient's max HP so it auto-scales; bundles illustrative):

| Tier | Aggregate dmg | Server-wide bundle for *all* participants (pre personal multiplier) |
|---|---|---|
| **T1 Stirred** | 15% | base gold+XP; 1× Ancient Sigil; small gem trickle (Rare+ only) |
| **T2 Roused** | 30% | T1 + larger gold/XP; 1× Ancient material |
| **T3 Enraged** | 50% | T2 + gem bump; guaranteed Blue Ancient gear |
| **T4 Sundered** | 70% | T3 + gear upgraded to Purple; bonus stat-bag |
| **T5 Vanquished** | 90% | T4 + gear → Orange (ceiling); bonus gems |
| **T6 Annihilated** | 100% (kill) | T5 + **server-first Orange "Ancient" cosmetic/title for everyone** + gem top-up (biggest jump) |

Pacing: space thresholds so T1–T3 clear in the first days and T4–T6 need a late-window push (the "can
we finish it?" crescendo). If T6 isn't reached, everyone keeps whatever tier *was* crossed.

**Axis 2 — Personal Contribution Bracket** (reuse + extend the existing
Legendary/Epic/Rare/Participant engine to ED's percentile shape):

| Bracket | Who | Multiplier on the community bundle |
|---|---|---|
| **Ancient's Chosen** | Top 10 players | ×1.50 + exclusive title/cosmetic |
| **Legendary** | Top 25% | ×1.25 |
| **Epic** | Top 50% | ×1.10 |
| **Rare** | Top 75% | ×1.00 |
| **Participant** | everyone with ≥ floor | ×0.50 |

Keep **gems gated to Rare+** (existing rule); the community bundle flows all the way to Participant.

## B3. Participation floor (one line, ED-derived)

> **Any player who registers ≥1 valid hit on the Ancient (or ≥1 essence during Charge) is a
> Participant and receives the full community-tier bundle the server unlocked, at ×0.50 — guaranteed,
> regardless of rank.**

## B4. Fairness *without* caps (owner — no whale cap)

Owner decision: **no daily contribution cap, either phase.** Heavy spenders and heavy-active players
keep fuelling the Rise uncapped — in a capped-power game this is the point: it gives whaling/grinding a
*purpose beyond self-improvement*. Your excess pours into the **community ladder**, pulling the whole
server up a tier. Casuals are protected not by capping whales but by structure:

- **The community tier pays everyone equally** (Axis 1) — a whale who solos 40% of the boss *raises*
  the community bundle for all; they can never take it *away* from anyone.
- **The participation floor** (B3): ≥1 hit = the full community bundle at ×0.50, regardless of rank.
- **Personal bracket multipliers stay bounded** (×0.50 → ×1.50): uncapped *contribution* lets a whale
  reliably claim **Ancient's Chosen** (Top-10 title + cosmetic + ×1.50) and *power the server*, but the
  ×1.50 ceiling means their personal *take* never balloons proportionally — the prestige is the reward,
  the community lift is the point.
- **Multi-action credit** (FFXIV FATE lesson): contribution accrues from *all* combat, so casual/low-
  level players still contribute through whatever they already play.

Net: the Top-10 race becomes a genuine whale/hardcore **prestige** competition (good — the
self-expression the grind earns), and it never shuts a casual out of real Ancient loot.

## B5. Commemoration (three layers — AQ only nailed the top one)

1. **Server-first record** — the cohort/guild landing the killing blow: unique title + cosmetic, on the
   timeline. **Awardable every Rise** (not a one-shot like Scarab Lord).
2. **Participation badge** for anyone over a threshold — a dated "Awakener of <Ancient>" flair.
3. **The permanent unlock itself** — for everyone, retroactively claimable by latecomers.
4. **A Hall of the Ancients** — in-world monument/timeline of every Rise, its date, and record-holders.

## B6. No-FOMO guarantee (non-negotiable for a no-reset game)

Participation is **permanent & repeatable** — never sealed like AQ's gates. A latecomer contributes to
the *current* Ancient and still inherits every past unlock; only the **dated server-first record** is
exclusive. Mid-cycle joiners get a small catch-up bundle (Diablo "Haedrig's Gift" pattern), never
touching permanent power.

## B7. Categorization & data model (gate-don't-fork — your proven pattern)

- **`ActiveRaid.Tier = "Ancient"`** + a nullable **`ActiveRaid.AncientEventId`** fork (mirrors
  `GauntletEventId` / `GuildId`). Combat amplifier/scoring gate on it being non-null; **base
  `HitRaidAsync` is untouched — no parallel combat path** (consistent with System 16 / 21).
- **`AncientEvent`** server-singleton-per-cycle: `AwakeningProgress`, `AwakeningTarget`,
  `Phase {Charging, Active, Settled}`, `AggregateDamage`, `CommunityTierReached`, `RaidWindowEndsAt`,
  `ParticipantCount` (denormalized, O(1)).
- **Append-only ledgers** (house style): `AncientContributionTransaction` (per-player essence during
  Charge); reuse `RaidParticipant` for raid-window damage. **Idempotent settlement** via unique index
  `(ancient_event_id, player_id)` — copy the Gauntlet settlement exactly.
- **No daily-cap counters needed** (per B4) — contribution is uncapped; still track per-player running
  totals (for live bracket display) and the server aggregate (for the community ladder + ETA).
- **Server-driven** via CLI `ancient-open` / `ancient-close` / `ancient-settle` (mirrors
  `gauntlet-open/close/settle`). Players don't summon it; the awakening does.
- **Content** `content/ancient_raids.json`, `Tier="Ancient"`, one boss per cycle.
- Reuses the **leaderboard service (System 17)** for the Ancient board; audited on settle.

---

# PART C — PoE-DEPTH (deferred content layers — your long runway)

Governing rule (PoE vs D4-Paragon): **in a capped, no-reset game, depth must come from decisions and
completion %, not power.** Each layer is a *menu of meaningful choices* + a *visible completion target*,
addable one at a time after the core proves out — the "good time for adding content" the owner asked for.

1. **Per-tier "pick ONE of N" mastery choice** (PoE Mastery-node model) — **deferred to Phase C per
   owner** (v1 stays simple flat modifiers). Each tier-up offers ~4–6 curated modifier options; you
   lock one. A **cross-Ancient "crossed-off" rule** (taking an option on one Ancient removes it from
   another) pushes breadth. This is the first depth layer to add — it turns a tier from "a number" into
   "*yours*." Options stay single-digit modifiers, never mechanic-changers.
2. **Per-Ancient stat-tree** ("ROTA's Atlas"): a small (~30–40 node), **freely-respeccable**
   constellation per Ancient using node / **Notable** / **Keystone** vocabulary, **affinity-gated** for
   a legible ramp, with **1–2 trade-off Keystones** each. Identity sticky; allocation loose. Horizontal
   (drop-weighting / regen flavour / cosmetic procs), never raw power.
3. **Mastery-gated gear enchantment** (PoE Labyrinth / Divine-Font model): a **slot ladder** — Tier 2
   unlocks enchant slot A, Tier 3/4 add more, **Tier 5 unlocks the highest-quality enchants + an extra
   use**. **Deterministic choose-your-enchant** (no slot-machine), framed as specialization/QoL, with
   the single build-defining enchant gated to L5.
4. **All-four capstone** — a cosmetic "Ascendant of the Ancients" title/badge at all-four threshold.
5. **Discernment's literal Discovery layer** — hidden quest nodes / secret bosses / bonus sigil sites
   revealed by Discernment tier (the explorer fantasy, as *content*).
6. **Rotating seasonal cosmetic-challenge layer** on top of the permanent spine — never resetting it.

---

## Phasing / slice plan

**Phase A — Mastery core (viable v1).** 4 Ancients · global+pledge flat modifiers · challenge-checklist
leveling · Overall Mastery Rating (Formula B, Active+Lifetime) · titles + capped micro-bonus ·
re-spec economy. Ships the months-long horizontal progression spine.

**Phase B — The Rise + Ancient Raid.** `AncientEvent` · Charge meter (no cap, soft-min window) ·
`Tier="Ancient"` raid fork · community ladder + personal brackets · participation floor · commemoration
+ Hall · CLI. Makes the title literal.

**Phase C — PoE-depth (1–6 above), one layer per content update.** Pick-one-of-N first (highest
value-to-effort), then stat-tree, then gear enchants, then capstone/discovery/seasonal.

## Architecture hooks (why this is viable to add)

| Need | Existing hook |
|---|---|
| Wrath → Legion power | `legionBonusFraction = (PowerBonus + ΣLegionBonus [+ future terms]) /100` — already reserves the additive slot (System 15) |
| Bulwark → guild-raid dmg | `FlatDamagePercent` (post-crit, v0.2.5) gated on `ActiveRaid.GuildId != null` |
| Hoard/Discernment → drops | quest loot pipeline already scales drops (`base × (1+Disc×0.03)`, System 20) — add a mastery multiplier |
| Ancient Raid | `ActiveRaid` + nullable `AncientEventId` fork, like `GauntletEventId`/`GuildId` — no new combat path |
| Ledgers / idempotency / settlement | append-only ledger + unique-index pattern used everywhere (gems, gauntlet, guild sigils) |
| Re-spec gem spend | idempotent gem ledger + Bazaar SKU |
| Mastery choice nodes / stat-tree / enchants | the JSON `ConditionalBonus` engine (v0.2.5) — most of it is data, not new combat code |
| Audit | every state change audited (governance DNA) |

## Resolved decisions (owner-confirmed 2026-06-07)

1. **All 4 Ancients are pledgeable from launch.** The Rise wakes them for community raids/rewards and
   introduces *future* Ancients as content. (No mastery-less new server.)
2. **The per-tier "pick one of N" choice is deferred to Phase C.** v1 Masteries = simple flat global +
   pledge modifiers.
3. **NO whale cap.** Uncapped contribution fuels the community ladder (§B4) — heavy spend/play means
   more than self-improvement because it lifts the whole server. Casuals are protected by the
   participation floor + everyone-gets-the-same-community-tier, and personal bracket multipliers stay
   bounded (×0.50–×1.50), not by capping anyone.
4. **Magnitudes raised (still TUNE):** globals up, and **pledging ≈ doubles** an Ancient's bonus — Wrath
   ~5% Legion power, Hoard/Discernment ~8% drops/quality+sigil pledged-and-maxed; Bulwark stays at its
   ~1% ceiling (direct combat %, the deliberate exception).

---

## Sources (research, 2026-06-07)
Meta-events: WoW Gates of Ahn'Qiraj, Warframe Scarlet Spear, Destiny 2 *Almighty*. Breadth-rating:
Warframe Mastery Rank, Destiny Triumph/Seals, D4 Renown/Paragon, Genshin Spiral Abyss; HDI geometric-
mean. Community damage: Elite Dangerous Community Goals (two-axis + ≥1 floor), GW2 world-boss/meta,
FFXIV FATE medals, AFK Arena/Idle Heroes guild bosses. PoE-depth: PoE Masteries (3.16), Atlas/
Voidstones, 40-challenge leagues, Labyrinth/Divine-Font enchants, Ascendancy gating; Grim Dawn
Devotion, Last Epoch Mastery, D4 Paragon (anti-pattern). Full citation list in session research logs.
```
