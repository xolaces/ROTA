# Autolevelling and level pacing — evaluation sheet

Owner question: at what point can players autolevel, and which E:S builds level fastest?

> **OWNER INTENT, clarified 2026-09-03.** Autolevelling is WANTED, not a defect. Levels 1–500 should
> creep into the next level with little or no waiting; occasional autolevelling around 3,000–3,500 is
> fine; it should not be a heavy occurrence before 7,500; past that it should be available. That
> reverses the framing this sheet was originally written under, and Finding 3 is rewritten to match.
> The headline conclusion changes: **the potion design is too STINGY for this intent, not too
> generous.**

**Three answers, and the second one is the good news:**

1. **No E:S split levels faster than any other.** They are exactly equal, by construction — the LSI
   cap's 2× stamina weighting precisely cancels stamina's 2× XP rate. This looks deliberate and it is
   correct.
2. **Autolevelling is impossible from pool drains alone.** A full drain of both pools is 79.8% of a
   level in the linear regime, and less where a milestone floor bites. It never reaches 1.0.
3. **The only autolevelling vector in the game is the potion economy crossing R = 1.0.** Past that,
   energy refunds itself, clicks become unbounded, and levelling follows. The potion crossover level
   *is* the autolevelling level.

And one structural finding that reframes the "more play time per level" goal: **regen is a flat 288
energy per day at every level**, while pools grow linearly. Past roughly level 1,000 the game is not
paced by regen at all — it is paced entirely by potions and gem refills.

---

## The formulas

    TNL(L)      = max( milestoneFloor(L), 14 x L, 30 x L^0.8 )
    energy pool = 25 + EnergyInvestment
    stamina pool= 5  + StaminaInvestment
    LSI cap     : EnergyInvestment + StaminaInvestment x 2  <=  7.45 x L
    quest XP    = 1.5 per energy   (deterministic)
    raid XP     = 3.0 per stamina  (mean of Uniform[1,5])

`30 x L^0.8` only leads below level ~45; past that `14 x L` dominates, with milestone floors
overriding both at ten breakpoints.

## Finding 1 — every E:S split levels at exactly the same rate

This falls straight out of the two weightings:

    1 LSI point into ENERGY  = 1 energy  x 1.5 XP/energy  = 1.5 XP
    1 LSI point into STAMINA = 0.5 stamina x 3.0 XP/stamina = 1.5 XP

Stamina costs 2× to buy and pays 2× to spend. The two cancel **exactly**. So XP from a full drain of
both pools is:

    XP = 1.5 x (E + 2S) = 1.5 x 7.45 x L = 11.175 x L        for EVERY split

There is no heavy-E or heavy-S levelling advantage to find, because there is not one. A player
choosing their split is trading **what they do**, not **how fast they level** — energy buys quest
content and potion drops, stamina buys raids, SP and loot.

This is worth protecting. It is the kind of property that breaks silently if either the LSI weighting
(2×) or the XP rates (1.5 / 3.0) is retuned without the other. **Nothing currently tests it.** →
backlog R9.

## Finding 2 — pool drains never reach a level

    XP per full drain / TNL  =  11.175 x L / 14 x L  =  0.798

Constant in the linear regime, and lower wherever a milestone floor is in force:

| level | TNL | XP per full drain | **drains per level** |
|---|---|---|---|
| 100 | 1,400 | 1,170 | 1.20 |
| 500 | 7,000 | 5,640 | 1.24 |
| 1,000 | 15,000 | 11,228 | 1.34 |
| 2,500 | 35,000 | 27,990 | 1.25 |
| 5,000 | 75,000 | 55,928 | 1.34 |
| 10,000 | 200,000 | 111,802 | **1.79** |
| 13,092 | 200,000 | 146,356 | 1.37 |
| 25,000 | 600,000 | 279,428 | **2.15** |
| 50,000 | 700,000 | 558,802 | 1.25 |

Always above 1.0, so a player who drains everything they have still needs a second helping to level.
**Dawn's autolevelling cannot happen here from pools alone.**

Note the **sawtooth**: 1.34 at 5,000 → 1.79 at 10,000 → 1.37 at 13,092 → 2.15 at 25,000 → 1.25 at
50,000. Milestone floors jump the cost of a level, then linear growth erodes the jump until the next
floor. A player hits a wall at each breakpoint and then coasts. That is a pacing choice rather than a
defect, but the swing is nearly 2× and it is not smooth.

Also note 25,000 is the **last** floor. Past it, TNL is `14 x L` forever and the ratio settles at
0.798 — so levels get *relatively* cheaper for the entire endgame.

## Finding 3 — the potion crossover is the autolevelling MECHANISM, and it is mis-sited

Findings 1 and 2 hold only while the pool is finite. R is the fraction of spent energy the potion
stream returns; at R >= 1.0 energy is self-sustaining and levelling is limited only by how fast a
player can act. **That is the autolevelling lever, and it is the only one in the game.**

Measured against the stated intent:

| level | R (pure energy) | R (50/50) | intent |
|---|---|---|---|
| 500 | 0.038 | 0.019 | near-continuous levelling |
| 3,000 | 0.229 | 0.115 | occasional autolevel |
| 3,500 | 0.268 | 0.134 | occasional autolevel |
| **7,500** | **0.573** | **0.287** | **autolevel available** |
| 13,092 | 1.000 | 0.500 | — |
| 26,184 | 2.000 | 1.000 | — |

