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

## 3. Milestone design rights — the pinnacle magics

**Decided, and partly built.** The **first player** to reach a milestone level designs that level's
magic outright. Everyone who reaches the same level afterwards **inherits that design and unlocks it
permanently** — the first player earns the authorship, not exclusivity. A player may own **one of
each**; the per-raid magic slot cap is unchanged, so owning them all is not casting them all.

Milestone levels: **1,000 · 2,500 · 5,000 · 7,500 · 10,000 · 15,000 · 25,000** — later means rarer.

### What a winner may design

Anything, within balance. Explicitly on the table (owner): a damage proc, an item drop from the
magic itself, or an *interactive* effect — a chance to force your mount's proc to fire, a boost to
your mount's proc chance, a general's ability re-triggering, and so on.

> **These exotic effects exist ONLY on pinnacle magics.** No ordinary magic and no item may carry one
> until the owner says otherwise. `PinnacleMagicTests.ExoticEffectTypesAppearOnlyOnPinnacleMagics`
> enforces it, so the rule cannot erode by a later content pass copying a pinnacle entry as a
> template.

Three limits keep a designed magic from breaking the game:

- **Magnitude is not the winner's to choose.** An Orange damage proc is +110%, full stop. They pick
  the name, the flavour, and the chance within 14–18%.
- `MagicProcBandTests` fails the build rather than shipping a magic that breaks the band.
- **An undesigned placeholder must be genuinely inert** (0% / 0.0). A placeholder carrying a live
  number is the "silent stub that looks complete" the code-labelling rules forbid.

### The level-1,000 showcase — Ascendant's Banner

Designed in-house to demonstrate what a pinnacle magic can do that an ordinary one cannot. **Built
and tested**, not a stub.

`FlatAttackAura`, a new effect type: **+120 flat Attack on every hit landed on the raid, by anyone,
for as long as it is applied.** Always on — no roll.

It was chosen over the other candidates for one property. Being **flat and raid-wide, it helps the
weakest participant most**: a 500-Attack newcomer gains about a quarter of their damage from it,
while the level-1,000 veteran who applied it gains about two percent. Every other power in the game
widens the distance between a veteran and a newcomer. This is the only one that narrows it — which
is precisely the failure the research paper blames for ending Dawn's new-player acquisition.

It also cannot inflate: it enters through **Attack**, so it flows through `(ATK × 4 + DEF) × hitSize`
like any stat point and never multiplies with a proc.

### A menu for future winners

Brainstormed alongside the Banner and left here as seeds rather than decisions:

| Idea | What it does | Why it is interesting |
|---|---|---|
| **The Spur** | When your mount's proc fails, a chance to fire it anyway | The owner's own suggestion. Conditional on gear, so it rewards a build rather than a slot |
| **Echo of Command** | Chance to re-trigger a general's ability in the same hit | Makes legion composition matter at the moment of the hit |
| **The Long Count** | Every hit that procs nothing raises the next hit's proc chance | A pity counter. Turns bad luck into rising tension instead of a flat feel-bad |
| **Tithe of the Fallen** | The raid's killing blow drops one extra item for *every* participant | The Rhalmarius euphoria, in a magic rather than a raid |
| **Quartermaster's Seal** | Converts a share of the raid's gold into materials | The first magic that changes what a raid *pays*, not how hard it hits |

### Still missing

**Nothing grants a pinnacle magic to anyone.** There is no code path from reaching a milestone level
to owning `magic_pinnacle_*`. The content exists, the levels are configured, the first-claim ledger
exists — but the delivery does not. See `docs/AGENT_BACKLOG.md`.

**Deferred by the owner:** the guild equivalent, and the questing equivalent (to be assessed before
launch).

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
