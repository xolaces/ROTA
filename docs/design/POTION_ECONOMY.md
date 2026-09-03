# Potion economy — working design

Status: **design agreed, not implemented.** No potion drop code exists yet. The campaign does not
reach the energy costs this depends on. Owner decisions still open at the bottom.

Owner intent (2026-09-03): potions become a questing reward, so a player can sustain play without the
thing Dawn of the Dragons forced — multi-summoning guild campaigns through scripted clients just to
stay supplied. Sustainment should come from playing the game.

> **Supersedes the 2026-09-03 morning draft, which had the two axes backwards.** That version had
> Discernment driving drop FREQUENCY with flat tier weights. The owner reversed it the same day:
> **Discernment drives TIER QUALITY, energy spent drives FREQUENCY.** Every number below is derived
> from the reversed model. If you are reading a copy that says "Discernment ladder → chance", it is
> the superseded one.

---

## The three resources

**Energy, Stamina, GuildStamina.** Equal 1:1:1 odds — a drop rolls which of the three it is at 33.3%
each.

"Honor" was the original third name; there is no Honor resource. `ResourceType` carries Energy,
Stamina, GuildStamina and Health, and "honor" exists only as `PlayerMagicHonor`, a permanent Gauntlet
award. GuildStamina is the intended one — it already scales 1:1 with level and feeds guild raids,
which is the closest thing to Dawn's guild-campaign potion economy.

Health is deliberately NOT in the rotation.

## Potions restore a PERCENTAGE of the pool

Tiers: **3%, 5%, 7.5%, 15%, 25%.** A percentage rather than a flat amount means a potion keeps its
value as the player grows, with no re-authoring of content and no tier inflation.

## The two axes

    P(drop)   = Pmax x min(1, E / Ecap)^k          <- ENERGY SPENT sets frequency
    tier mix  = lerp(floor, ceiling, t^ease)       <- DISCERNMENT sets quality

Shipped parameters:

| symbol | value | meaning |
|---|---|---|
| `Pmax` | 1/15 | best achievable drop rate per quest click |
| `Ecap` | 200 | energy per click at which frequency saturates |
| `k` | 1.0 | shape of the energy ramp (linear) |
| `Dmin` | 1,000 | Discernment where the tier ladder starts |
| `Dsat` | 1,992,880 | Discernment where tier quality saturates |
| `ease` | 3.0 | back-loading exponent on the quality curve |
| `N` | 500 | quest clicks one full pool buys (cost tracks pool) |

**`t` is logarithmic in Discernment, not linear.** This matters and is easy to get wrong:

    t      = clamp( ln(D / 1,000) / ln(1,992,880 / 1,000), 0, 1 )
    mix    = t^3

A linear `t` produces a curve that saturates far too early and does not reproduce any of the numbers
below. The log scale is what lets twelve rungs from 1k to 5M all be visible steps.

## Tier weights across the Discernment range

Floor weights `[88, 10, 2, 0, 0]` average **3.2900%** of pool. Ceiling weights `[20, 25, 25, 20, 10]`
average **9.2250%**. Both sum to 100, so the linear interpolation between them needs no renormalising.