At 7,500 a 50/50 player gets **R = 0.287 where ~1.0 is wanted — a 3.5x shortfall**. The design does
not autolevel too early; it autolevels roughly 3.5x too LATE, and then overshoots without bound
(R = 3.8 by level 50,000).

Both halves are the same defect: R is proportional to level because the pool grows and the cost does
not. A quantity that is wrong-low early and wrong-high late is a quantity with the wrong shape, not
the wrong constant.

### The fix already proposed happens to be the right shape for this intent

`POTION_ECONOMY_EVAL.md` option 1 — let quest cost track the pool — removes the level term entirely.
R then depends only on Discernment:

| Discernment | R | effect |
|---|---|---|
| floor (~1,000) | **0.366** | potions extend a session, cannot sustain it |
| ceiling (~2,000,000) | **1.025** | self-sustaining — autolevelling |

Constant at every level, rising only with Discernment. That is close to the stated intent by
construction:

- Early game: R ~0.37, so potions help and regen still matters. Levelling is fast because TNL is
  small, not because energy is free.
- Late game: R -> 1.025 once Discernment is built, and autolevelling becomes available.
- **It is earned rather than reached.** Discernment comes from raid SP, so autolevelling becomes a
  reward for having played the raid economy rather than a consequence of having a big number.

The remaining question is whether ~2,000,000 Discernment is reachable near level 7,500. At 300 SP per
raid that is ~6,667 raids, and nothing in this repo maps raids to level yet — so the anchor may need
to move down. That is a tuning question on a correct shape, which is a much better position than
tuning a wrong one. → **R10**.

## Finding 4 — regen is irrelevant past low level, so nothing paces play except potions

`RegenMinutesPerPoint` is flat: one point per interval, 5.0 minutes for Conscript. Pools grow
linearly with level. So **daily regen is a constant 288 energy at every level**, while the pool it
refills is not constant at all:

| level | energy pool | regen per day | **% of pool per day** | full refill |
|---|---|---|---|---|
| 10 | 100 | 288 | 289% | 8 hours |
| 100 | 770 | 288 | 37.4% | 2.7 days |
| 1,000 | 7,475 | 288 | 3.85% | 26 days |
| 13,092 | 97,560 | 288 | 0.295% | 339 days |
| 25,000 | 186,275 | 288 | 0.155% | 647 days |

This is the same constant-against-linear mismatch as the potion cost cap and the Defense mitigation
cap, and it has the largest practical consequence of the three: **past roughly level 1,000 regen
stops being a meaningful source of play.** Everything after that is potions and gem refills.

That matters directly for the "more play time within a level" goal. The current curve does not give
more play per level — it gives *less*, because the pool that funds a level grows while the tap that
fills it does not. Play time per level is currently governed by potion supply, not by the XP curve,
and the transition from "starved" to "unlimited" happens abruptly at R = 1.0 with nothing in between.

Against the stated intent this is the sharpest pacing problem in the game. Days per level from regen
alone, using both pools (288 energy x 1.5 XP + 288 stamina x 3.0 XP = 1,296 XP per day):

| level | days per level | intent |
|---|---|---|
| 10 | 0.1 | near-continuous |
| 100 | 1.1 | near-continuous |
| **500** | **5.4** | "little or no waiting" |
| 1,000 | 11.6 | — |
| 3,000 | 32.4 | occasional autolevel |
| 3,500 | 37.8 | occasional autolevel |
| **7,500** | **92.6** | autolevel available |
| 25,000 | 463.0 | — |

**The early game the owner describes exists only below roughly level 200.** By 500 a level takes 5.4
days on regen alone, and by 7,500 it takes three months. Potions are not a supplement to this curve —
they are the entire curve past low level, which is why their tuning decides the pacing of the whole
game.

Options, all owner calls:

1. **Make regen proportional** — a percentage of max pool per interval rather than a flat point.
   Turns the flat tap into a linear one and restores regen as a pacing lever at every level. Biggest
   change; interacts with the class regen identities.
2. **Scale `RegenMinutesPerPoint` down with level** — same effect, smaller edit, but the per-class
   rates become level-dependent and harder to reason about.
3. **Leave it and let potions be the pacing lever** — which is the current de-facto design. It works
   only if R stays comfortably below 1.0, so it makes the potion tuning load-bearing.
4. **Accept a low regen floor as an anti-idle measure** and pace deliberately through potions, with
   the refund ratio targeted at something like 0.6–0.8 rather than 1.0.

→ filed as **Owner decision 0d**.

---

## What this sheet does not cover

- **Actual campaign XP per node.** The XP rates used are the config's per-resource rates
  (`XpPerEnergyRoll*`, `XpPerStaminaRoll*`), which is how XP is computed today — authored per-node XP
  no longer feeds the live formula. If content is ever re-authored to grant XP directly, none of the
  above holds.
- **Generals, gear and legion multipliers.** The owner's stated preference is that autolevelling
  become niche *and* aided by generals. Nothing here models a damage or XP multiplier from equipment,
  so a build that levels faster through gear is not ruled out by Finding 1.
- **Guild stamina.** A third pool with its own regen, excluded from LSI, not modelled here.
