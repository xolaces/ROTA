# ROTA's economy against published genre design standards

Backlog R6. Cross-checks ROTA's measured behaviour against what the idle/incremental design
literature actually says, rather than against intuition.

Every ROTA number here comes from a prior measurement in this repo, cited to its eval sheet. Every
genre claim is quoted from a source listed at the bottom. **Nothing about another game's balance is
stated from memory** — where a source could not be retrieved, that is said so explicitly.

---

## The standard, in one line

Daniel Cook's value-chain framework states the matching rule directly:

> Constant sources pair with fixed sinks. Linear sources require linear or stronger sinks.
> Exponential sources demand exponential or competitive sinks.

and the consequence of getting it wrong:

> Linear sinks cannot indefinitely contain exponential sources — the mathematics ensure overflow
> eventually.

The idle-game math literature says the same thing from the cost side: costs are expected to grow
**exponentially** while production grows **linearly or polynomially**, because "exponential growth
(anything of the form n^x, for n > 1) will eventually catch and far exceed any polynomial growth
(x^k)". The canonical example given is AdVenture Capitalist, where each purchase multiplies the next
cost by a growth rate of **1.07**.

So the genre's default posture is: **the sink must outgrow the source, permanently.**

---

## Where ROTA already conforms

**Raid XP and quest XP scale with resource spent, not with level.** `CombatConfig.XpPerStaminaRoll*`
and `QuestConfig.XpPerEnergyRoll*` both derive XP from the resource actually consumed, so reward and
cost move together by construction. This is the "linked resources" pattern — one resource required to
produce another — and it is why the XP economy has no crossover.

**The Gauntlet's HP curve is exponential, at almost exactly the genre's canonical rate.**
`H(n) = StageHpBase x StageHpGrowth^(n-1)` with `StageHpGrowth = 1.0493`, interpolating up to
`LateRampFinalGrowth = 2.0` past stage 200. Against the AdVenture Capitalist figure of **1.07** quoted
in the idle-math source, ROTA's base growth of **1.0493** sits squarely in the same band — arrived at
independently, and the closest thing in the codebase to a textbook-correct sink.

(The Gauntlet *health cost* per hit is a different curve and is **piecewise linear**, not exponential:
`base + stage x 0.25 + max(0, stage - 200) x 2.0`. A steeper linear segment, not a shape change. It is
the HP curve, not the health cost, that carries the exponential.)

**Gold and gem sinks are priced, not capped.** Nothing about them saturates.

## Where ROTA departs from the standard — three measured cases

### 1. The potion design is a constant sink against a linear source

The worst of the three shapes Cook lists. `pool(L) = 25 + 7.45 x L` grows linearly with level forever,
while quest energy cost stops growing entirely at chapter 16 (`ChapterScalingCap = 16`, all chapters
16–24 reusing `EnergyCostMultiplier = 5.05`).

Measured consequence: the refund ratio reaches 1.0 at **level 13,092** and keeps climbing — 1.53 at
level 20,000, 3.82 at level 50,000. Below the 200-energy gate the cost cancels out of the ratio
entirely, so no choice of quest avoids it, and a player who never invests in Discernment still crosses
at level 36,716.

This is precisely Cook's overflow case, and his description of the symptom matches what R = 1.0 means
mechanically: *"If you have all the sticks you'll ever need, why bother harvesting another dirt
pile?"* Past the crossover the energy pool stops rationing play, so the activity it gates stops being
a decision.

→ `docs/eval/POTION_ECONOMY_EVAL.md`. Already Owner decision 0. **Not yet built** — the cheapest
possible moment to change it.

### 2. Discernment's sinks were fixed against an unbounded source

Cook: *"Marginal value erodes over time with repeated actions."* His prescribed fixes are exponential
sink scaling, or cascading fixed sinks into repeatable then exponential ones.

Before `f59e481`, every Discernment sink was a **fixed** sink: crit chance capped at 1,000
Discernment, crit damage at 5,000, rare drops 90% saturated by 450,000 — against an endgame that runs
to 15,000,000–100,000,000. Past roughly 0.5% of the intended range, a point in Discernment was worth
nothing and Attack was strictly better.

The retune moved the caps out 100× and re-anchored rare drops on 1,000,000 with a hard ceiling at
10,000,000. That is Cook's "extend the sink" fix rather than his "make it exponential" fix — the
caps still exist, they are simply further away. **It is a larger fixed sink, not a scaling one**, so
the same erosion recurs at 100× the Discernment unless a genuinely scaling sink is added later.

Worth being explicit that this was a deliberate trade: the caps are what keep crit bounded, and
removing them is a different game.

### 3. The achievement ladder is a fixed sink, correctly

Not a defect — the counter-example that shows the rule is about *sinks that ration play*, not all
progression. AP is a pure score that nothing spends, and the ladder's reward rate holds inside
0.467–0.800 AP per clear across a 500× span of thresholds. A prestige score is allowed to be finite
because it gates nothing.

→ `docs/eval/ACHIEVEMENT_PACING_EVAL.md`.

---

## What the literature suggests that ROTA has not considered

**Prestige as the release valve.** The idle-math source treats prestige — a reset that trades progress
for a persistent multiplier — as the standard answer once exponential costs make progression
"prohibitively long". ROTA has no prestige mechanic. It has mastery, which is additive and permanent,
and one-way stat allocation with no respec. There is currently no mechanism that resets any curve.

This is not a recommendation to add one; a Dawn-faithful async RPG may deliberately not want it. But
it is the genre's standard tool for the exact problem ROTA's late game will have, and its absence
should be a decision rather than an omission.

**Cascading sink types.** Cook's third suggestion — fixed sinks early, repeatable in the middle,
exponential at the top — describes a structure ROTA half-has. The Gauntlet is the exponential tier;
gold and gems are the repeatable tier; stat caps are the fixed tier. What is missing is an
**exponential sink for Discernment specifically**, which is why finding 2 recurs.

---

## What could not be sourced

The Dawn of the Dragons wiki pages on stamina-versus-energy efficiency, autolevelling and stamina
potions all returned HTTP 402 and could not be read. **No DotD numbers appear in this document**, and
any comparison to DotD's actual balance remains unsourced. The design intent recorded in
`POTION_ECONOMY.md` — that DotD forced multi-summoning through scripted clients to stay supplied — is
the owner's own account of playing it, not a cited figure, and is treated as intent rather than data.

The GDC Europe 2016 talk "Quest for Progress: The Math and Design of Idle Games" (Pecorella) is the
most relevant primary source found; the PDF is image-based and could not be text-extracted. Worth a
manual read if this analysis is ever taken further.

---

## Sources

- [Value chains — Lost Garden, Daniel Cook](https://lostgarden.com/2021/12/12/value-chains/) — the
  source/sink matching rule and the sink-relevance problem.
- [The Math of Idle Games, Part I — Game Developer](https://www.gamedeveloper.com/design/the-math-of-idle-games-part-i)
  — exponential cost versus polynomial production, the `cost_base x rate_growth^owned` form, the 1.07
  figure, and prestige as the release valve.
- [Idle games and how to design them — Machinations](https://machinations.io/articles/idle-games-and-how-to-design-them)
  — consulted; general balance guidance only, no mechanical specifics, and nothing from it is relied
  on above.
- [Quest for Progress (GDC Europe 2016), Anthony Pecorella](https://media.gdcvault.com/gdceurope2016/presentations/Pecorella_Anthony_Quest%20for%20Progress.pdf)
  — located but not machine-readable.
