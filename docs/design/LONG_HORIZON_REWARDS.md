# Long-horizon rewards — logins, the Idol, milestones, anniversaries

*Owner decisions, 2026-09-07. Status: **decided, none built**. Companion to
[`docs/specs/active/system-26-gravewends-table.md`](../specs/active/system-26-gravewends-table.md).*

Four reward structures that all answer the same question: what does a player get for having been
here a long time, without it becoming a wall for someone who arrives late?

---

## 1. The 14-day login cycle

**Decided.** Week one establishes the habit with ordinary things; week two pays it off.

| Day | Week 1 | Day | Week 2 |
|---|---|---|---|
| 1 | Gold | 8 | Mid-tier potions |
| 2 | Mid-tier potions | 9 | Gold, scaled up |
| 3 | Gold | 10 | Stat bag |
| 4 | Mid-tier potions | 11 | Potions, upper tier |
| 5 | Small stat bag | 12 | Sigil |
| **7** | **XP — 15% of a level, not a full one** | 13 | **Random troop mystery box** |
| 6 | Gold | 14 | **Guaranteed Orange gear piece** |

**The day-7 XP grant is 15% of the current level's requirement, deliberately not a full level.**
Owner decision, and the right one: a full level from a login is a free stat allocation, and at high
level `XpToNextLevel` is large enough that a free level would outpace playing the game. 15% is felt
without being a substitute for playing.

Concretely it reads from `IStatService.XpToNextLevel(level)` and grants 15% of that, so it scales
with the player automatically and needs no per-level table.

**Open:** does day 14's Orange come from a fixed pool, and does the cycle repeat from day 15? The
paper's warning applies to the first question — a rotating annual pool is how each year's set
obsoletes the last.

## 2. The Idol — unbroken logins

**Decided: unbroken, not cumulative.** I argued for cumulative days and the owner has decided
otherwise; recording the decision and the one mitigation worth considering, then leaving it.

The research paper files Dawn's Idol of the Devoted under anti-patterns because its crafted payback
*"broke even after roughly 54 weeks of unbroken logins,"* which made premium currency effectively
unearnable for a free player. Two separable things were wrong there: the **unbroken requirement**,
and the fact that the payback was **premium currency**.

The decision here keeps the unbroken requirement. That is defensible as long as the second half does
not follow it — an unbroken streak that pays *prestige* is a badge of honour, while an unbroken
streak that pays *the economy* is a hostage situation. Keep the Idol's payout on the prestige side
and the anti-pattern does not reproduce.

**Worth considering, not decided:** a small number of grace tokens (one earned per 30 days, maximum
two held) that cover a single missed day. It preserves "unbroken" as the thing being celebrated
while removing the outcome where a year of devotion ends because of a flight.

The 14-day cycle above runs alongside the Idol and is unaffected by it.

## 3. Milestone design rights — the Honorable Rock

**Decided.** The first **three** players to reach each milestone level earn the right to **design a
magic**.

Milestone levels: **2,500 · 5,000 · 7,500 · 10,000 · 15,000 · 25,000**

The paper's version of this is MapleStory Idle's "Honorable Rock," which permanently engraves the
first ten guilds per server to clear new content, and Dawn's own end-of-campaign armour sets named
after the first guild to clear them on Hard. Its assessment: *"a brilliant, cheap, deeply social
prestige hook that costs almost nothing to implement and produces years of guild loyalty."*

**The vessels already exist.** `magics.json` carries five inert Orange placeholders —
`magic_pinnacle_5000`, `_7500`, `_10000`, `_15000`, `_25000` — sitting at 0% chance and 0% amount,
reserved and doing nothing. They are exactly this feature's storage, already shipped. Only
**2,500 is missing** and needs adding.

Three constraints on what a winner may design, so the prize cannot break the game:

- Magnitude is fixed by rarity. An Orange damage proc is **+110%**, full stop — the winner chooses
  the *name*, the *flavour*, and the *chance within 14–18%*, not the power.
- `MagicProcBandTests` enforces this, so a designed magic that breaks the band fails the build rather
  than reaching players.
- The player's name goes in the description. That is the actual prize.

**Deferred by the owner:** the guild equivalent, and the questing equivalent. Questing is to be
assessed before launch.

## 4. Anniversaries — ROTA Coins

**Decided.** Anniversary gear is bought with **ROTA Coins**, a distinct tier of coin per anniversary.
**The game's launch starts the annual clock.**

This is knowingly adjacent to the sharpest anti-pattern in the paper, so the difference has to be
deliberate. Dawn ended with *"annual colour-coded coin families (Brown, Grey, Green, Blue, Purple,
Orange Solus Coins; Crystal Dawn Coins; Rising/Endless/Eternal/Infinite Dawn Coins) that became dead
weight the instant an event closed"* — and players who could not grind enough during a 1–4 week
window *"lost partial progress forever."*

Per-anniversary coin tiers are the same shape. What separates a collection from a graveyard is what
happens to last year's coin, and that decision is not yet made. Three options worth weighing:

1. **Coins never expire.** Last year's coins still buy last year's gear, forever. Slowest power
   creep, largest inventory.
2. **Coins convert.** Old coins exchange down into the current tier at a rate. Keeps them alive,
   costs a conversion UI.
3. **Coins expire.** The Dawn model. Not recommended, and named here only so the choice is explicit.

Related and already noted: ROTA currently runs gold, gems, guild sigils, shop tickets, Gauntlet
tokens and Pitchforks. That is six currencies before ROTA Coins exist. The paper's target state is
*"one soft, one premium, one event — that is it."*

---

## What is not decided here

- **Sweep** and **tourist mode** — both deferred, both in `docs/AGENT_BACKLOG.md`.
- **Guild and questing milestone prestige** — owner will assess.
- **Gravewend's Table** — its own spec.