| Discernment | t | t³ | 3% | 5% | 7.5% | 15% | 25% | avg restore | R |
|---|---|---|---|---|---|---|---|---|---|
| 1,000 | 0.0000 | 0.0000 | 88.0% | 10.0% | 2.0% | 0.0% | 0.0% | 3.2900% | 0.366 |
| 5,000 | 0.2118 | 0.0095 | 87.4% | 10.1% | 2.2% | 0.2% | 0.1% | 3.3464% | 0.372 |
| 10,000 | 0.3031 | 0.0278 | 86.1% | 10.4% | 2.6% | 0.6% | 0.3% | 3.4552% | 0.384 |
| 25,000 | 0.4237 | 0.0761 | 82.8% | 11.1% | 3.7% | 1.5% | 0.8% | 3.7414% | 0.416 |
| 50,000 | 0.5149 | 0.1365 | 78.7% | 12.0% | 5.1% | 2.7% | 1.4% | 4.1003% | 0.456 |
| 100,000 | 0.6062 | 0.2227 | 72.9% | 13.3% | 7.1% | 4.5% | 2.2% | 4.6118% | 0.512 |
| 250,000 | 0.7268 | 0.3839 | 61.9% | 15.8% | 10.8% | 7.7% | 3.8% | 5.5682% | 0.619 |
| 500,000 | 0.8180 | 0.5473 | 50.8% | 18.2% | 14.6% | 10.9% | 5.5% | 6.5385% | 0.726 |
| 1,000,000 | 0.9092 | 0.7517 | 36.9% | 21.3% | 19.3% | 15.0% | 7.5% | 7.7512% | 0.861 |
| 1,500,000 | 0.9626 | 0.8920 | 27.3% | 23.4% | 22.5% | 17.8% | 8.9% | 8.5837% | 0.954 |
| 2,000,000 | 1.0000 | 1.0000 | 20.0% | 25.0% | 25.0% | 20.0% | 10.0% | 9.2250% | **1.025** |
| 5,000,000 | 1.0000 | 1.0000 | 20.0% | 25.0% | 25.0% | 20.0% | 10.0% | 9.2250% | 1.025 |

`R` is the refund ratio at level 25,000 (see below). The 25% potion is never common: it peaks at
**10% of drops**, and a drop itself is at best 1 in 15, so a saturated player sees one roughly every
**450 quest clicks**.

The `ease = 3` back-loading is deliberate. Half the Discernment range (t = 0.5) yields only
t³ = 0.125 of the quality gain, so quality arrives late and the goal stays a goal.

## The refund ratio R

R is the fraction of spent energy the potion stream hands back:

    R = P(drop) x (1/3 chance it is Energy) x E[restore %] x MaxPool / QuestCost

`MaxPool / QuestCost` is just `N`, the number of clicks a full pool buys.

**Worked, at saturation:**

    R = (1/15) x (1/3) x 0.09225 x 500
      = 0.0222222 x 0.09225 x 500
      = 0.00205 x 500
      = 1.0250

So a fully saturated player recovers **102.5%** of the energy they spend — self-supplying, with 2.5%
of headroom. For R to land on exactly 1.000 the pool would need to buy **487.80** clicks rather than
500. That 2.5% overshoot is a deliberate margin, not an error, but it is the number to change if
self-supply should be exact.

**Self-supply is reached at D = 1,808,205,** by bisection on R. Tier quality saturates slightly later
at D = 1,992,880. The owner's stated target was "an average of about 2,000,000 Discernment before you
can really just self supply" — the model delivers 1.81M, about 10% early, which is within the
intended band.

## Level, and the early-game pushback

Quest cost **tracks the pool** at a fixed 500 clicks per pool: `cost = (25 + 7.45 x level) / 500`.
This is the structural fix (see the next section) and it has a clean consequence:

**Above level 13,420, R does not depend on level at all.** `MaxPool / QuestCost` is 500 by
construction, and the energy gate `min(1, E/200)` is saturated, so level drops out of the formula
entirely. Progression changes what a potion is worth in absolute energy, never the ratio.

Below that, the energy gate scales R down linearly, because a click costs less than 200 energy:

| level | pool | cost/click | energy gate | R at 2M Disc | R at 100k Disc |
|---|---|---|---|---|---|
| 1,000 | 7,475 | 14.9 | 0.075 | 0.077 | 0.038 |
| 2,500 | 18,650 | 37.3 | 0.186 | 0.191 | 0.096 |
| 5,000 | 37,275 | 74.5 | 0.373 | 0.382 | 0.191 |
| 10,000 | 74,525 | 149.1 | 0.745 | 0.764 | 0.382 |
| **13,420** | 100,004 | 200.0 | **1.000** | **1.025** | 0.512 |
| 25,000 | 186,275 | 372.6 | 1.000 | 1.025 | 0.512 |
| 50,000 | 372,525 | 745.0 | 1.000 | 1.025 | 0.512 |

