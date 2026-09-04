# Skill point costs — evaluation sheet

Two owner items: stamina should cost 2 SP rather than 1, and stat costs should scale rather than stay
1:1 for the whole game.

**The stamina one is a live defect, not a preference.** At 1 SP per point, stamina reaches the same
LSI ceiling for exactly half the skill points, which makes it strictly better than energy at every
level. 2 SP restores parity precisely.

The scaling curve is a core-fundamentals decision and is **not** something to change autonomously —
options with arithmetic are below, and the choice is the owner's.

---

## Current behaviour

`StatService.AllocateStatPointCoreAsync` charges **1 SP per point, for every stat**. There is no cost
function anywhere:

```csharp
if (stats.SkillPoints < amount) return Fail(...);
...
case StatType.Stamina: stats.AllocateToStamina(amount); break;
```

The LSI cap is checked separately and correctly (`(E + 2S)/level <= 7.45`), but it constrains the
*investment*, not the *price*.

## Finding 1 — stamina is strictly better, and it is arithmetic

Both stats are bounded by the same LSI ceiling of `7.45 × level`. Reaching it:

| level | pure energy | pure stamina | |
|---|---|---|---|
| 1,000 | 7,450 SP | 3,725 SP | **2× cheaper** |
| 7,500 | 55,875 SP | 27,938 SP | **2× cheaper** |
| 25,000 | 186,250 SP | 93,125 SP | **2× cheaper** |

Energy needs `E = 7.45L` points at 1 SP each. Stamina needs only `S = 3.725L` points, because each
one counts double toward the cap — but each still costs 1 SP.

Both builds hit the same ceiling and, per `AUTOLEVELLING_AND_PACING_EVAL.md`, the same XP throughput.
**Stamina gets there for half the skill points**, leaving `3.725 × L` SP free for Attack, Defense and
Discernment. At level 25,000 that is 93,125 spare points a pure-energy build does not have.

### This corrects an earlier finding of mine

`AUTOLEVELLING_AND_PACING_EVAL.md` Finding 1 says every E:S split levels at exactly the same rate.
That is true **per LSI point** and false **per skill point** — and the skill point is the constraint
that actually binds, because SP is earned and the LSI cap is merely a ceiling.

The correct statement is narrower: splits are equal in XP throughput once at the cap; they are *not*
equal in what it costs to get there. That sheet has been corrected.

**Fix:** charge 2 SP per stamina point. At level 7,500 pure stamina then costs 55,875 SP — identical
to pure energy. Parity is exact, not approximate, because the 2 in the price matches the 2 in the
LSI weight.

## Finding 2 — cost scaling, with the arithmetic

Assumed endgame budget: **~100,000,000 SP**, from 50,000 raids at the owner's stated late-game rate of
a few thousand SP per raid.

Four candidate curves. All leave the first 1,000,000 points at 1 SP each, so **nothing a player meets
before the deep endgame changes at all**:

| curve | stat reachable on 100M SP | SP to reach 100M stat |
|---|---|---|
| **A** flat 1:1 (current) | 100,000,000 | 100,000,000 |
| **B** double per decade — 1 / 2 / 4 / 8 SP at 1M / 10M / 100M | 30,250,000 | 379,000,000 |
| **C** double per 5× — 1 / 2 / 4 / 8 / 16 | 26,375,000 | 689,000,000 |
| **D** gentle, +1 per decade — 1 / 2 / 3 / 4 | 37,000,000 | 289,000,000 |

The question this actually poses: **what should 100,000,000 skill points buy?**

- Under **A**, it buys 100M stat, and stat value is just a restatement of raids cleared. Allocation
  carries no cost pressure, so the "choice" is only *which* stat, never *how much*.
- Under **B**, it buys 30M, and pushing one stat to 100M costs 3.8× the entire endgame budget — so a
  specialist is a genuine sacrifice rather than a default.
- **C** is the harshest; **D** the mildest that still bites.

**B is the one I would put in front of a playtest first**, purely because doubling at each order of
magnitude is legible — a player can hold "every 10× costs twice as much" in their head, and the
breakpoints land on round numbers they will see coming. That is a presentation argument, not a
balance one, and the balance call is the owner's.

### What a scaling curve interacts with

- **The LSI cap becomes a much weaker constraint.** If energy costs 4 SP a point deep in the endgame,
  the SP price binds long before `7.45 × level` does. Worth checking whether the cap still does
  anything at high level, or becomes vestigial.
- **Owner decision 0c (Defense is dominated) gets worse, not better.** If Defense costs the same as
  Attack and returns a quarter of the damage, a rising price multiplies the gap in absolute terms.
- **Respec does not exist.** Stat allocation is one-way. Under 1:1 a wasted point is one point; under
  a rising curve a wasted point deep in the endgame is eight, and the absence of a respec becomes a
  much sharper problem.
- **Discernment's anchors move.** The potion tier anchor (2,000,000) and the rare-drop ceiling
  (10,000,000) are stated in *stat value*. A curve that makes 10M Discernment cost 379M SP moves
  those anchors far out of reach and both would need re-siting.

---

## Recommended order

1. **Stamina to 2 SP.** A defect, small, and independent of everything else.
2. **Decide the curve** (owner) — it moves several other anchors, so it wants deciding before those
   are tuned rather than after.
3. **Re-site the Discernment anchors** once the curve is chosen.

## Not covered

- Whether Health should also cost more than 1 SP. It has no LSI weight, so the same argument does not
  apply, but its value per point was not examined here.
- Whether SP income should scale instead of SP cost. Raising raid SP has the same effect on the
  *ratio* and a very different effect on how progress feels — it was not modelled.
