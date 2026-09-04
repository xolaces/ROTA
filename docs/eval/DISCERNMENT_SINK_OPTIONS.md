# Discernment's scaling sink — R6b, with the arithmetic

**The short version: R6b's premise does not survive contact with the grant rates.** The item asks for
options to make Discernment's sinks *scale*, on the grounds that they saturate "inside the first 0.5%
of an endgame that runs to 15,000,000–100,000,000." That endgame number is not reachable. At the rate
content actually grants skill points, **15,000,000 Discernment is thirty years of daily play and
100,000,000 is four hundred.**

So the real finding is narrower and much cheaper to act on than "add an exponential sink":

- **Crit chance is the one genuine defect.** It dies in 2.5 to 12 months.
- **Crit damage is borderline.** One to five years.
- **Rare drops are fine.** Four to ten years to 90% of the bonus.
- **The 10,000,000 hard clamp is unreachable**, so it is not costing anyone anything.
- **The stated 15M–100M anchor is fiction** and should be corrected wherever it appears, because
  several open items are being reasoned about against it.

Everything below is computed from shipped config and shipped content, and the computation is shown.

---

## 1. What a point of Discernment buys today

From `CombatConfig` and `QuestConfig` as shipped:

| lane | formula | saturates at |
|---|---|---|
| crit chance | `min(0.10, D × 1e-6)` | **100,000** |
| crit damage | `min(1.00, D × 2e-6)` | **500,000** |
| rare drop (asymptotic) | `base + 0.045 × d/(d+111,111)`, `d = min(D, 10,000,000)` | 90% of bonus at **1,000,000**; hard zero at **10,000,000** |
| generic drop multiplier | `base × (1 + 0.03D)`, clamped 0.95 | **base-dependent**, see §4 |

The value curve, and the marginal value of *one more point*:

```
D                crit%   critDmg   rareDrop   marginal-rare   crit-marginal
            0   0.000    0.000    0.5000%   4.0500e-07   3.0e-06
       10,000   1.000    0.020    0.8716%   3.4088e-07   3.0e-06
      100,000  10.000    0.200    2.6316%   1.1219e-07   3.0e-06
      500,000  10.000    1.000    4.1818%   1.3388e-08   0.0e+00
    1,000,000  10.000    1.000    4.5500%   4.0500e-09   0.0e+00
   10,000,000  10.000    1.000    4.9505%   0.0000e+00   0.0e+00
  100,000,000  10.000    1.000    4.9505%   0.0000e+00   0.0e+00
```

Two things to read off it. Erosion in the rare lane, as a ratio against a fresh account:

```
  D=     100,000  1 / 4
  D=   1,000,000  1 / 100
  D=   5,000,000  1 / 2,116
  D=   9,990,000  1 / 8,265
  D >= 10,000,000  ZERO   (hard clamp)
```

And the fact the retune left in place: **past 10,000,000 Discernment every lane is exactly zero.** Not
small — zero, because crit is capped and the rare curve is clamped. If a player could reach that
number, the stat would be dead for the whole range above it.

## 2. Can a player reach those numbers? — measured from content

This is the question R6b never asks, and it decides the answer.

**The source.** Discernment comes from skill points. Measured from `loot_tables.json`, a full
cumulative clear grants:

```
  unassignedSP per clear:  min 20   max 132        (132 = the two World raids)
  Attack/Defense/Disc:     min 0    max 16         (World/Event only, per CLAUDE.md)
```

Best case for a pure Discernment build is every unassigned point plus its third of the A/D/Disc lane:
`132 + 16/3 = 137.3` Discernment per World-raid clear. Levelling adds 10 SP per level, which is
negligible here — ten years of play reaches about level 1,885, so **18,849 SP from levelling**, against
millions from raids.

**Every milestone, in clears and in play time:**

```
milestone                    Discernment      clears   time at 2/5/10 clears a day
crit chance cap                  100,000         728       364d     146d      73d
crit damage cap                  500,000       3,641       5.0y     2.0y     364d
rare drop 90% of bonus         1,000,000       7,282      10.0y     4.0y      2.0y
rare drop HARD clamp          10,000,000      72,816      99.7y    39.9y     19.9y
stated endgame LOW            15,000,000     109,223     149.6y    59.8y     29.9y
stated endgame HIGH          100,000,000     728,155     997.5y   399.0y    199.5y
```

**The 15M–100M anchor is 30 to 400 years of daily play**, in the best case, with every point poured
into one stat and only the two World raids farmed. It is not an endgame figure. Several open items —
R6b, R7b, R10, and the `f59e481` retune's own rationale — are reasoning against it, so it is worth
correcting at the source rather than working around.

The rate assumption is the soft part of this. If SP-per-clear scales with content tier later, or if
World raids multiply, the numbers move. But they would have to move by **two orders of magnitude** to
put 15M in reach of a normal player, and nothing currently in content does that.

## 3. So what is actually broken

**Crit chance, and only crit chance, on any honest reading.** It caps after 728 World-raid clears —
between two and a half months and a year of play. A player who reaches it has the whole rest of the
game with that lane dead. The retune already moved it 100×; it needs roughly another 10× to sit where
crit damage does, or a different shape entirely.

