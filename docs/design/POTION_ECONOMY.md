# Potion economy — working design

Status: **design agreed in outline, not implemented.** The campaign does not yet reach the energy
costs this depends on. Owner decisions still open at the bottom.

Owner intent (2026-09-03): potions become a questing reward, so a player can sustain play without
the thing Dawn of the Dragons forced — multi-summoning guild campaigns through scripted clients just
to stay supplied. Sustainment should come from playing the game.

---

## The three resources

**Energy, Stamina, GuildStamina.** Equal 1:1:1 odds — a drop rolls which of the three it is at
33.3% each.

"Honor" was the original third name; there is no Honor resource. `ResourceType` carries Energy,
Stamina, GuildStamina and Health, and "honor" exists only as `PlayerMagicHonor`, a permanent Gauntlet
award. GuildStamina is the intended one — it already scales 1:1 with level and feeds guild raids,
which is the closest thing to Dawn's guild-campaign potion economy.

Health is deliberately NOT in the rotation.

## Potions restore a PERCENTAGE of the pool

Tiers: **3%, 5%, 7.5%, 15%, 25%.** A percentage rather than a flat amount means a potion keeps its
value as the player grows, with no re-authoring of content and no tier inflation.

Suggested weights, with the top tier genuinely rare:

| tier | weight | share of drops |
|---|---|---|
| 3% | 50 | 50.0% |
| 5% | 27 | 27.0% |
| 7.5% | 15 | 15.0% |
| 15% | 6 | 6.0% |
| 25% | 2 | 2.0% |

Average restore per drop: **5.375% of pool**.

## Drop rate: Discernment sets it, energy spent gates it

    P(drop) = P_discernment(d) x min(1, E / 200)

`d` is the player's Discernment, `E` the energy the quest attempt cost.

**Discernment ladder** — logarithmic, so all twelve rungs are visible steps rather than the top
eight doing nothing (a hyperbolic curve would saturate by ~50k and waste the rest):

    P_disc(d) = 0.01 + (1/15 - 0.01) x ln(d / 1000) / ln(5000 / 1)   clamped to [1/100, 1/15]

| Discernment | chance | 1 in |
|---|---|---|
| 1,000 | 1.00% | 100 |
| 5,000 | 2.07% | 48 |
| 10,000 | 2.53% | 40 |
| 25,000 | 3.14% | 32 |
| 50,000 | 3.60% | 28 |
| 100,000 | 4.06% | 25 |
| 250,000 | 4.67% | 21 |
| 500,000 | 5.13% | 20 |
| 1,000,000 | 5.60% | 18 |
| 1,500,000 | 5.87% | 17 |
| 2,000,000 | 6.06% | 17 |
| 5,000,000 | 6.67% | **15** |

**The energy gate is mandatory and multiplicative.** The headline 1-in-15 needs BOTH maxed
Discernment and a 200+ energy quest. At 25,000 Discernment:

| energy spent | 1 in |
|---|---|
| 20 | 318 |
| 28 | 227 |
| 56 | 114 |
| 200 | 32 |

Discernment drives rate only. Tier weights stay flat, so the 25% potion is equally rare for
everyone. Letting Discernment scale rate AND quality multiplies two growth curves together, which is
how a drop table runs away.

---

## The structural warning — read this before tuning

**This design has the auto-levelling bug's shape.** Potion value is a percentage of the pool, so it
grows linearly with level. Quest cost caps at 200, so it does not. The refund ratio R — the fraction
of spent energy handed back — therefore grows without bound:

    R = P(drop) x (1/3 energy) x E[tier] x MaxPool / QuestCost

| level | pool | R |
|---|---|---|
| 5,000 | 18,625 | 0.11 |
| 25,000 | 93,125 | 0.56 |
| **~45,000** | 167,600 | **1.00 — potions fund their own clicking** |

At R = 1.0 the energy economy stops existing. This is the same crossover that let a linear stamina
pool outrun a sublinear XP curve past level 139: linear always beats capped, eventually.

**The fix is the same one that worked there — make the two curves the same shape.** Let quest energy
cost keep scaling with chapter so cost tracks pool, rather than flattening at 200. The campaign is
not built that deep yet, which makes now the cheap moment to bake it in.

If cost stays capped, the alternative is to cap R directly, which means suppressing drop rate at high
level and feels punitive.

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
resource and tier rolls. Roughly 99% of attempts then cost exactly one `NextDouble()`. The real
per-drop cost is the inventory upsert, identical to the sigil drop that already ships.

**Tier roll.** One `NextDouble()` against a cumulative weight table — five comparisons, no allocation.
Should Discernment ever be wanted on quality as well, `u' = u^(1/(1+b))` biases a cumulative table
toward the rare end in a single `Math.Pow`, continuously and without a cliff.

**Consumption already handles percentages.** `UseItemAsync` consumes only what the pool can absorb.
That generalises unchanged: `needed = ceil(deficit / restoreAmount)` becomes
`ceil(deficit / (pct x maxPool))`.

**Config tables must not default empty.** Anything added here — tier weights, the Discernment ladder
— needs a non-empty default or a `ValidateOnStart` guard, for the reason recorded in the config audit:
an empty table is indistinguishable from a binding failure and degrades silently.

---

## Open

1. **Does quest energy cost keep scaling past 200?** Everything above hinges on it. Answering "yes"
   removes the runaway by construction.
2. **Where do potions sit against the existing seven?** The shipped ones are flat (25 / 60) and
   gold-priced. Do those become percentage potions, stay as a separate low tier, or leave the shop?
3. **Is a drop one potion or a stack?** Assumed one.
4. **Do the three resources drop at 1:1:1 regardless of what the player needs?** A player at full
   Stamina still rolling Stamina is a wasted drop, which is honest but can feel bad.
