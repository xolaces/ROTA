# Which drops use which curve — R7b, audited

R7b asks two things: which loot entries carry `rareScaling` today and whether the unflagged ones
should move to the asymptotic curve, and separately to quantify the raid-side twin
`MaxThresholdDropChance` "which is the same shape driven by Hoard mastery rather than Discernment".

**Both halves come back differently than the item expects.**

- **Quest side: there are no unflagged entries.** All 832 quest chance and gear drops carry
  `rareScaling`, so all of them already use the asymptotic curve. The capped multiplier `Scale()` is
  **unreachable on the quest path** with shipped content.
- **Raid side is not the same shape.** The Discernment lane multiplies by `1 + 0.03D`, unbounded in
  D. The Hoard lane multiplies by `1 + hoardFraction`, and Hoard tops out at **8%**. So the 0.95
  clamp is very nearly inert, and the lane's problem is not saturation — it is that the lane barely
  does anything.
- **And it does nothing at all on the two raids that matter most.** Both World raids are
  `baseHp: 0`, which means timer-only, which means no killer, and Hoard's raid drop scaling is
  killer-only. Expected benefit: **exactly zero, for every participant, always.**

---

## 1. The audit

Every drop entry in `loot_tables.json`, by kind, with `rareScaling` coverage:

```
entry kind               count   rareScaling coverage
  quest.chanceItem          20   20 flagged / 0 not
  quest.gear               812   812 flagged / 0 not
  quest.guaranteed         216   n/a (no flag on this type)
  raid.thresholdItem      1424   n/a (no flag on this type)
  raid.thresholdGear       104   0 flagged / 104 not
  raid.magic                24   n/a (no flag on this type)

  TOTAL drop entries: 2600
```

Reading it against the code:

**`quest.guaranteed` (216)** never passes through a scaler at all — `ProcessQuestLootAsync` grants
guaranteed drops directly, before either curve. Correct, and not R7b's business.

**`quest.chanceItem` (20) and `quest.gear` (812)** are the two kinds that CAN be flagged, and both are
at 100%. So `Scale()` — the capped multiplier — is reached on the quest path only by magic, unit and
legion drops, and shipped content contains **zero** of those. R7b's question therefore has no
population: there is nothing unflagged to move.

That is a recent state, not an old one. `quest.chanceItem` only became flaggable in `3b0d2b1`
(`ItemDropChance` had no `RareScaling` field before it), and the 20 entries are the deep relics from
`66808bf`. The audit's value is that it now holds: any future content that adds a quest chance drop
without the flag lands on a curve that saturates at trivial Discernment, and nothing warns about it.

**`raid.thresholdGear` (104)** carries a `chance` the raid path never reads — gear is granted
unconditionally at a qualifying threshold. That is Owner decision 0e and is already filed.

## 2. Why the quest lane needs the flag — the saturation, restated

For anything that ever does land on `Scale()`, the curve is `base × (1 + 0.03D)` clamped at 0.95, and
it saturates as a function of the drop's **own** base rate:

```
  base 0.5     saturates at D =      30
  base 0.1     saturates at D =     283
  base 0.01    saturates at D =   3,133
  base 0.005   saturates at D =   6,300
  base 0.0005  saturates at D =  63,300
```

A 50%-base drop stops responding to Discernment at **thirty points** — before a player has finished
the tutorial. This is the strongest argument for the flag being the default rather than the
exception, and it is why the coverage above matters going forward even though it is currently 100%.

## 3. The raid twin is not the same shape

R7b's premise is that `MaxThresholdDropChance` (0.95) is "the same shape driven by Hoard mastery".
The clamp value matches; the multiplier feeding it does not.

```
  Discernment lane:  base x (1 + 0.03 x D)      D unbounded  -> saturates at trivial D
  Hoard lane:        base x (1 + hoardFraction) hoardFraction <= 0.08
```

Hoard's ceiling, read from `masteries.json` and `MasteryConfig`:

```
  globalPercentByLevel = [0.8, 1.6, 2.4, 3.2, 4.0]   percent, level 1..5
  PledgeMultiplier     = 2.0                          "pledging roughly doubles it"
  BreadthMicroBonus    = 0.0 by default (cap 2.0)
  -> maximum HoardDropMultiplier = 1.08
```

So the clamp binds only when `base × 1.08 > 0.95`, i.e. `base > 0.8796`. Against shipped content:

```
raid threshold item entries: 1424
  Hoard delivers in FULL:                 1328  (93.3%)
  Hoard partially CLIPPED by the clamp:     96  (6.7%)
    of those, base >= 0.95 so Hoard does NOTHING anyway:  80
    genuinely clipped, in the 0.8796-0.95 band:           16
    worst case: base 0.9300 wanted 1.0044, got 0.9500 — lost 5.44pp
```

**The clamp is not a saturating sink.** It is a safety rail against a multiplier that cannot reach it,
and in the 16 cases where it does clip, it is clipping a bonus that would otherwise exceed 100%
probability — which is the clamp doing its job, not eroding anything.

## 4. The finding that actually matters here

Hoard's raid drop scaling is **killer-only**, by an explicit and documented decision — scaling every
participant would need a mastery read per participant inside the advisory-lock transaction, which is
the per-participant kill-loop cost System 22 deferred:

```csharp
double hoardForThisPlayer = p.PlayerId == callerPlayerId ? callerHoardDropMultiplier : 1.0;
```

The consequence, for a **maxed and pledged** Hoard:

```
raid kind                          participants   expected relative bonus
  World (timer-only, no killer)             any                    0.000%
  Standard (killer takes it all)              1                    8.000%
  Standard (killer takes it all)              5                    1.600%
  Standard (killer takes it all)             10                    0.800%
  Standard (killer takes it all)             25                    0.320%
  Standard (killer takes it all)             50                    0.160%
```

The World row is the sharp one, and it is not a rounding effect. Both World raids ship with
`baseHp: 0`:

```
  {'id': 'raid_ironcolossus', 'tier': 'World', 'baseHp': 0, 'timerHours': 168}
  {'id': 'raid_malachar',     'tier': 'World', 'baseHp': 0, 'timerHours': 168}
```

`baseHp: 0` means the kill branch never fires; the raid ends only when its timer runs out, and
`SettleExpiredRaidsAsync` passes `callerPlayerId: Guid.Empty`. No participant can match it. So on the
two raids that are the **only** source of Attack/Defense/Discernment points, and which grant 132
unassigned SP against a Standard raid's 20, a fully-invested Hoard mastery contributes **nothing to
drop rate, for anybody, ever**.

Hoard's other lanes are unaffected — gold-on-hit scales for the hitter, and the quest drop lane scales
for the questing player. This is the raid drop lane specifically.

---

## What this is not

Not a re-flagging. R7b says to bring numbers and not to re-flag content autonomously, and in any case
the quest side has nothing left to flag. The raid side needs an owner call rather than an edit,
because "Hoard is killer-only" is a deliberate performance trade, not an oversight.

## What I did not check

- Whether the 16 genuinely-clipped entries are content I generated in `66808bf` or predate it. They
  are high-base rungs either way, and the clip is bounded at 5.44pp.
- The Discernment sigil-find and drop-quality lanes, which use `discFraction` from the same mastery
  system and would have the same killer-only question if they ran on the raid path. They do not —
  both are quest-side.
- Whether `MaxThresholdDropChance` should exist at all now that the multiplier feeding it is bounded
  well below it. Removing a clamp is a fundamentals change and is not worth the risk for 16 entries.
