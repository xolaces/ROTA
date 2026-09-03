# Potion economy — evaluation sheet

Companion to `docs/design/POTION_ECONOMY.md`. That document describes the intended model; this one
checks it against the **code and content as they actually exist**, and the two disagree.

**Headline: as designed, the energy economy ends at about level 13,100.** Potions are not implemented
yet, which is the good news — this is a finding about a design, before it is built, which is the
cheapest possible moment to have it.

Nothing here is a balance change. The numbers are the input to an owner decision, listed at the end.

---

## Assumptions, so every number below can be re-derived

| symbol | value | source |
|---|---|---|
| `Pmax` | 1/15 | design doc — best drop rate per click |
| `Ecap` | 200 | design doc — energy at which frequency saturates |
| `pool(L)` | `25 + 7.45 × L` | `BaseMaxEnergy 25`, LSI ceiling 7.45/level |
| ceiling restore | 9.2250% | tier weights `[20,25,25,20,10]` over `[3,5,7.5,15,25]` |
| floor restore | 3.2900% | tier weights `[88,10,2,0,0]` over the same |
| resource split | 1/3 | Energy / Stamina / GuildStamina at 1:1:1 |

Refund ratio — the fraction of spent energy the potion stream hands back:

    R = Pmax x min(1, c/Ecap) x (1/3) x restore x pool(L) / c

where `c` is the energy one quest click costs.

---

## Finding 1 — quest energy cost is capped, and the design assumed it is not

`QuestConfig.ChapterScalingCap = 16`. `GetChapterScaling` clamps the lookup, so **chapters 16 through
24 all reuse chapter 16's `EnergyCostMultiplier = 5.05`.** Cost stops growing there. The pool does
not — it is linear in level, forever.

    cost = ceil(BaseEnergyCost x difficultyMult x chapterMult x (1 + zoneIndex x 0.04))

Content today: 139 nodes, chapters 1–7 built, `BaseEnergyCost` from 5 to 28, `zoneIndex` 0–4.

The design doc's tables assume `cost = pool / 500`, i.e. that cost tracks the pool so `pool/c` stays
constant. **That is not what the code does**, and the doc's structural-warning section says removing
that assumption reinstates the runaway. It is reinstated.

## Finding 2 — below the energy gate, the cost cancels out of R entirely

This is the part that makes it urgent rather than merely wrong.

For any `c ≤ 200` the gate is `c/200`, so:

    R = Pmax x (c/200) x (1/3) x restore x pool/c
      = Pmax x (1/200) x (1/3) x restore x pool          <- c cancels

Verified numerically at level 20,000, saturated Discernment:

| quest cost | 10 | 26 | 50 | 100 | 142 | 199 | 200 | 201 | 300 | 425 | 493 | 900 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **R** | 1.5275 | 1.5275 | 1.5275 | 1.5275 | 1.5275 | 1.5275 | 1.5275 | 1.5199 | 1.0183 | 0.7188 | 0.6197 | 0.3394 |

Three consequences:

1. **Which quest a player picks does not matter** below 200 energy. The cheapest node in the game
   refunds exactly as efficiently as the most expensive one. Content difficulty stops being an
   economic choice.
2. **Above 200, cheaper is strictly better** — R declines as `1/c`. So the optimal play is the
   cheapest quest that reaches the gate, and every quest above it is a worse deal.
3. **The single most efficient cost in the game is exactly 200**, and the curve is continuous there.

## Finding 3 — the crossover is level 13,092

With the cost cancelled, R depends only on level:

    R = 1.025 x 10^-5 x pool(L)
    R = 1.0  ->  pool = 97,561  ->  L = (97,561 - 25) / 7.45 = 13,092

Past that, every quest click returns more energy than it costs and the pool stops rationing play.

| level | pool | R at saturated Discernment | R at floor Discernment |
|---|---|---|---|
| 5,000 | 37,275 | 0.382 | 0.136 |
| 10,000 | 74,525 | 0.764 | 0.272 |
| **13,092** | **97,561** | **1.000** | 0.357 |
| 20,000 | 149,025 | 1.528 | 0.545 |
| 25,000 | 186,275 | 1.909 | 0.681 |
| 36,716 | 273,559 | 2.804 | **1.000** |
| 50,000 | 372,525 | 3.818 | 1.363 |

A floor-Discernment player — someone who has never invested in it — crosses at level **36,716**. So
the runaway is not confined to optimised accounts; it is only delayed for everyone else.

For contrast, at the *most expensive* node the game can currently produce (base 28, Nightmare, zone 4,
chapter ≥16 → cost 493) the crossover is level 32,277. But no player is obliged to run that node, and
Finding 2 says they are strictly worse off if they do. **13,092 is the number that matters**, because
it is the one a player can choose.

---

## Finding 4 — the 2,000,000 Discernment anchor moves a long way with SP per raid

The owner's stated intent: self-supply should require roughly 2,000,000 Discernment, as a long-term
goal. Assuming raid SP dominates and is spent on Discernment (the LSI ceiling forces surplus there
once energy investment is capped at 7.45/level):

| SP per raid | raids to reach 2,000,000 | share of a 50,000-raid endgame |
|---|---|---|
| 200 | 10,000 | 20.0% |
| 300 | 6,667 | 13.3% |
| 400 | 5,000 | 10.0% |
| 1,000 | 2,000 | 4.0% |
| 2,000 | 1,000 | 2.0% |
| 5,000 | 400 | 0.8% |

Stated plan is 200–400 early, "thousands later". At the early end the anchor sits at 10–20% of the
endgame; once raids pay thousands it collapses to a few percent. **The anchor is not load-bearing at
either end** — it is reached early in the endgame regardless, and Finding 3 shows level crosses R=1.0
independently of Discernment anyway. Discernment moves the crossover from 13,092 to 36,716; it does
not prevent it.

---

## What would actually fix it

Not recommendations — the trade-offs are yours. Costed for effort and side-effects.

1. **Make cost track the pool.** `cost = pool(L) / N` for a constant `N`, which is what the design doc
   already assumes. Then `pool/c = N` and the runaway term disappears by construction rather than
   being bounded. `N = 487.8` puts R at exactly 1.000 at saturation; `N = 500` puts it at 1.025.
   Biggest change: quest cost stops being an authored per-node property and becomes derived, which
   affects every existing node and the whole feel of chapter progression.
2. **Raise `ChapterScalingCap` and keep the multiplier climbing.** Smallest change, and it only
   delays the crossover rather than removing it — cost would have to grow linearly in level, and
   chapter is not level.
3. **Make the potion tier restore a flat amount rather than a percentage.** Removes the linear term
   directly, at the cost of the property the design was built around: a potion keeping its value as
   the player grows, with no re-authoring and no tier inflation.
4. **Cap R directly** by suppressing drop rate at high level. The design doc already calls this the
   punitive option and it reads as a penalty for progressing.

**Option 1 is the only one that removes the term rather than bounding it**, and it is what the design
doc already assumed was true.

---

## What this sheet does not cover

- **Stamina and GuildStamina.** The same 1/3 share and the same percentage-of-pool restore apply to
  both, against raid and guild-raid costs that were not examined here. The shape is likely identical
  but the crossover level is not — it needs the raid stamina-cost model, which is a separate pass.
- **Whether 200 is the right `Ecap`.** Findings 2 and 3 both pivot on it, and it is a design-doc
  parameter that no code has ever enforced. Lowering it moves the crossover later, linearly.
- **Any behaviour of potions in the shop**, which are flat-amount and gold-priced today and are a
  separate economy from the drop.