This is the owner's "early game is a bit of a push back but not a wall", expressed as one number: a
level-1,000 player recovers 7.7% of their energy even at saturated Discernment, and a level-13,420
player recovers all of it. The wall is soft because it is a multiplier, never a gate — a low-level
player still gets drops, just fewer.

---

## The structural warning — and why it is now resolved

The earlier draft flagged this design as carrying **the auto-levelling bug's shape**: potion value is
a percentage of the pool so it grows linearly with level, while quest cost was capped at 200, so R
grew without bound and crossed 1.0 around level 45,000. Linear always beats capped, eventually. It is
the same crossover that let a linear stamina pool outrun a sublinear XP curve past level 139.

**Letting quest cost track the pool removes it by construction.** `MaxPool / QuestCost` is then the
constant `N`, and the runaway term is gone — not bounded, not suppressed, absent. This is the fix the
earlier draft recommended, and the tables above assume it.

**It is therefore a hard requirement, not a tuning preference.** If quest energy cost ever flattens
instead of scaling with chapter, R resumes growing with level and the energy economy stops existing
at high level. The alternative — capping R directly — means suppressing drop rate as the player
grows, which reads as punishment for progressing.

See `docs/AGENT_BACKLOG.md` R3: confirm whether the shipped content actually caps quest cost.

---

## Implementation notes

**Server-side. Not negotiable.** Rolling on the client means the client tells the server what it won;
a modified client claims a 25% potion every time. The threat is not hypothetical for this game
specifically — its reference point is a community that ran scripted clients to farm potions.

**The cost concern is misplaced.** `Random.NextDouble()` is ~5 nanoseconds. A quest attempt is already
a transaction with an advisory lock, several repository round trips and an audit insert — 2 to 10
milliseconds. Six orders of magnitude apart. `QuestService` already rolls sigil drops, boss gems and
rare drops on the same path.

**Structure so the common case costs one call.** Roll the drop gate first; only on a hit do the
resource and tier rolls. At the best rate roughly 93% of attempts then cost exactly one
`NextDouble()`, and at low Discernment far more. The real per-drop cost is the inventory upsert,
identical to the sigil drop that already ships.

**Tier roll.** One `NextDouble()` against a cumulative weight table — five comparisons, no allocation.
The weights are recomputed only when Discernment changes, so cache them on the player's stat read
rather than rebuilding the mix per click.

**Consumption already handles percentages.** `UseItemAsync` consumes only what the pool can absorb,
and a bulk use consumes only what fits (`8741d7e`, `65eb2ce`). That generalises unchanged:
`needed = ceil(deficit / restoreAmount)` becomes `ceil(deficit / (pct x maxPool))`.

**Config tables must not default empty.** Anything added here — tier weights, the easing exponent,
`Dsat` — needs a non-empty default or a `ValidateOnStart` guard, for the reason recorded in the config
audit: an empty table is indistinguishable from a binding failure and degrades silently.

**Guard the Discernment input.** The rare-drop curve now clamps Discernment before use
(`RareDropDiscernmentCap`, `f59e481`) and `GetCritProfile` takes a `long`. Any potion code must do the
same — endgame Discernment runs to 100M and the tier mix must not be handed an unclamped value.

---

## Open

1. **Does quest energy cost keep scaling past 200?** Everything above hinges on it. Answering "yes"
   removes the runaway by construction. This is now backlog item R3.
2. **Should self-supply be exact?** R saturates at 1.025, not 1.000. Exact needs N = 487.8 clicks per
   pool instead of 500.
3. **Where do potions sit against the existing seven?** The shipped ones are flat (25 / 60) and
   gold-priced. Do those become percentage potions, stay as a separate low tier, or leave the shop?
4. **Is a drop one potion or a stack?** Assumed one.
5. **Do the three resources drop at 1:1:1 regardless of what the player needs?** A player at full
   Stamina still rolling Stamina is a wasted drop, which is honest but can feel bad.
