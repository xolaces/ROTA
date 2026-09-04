# Saturating sinks — full sweep

Backlog R7. The crit retune (`f59e481`) fixed two sinks that saturated inside the first 0.5% of their
stat's range. This is the sweep for the rest, using the same test: **where does this sink stop
responding, and how does that compare to the range of the stat feeding it?**

**Headline: Defense is strictly dominated by Attack at every point in the game.** Not past a
threshold — everywhere. That is a bigger finding than the crit one and it is Finding 1.

Every saturation point below is computed from the shipped constants, shown inline.

---

## The inventory

| sink | formula | saturates at | stat's real range | status |
|---|---|---|---|---|
| Crit chance | `min(0.10, D × 1e-6)` | D = 100,000 | 15M–100M | retuned `f59e481` — still a *fixed* sink |
| Crit damage | `min(1.00, D × 2e-6)` | D = 500,000 | 15M–100M | retuned `f59e481` — still a *fixed* sink |
| Rare drops | `0.045 × d/(d + 111,111)`, d clamped at 10M | 90% at 1M, hard stop 10M | 15M–100M | retuned `f59e481` |
| **Generic Discernment drop multiplier** | `base × (1 + D × 0.03)`, capped 0.95 | **D = 600** at base 0.05 | 15M–100M | **untouched** — Finding 2 |
| **Gauntlet defense mitigation** | `min(0.80, Def × 0.001)` | **Def = 800** | unbounded | **untouched** — Finding 1 |
| Raid threshold drops (Hoard) | capped 0.95 | depends on Hoard % | — | same shape as the generic one |
| Gauntlet HP curve | `StageHpBase × 1.0493^(n-1)` | never — exponential | — | correct by construction |
| Achievement ladder | fixed 9 rungs | 5,000 clears | — | correct; gates nothing |

---

## Finding 1 — Defense is a dominated stat

Defense has exactly three mechanical roles. Every one of them is weaker than Attack's, and its only
*unique* one is both capped and inert.

**1. Raid damage — weighted 1× against Attack's 4×.**

```
RaidService.cs:918     baseValue = (EffectiveAttack * 4L) + EffectiveDefense
```

**2. Gauntlet battalion power — the same 4:1 weighting.**

```
GauntletBattalionService.cs:140     Power(totalAtk, totalDef) => totalAtk * 4 + totalDef
```

**3. Gauntlet damage mitigation — Defense's only unique role. It saturates at 800.**

```
reduction = min(GauntletHealthDefenseReductionMax, defense × GauntletHealthDefenseReductionPerPoint)
          = min(0.80, defense × 0.001)
          → saturated at defense = 0.80 / 0.001 = 800
```

Ordinary and guild raids have **no Defense mitigation at all** — `RaidHealthCostByDifficulty` is a
flat lookup by difficulty (Normal 5, Hard 10, Legendary 20, Nightmare 40). Defense does not appear.

**And the resource that mitigation protects gates nothing.**

```
RaidService.cs:899     // ...clamped at 0 — it never blocks the hit (PHASE-2: optional 0-health gate)
```

Health cannot stop a hit. So the 800-point mitigation sink reduces a number with no mechanical
consequence attached to it.

**Therefore:** a point in Attack is worth 4× a point in Defense for damage, everywhere, and Defense
buys nothing else that currently does anything. There is no build, no level and no content in which
Defense is the correct allocation. That is stronger than "Defense is weak" — it means the
Attack/Defense choice is not a choice.

This is not a defect in one number, so it is not something to retune autonomously. Options, with the
trade-off each carries:

1. **Turn on the 0-health gate** (the PHASE-2 note already anticipates it). Health becomes a real
   resource, mitigation becomes real, and Defense acquires a purpose — but a third pool now rations
   play, which is a significant change to how the game feels.
2. **Give Defense a scaling mitigation curve** instead of a capped linear one, in the shape of the
   Gauntlet HP curve. Pointless on its own while health gates nothing, so it pairs with option 1.
3. **Extend ordinary raids to use a Defense-scaled health cost**, so mitigation is not Gauntlet-only.
4. **Accept it and rebalance the 4:1 weighting**, making Defense simply a weaker damage stat rather
   than a dominated one — the smallest change, and it abandons Defense as a defensive stat.

→ filed as **Owner decision 0c**. Options 1 and 2 together are the only combination that makes
Defense a real choice; the rest are mitigations of the symptom.

## Finding 2 — the generic Discernment drop multiplier saturates between 30 and 3,133

```
boosted = base × (1 + Discernment × DiscernmentDropMultiplier)   capped at MaxDropChance
        = base × (1 + Discernment × 0.03)                        capped at 0.95
saturation: Discernment = (0.95/base − 1) / 0.03
```

| drop's base chance | saturates at Discernment |
|---|---|
| 0.01 | 3,133 |
| 0.05 | **600** |
| 0.10 | 283 |
| 0.25 | 93 |
| 0.50 | 30 |

Against an endgame Discernment of 15,000,000–100,000,000, every one of these is reached before the
player leaves the early game. A drop with a 50% base is saturated at **30 Discernment**.

This is the sink the crit retune did *not* touch. It is partly by design — `QuestConfig` says
rare-scaling drops deliberately skip this multiplier "which explodes at high Discernment" and use the
asymptotic curve instead. So the chase set is handled. **Everything not flagged `rareScaling` still
uses this path**, and for those drops Discernment stops mattering almost immediately.

Filed as **R7b**: audit which drops are flagged `rareScaling` and decide whether the unflagged ones
should move to the asymptotic curve too.

## Finding 3 — the crit retune extended the sinks but did not change their shape

Recorded here because the sweep is the right place for it. `min(cap, stat × rate)` is a fixed sink
whatever the rate. Moving the caps out 100× buys roughly 100× the runway; it does not stop the
saturation happening. Crit damage now saturates at 500,000 Discernment against a 15M–100M endgame —
better than 5,000, still 3% of the range.

Already tracked as R6b, and `docs/research/ECONOMY_VS_GENRE_STANDARDS.md` has the genre framing:
the literature's answer is a sink that *scales with* the source, and the Gauntlet HP curve is the one
example already in this codebase.

---

## What was checked and found sound

- **Attack** — uncapped everywhere it is used. No saturation.
- **Gauntlet HP** — exponential, `1.0493^(n-1)`. Cannot saturate.
- **Gold and gem prices** — priced per item, no ceiling on spend.
- **XP per resource** — derived from the resource spent, so it tracks cost by construction.
- **Achievement AP** — finite by design and gates nothing, so its ceiling is not a sink problem.
- **`MaxThresholdDropChance` (0.95)** — the raid-side twin of Finding 2. Same shape, driven by Hoard
  mastery rather than Discernment. Not quantified here because Hoard's percentage curve was not read;
  folded into R7b.

## Method, and its limits

Searched for `Math.Min(cap, …)` over a stat-scaled term, hyperbolic `d/(d+H)` forms, and every `Max*`
knob in `ROTA.Application/Configuration`. That catches the two shapes that have produced defects so
far. It would **not** catch a sink that saturates through content authoring rather than a constant —
for example a loot table whose best entry is already at 100% — and it does not cover the client.