**Crit damage is arguable.** One year at 10 clears/day, five at 2. That is a real ceiling a dedicated
player will meet, but it is a ceiling reached after a long time, which is what a fixed sink is
allowed to be.

**Rare drops are correctly placed.** 90% of the bonus at four to ten years. The hard clamp at
10,000,000 is unreachable and therefore harmless — worth keeping precisely because it bounds the
formula without ever binding on a real account.

## 4. The one lane that saturates almost immediately, and it is not on the Discernment ladder

The generic multiplier, `base × (1 + 0.03D)` clamped at 0.95, saturates as a function of the drop's
own base rate:

```
  base 0.5     saturates at D =              30
  base 0.1     saturates at D =             283
  base 0.01    saturates at D =           3,133
  base 0.005   saturates at D =           6,300
  base 0.0005  saturates at D =          63,300
```

A 50%-base drop stops responding to Discernment at **30 points**. This is R7b's territory rather than
R6b's, and R7b should be read with these numbers: the question is not academic, because the common
drops are the ones that saturate first, and they are the ones a player interacts with most.

## 5. If a scaling sink is wanted anyway — four shapes, with numbers

Cook's rule ("linear sources require linear or stronger sinks") still applies in principle. The source
IS linear in time: raid SP per day is stamina-bound and stamina regen is flat, so cumulative raid SP
grows linearly, while level-granted SP grows only as `t^0.588` and is negligible by comparison. So if
the play-rate ceiling in §2 is ever lifted, the mismatch becomes real. The options, priced:

### A — do nothing, and correct the anchor

Accept that Discernment is a stat whose lanes fill over years, and fix the 15M–100M figure in the
docs. Cost: nothing. Leaves crit chance broken.

### B — drop the hard clamp, keep the asymptote

`RareDropDiscernmentCap` 10,000,000 → unbounded. Does **not** work:

```
  D=   10,000,000  bonus 4.9505%   1 in 20,446,933,578 points buys 1pp
  D=  100,000,000  bonus 4.9950%   1 in 2,004,448,913,580 points buys 1pp
```

The asymptote decays as `1/d²`. Removing the clamp replaces "exactly zero" with "indistinguishable
from zero", which is not a fix.

### C — a logarithmic lane

`value = k × ln(1 + D/h)`, calibrated to pass through today's value at D = 1,000,000 so nothing below
that changes: `k = 0.017589`.

```
  D=            0  bonus  0.000%
  D=      100,000  bonus  1.129%
  D=    1,000,000  bonus  4.050%      <- matches today exactly
  D=   10,000,000  bonus  7.934%
  D=  100,000,000  bonus 11.967%
  D=1,000,000,000  bonus 16.015%
```

The property that makes it legible: **every doubling of Discernment is worth a flat 1.219 percentage
points, at any D.** It never saturates, and it is safe in a probability lane — it would need
`D = 1.65e11` to reach 25pp and `2.46e17` to reach 50pp, both unreachable by any margin.

### D — put the scaling in a multiplier lane, not a probability lane

`quantityMultiplier = 1 + (D/1e6)^0.5`:

```
  D=      100,000  x 1.316
  D=    1,000,000  x 2.000
  D=   10,000,000  x 4.162
  D=  100,000,000  x 11.000
```

This is the structural point behind every saturating sink in the game: **a probability is bounded by
1.0 by construction, and a multiplier is not.** Every Discernment lane today is a probability, which
is why they all saturate. Moving late scaling into "how much drops" rather than "how often" removes
the ceiling without any curve trickery. It is also the largest design change of the four.

### E — a rank ladder on ROTA's own Gauntlet curve

Thresholds at `5000 × 1.0493^(n-1)`, each rank a flat grant, so the *sink* is exponential while the
*reward* is linear — Cook's prescription, and the shape the Gauntlet already uses:

```
  rank   cost of that rank        cumulative Discernment
     1                5,000                    5,000
    25               15,869                  236,345
    50               52,851                1,023,462
   100              586,189               12,375,011
   143                  —                  98,702,984
```

Rank 104 is where 15,000,000 lands and rank 143 is where 100,000,000 does, so **40 rungs span the
stated endgame band and 103 come before it** — a workable ladder, if the band were reachable. Against
the real rates in §2 a player gets to roughly rank 100 after twenty years, so this option only makes
sense together with a large increase in SP grants.

---

## What this is not

Not a recommendation. R6b says explicitly not to invent a sink autonomously, and the strongest result
here is that the problem is smaller and differently shaped than the item assumed. Two things do want
an owner call, and they are filed as such.

## What I did not check

- Whether SP-per-clear is *intended* to scale with content tier. If it is, §2's timings are wrong and
  the picture changes; nothing in the repo states an intent either way.
- Hoard's percentage curve on the raid side (`MaxThresholdDropChance`, 0.95). Same shape as §4 driven
  by mastery rather than Discernment. R7b already carries it.
- Whether the crit lanes matter at all late, given Attack is a 4× coefficient in the damage formula
  and Discernment is not. That is the "is Attack strictly better" question and it belongs with
  Owner decision 0c.
