# System 26 — Gravewend's Table

*The communal raid. Owner-specified 2026-09-07. Status: **specified, not built**.*

One raid where contribution does not decide the loot — everybody who lands a hit takes the same
thing home. It is summoned by a Gauntlet top-ranker spending their trophy currency, which makes it
the one piece of content in the game where the strongest player's reward is *opening a door for
everyone else*.

## Where it comes from

The research paper singles this pattern out. Dawn's guild raids **Rhalmarius the Despoiler** and
**Grundus** gave identical loot to every participant regardless of damage, and the paper's verdict is
worth quoting exactly: it *"briefly broke power scaling but produced euphoric 'everyone gets the
prize' moments players still cite as career highlights."*

The lesson taken is narrow and deliberate: **one** raid, not a category. ROTA's contribution-tier
engine is good and stays untouched everywhere else. This is the exception that makes the rule
legible.

## The name

Gravewend is already canon — the peasant who got exactly one swing, and the only item in the game
permitted to wink. A raid where everyone eats the same regardless of rank is his table.

---

## §1 What makes it unlike every other raid

Five things, and each one is a departure that needs code.

| # | Departure | Why |
|---|---|---|
| 1 | **Global drop.** Every participant receives identical loot; contribution tiers are ignored entirely. | The whole point. |
| 2 | **One gear piece per completion, maximum.** Each piece rolls at 0.3%; if two hit, only one is granted. | Keeps a communal raid from being a set-vending machine. |
| 3 | **A summon cooldown** — the only one in the game. Two hours, per summoner. | The scarcity that replaces contribution gating. |
| 4 | **The cooldown starts when the raid ENDS, not when it is summoned.** | See §3 — this is the subtle part. |
| 5 | **Costs 250 Pitchfork Tokens**, the top-rank-only Gauntlet currency. | Only a Gauntlet champion can open it. |

## §2 Numbers

| Field | Value | Source |
|---|---|---|
| `baseHp` | **4,000,000** | 2× `raid_c6z4b` (Guardian of the Throne of Ancients, 2,000,000) — owner-specified |
| Summon cost | **250 Pitchfork Tokens** | owner-specified |
| Gear drop chance | **0.3% per piece**, capped at one piece granted | owner-specified |
| Gold on completion | **Above the standard curve** — magnitude TBD | owner: "give more reward on completion gold" |
| Difficulty multipliers | Standard 1.0 / 1.4 / 2.0 / 3.6 | unchanged |
| Timer | TBD — must be long enough for a server to gather | open question |

The gear pool is the third Orange chase set (owner decision: two Orange sets from questing, one from
this raid). That set does not exist yet.

## §3 The cooldown rule, stated precisely

This is the part that will be implemented wrong if it is written loosely, so:

> A player may have **one** Gravewend's Table summon in flight at a time. The two-hour cooldown does
> **not** begin at summon — it begins when that raid **ends**, whether by being killed or by
> expiring. A raid that lives for its full timer therefore blocks its summoner for the whole timer
> *plus* two hours.

Joining is unrestricted: a player may hit any number of other people's Tables. Only *summoning* is
gated.

**Implementation consequences.** The cooldown anchor is the raid's end timestamp, and `ActiveRaid`
does not currently record one — `MarkDefeated()` flips lifecycle state without stamping a time, and
expiry is inferred from `ExpiresAt` rather than observed. Both need to resolve to a single
"ended at" instant before this rule can be evaluated. The natural shape is a nullable `EndedAt` set
by whichever of the two paths fires first, plus a per-player lookup for "your most recent Table, and
when it ended."

**The race worth naming now:** two summons issued in the same instant by one player must not both
succeed. The project already has the right pattern for this — the conditional-UPDATE latch used by
`BetaKeyRepository.TryRedeemAsync` — and it should be reused rather than reinvented as a read-then-write.

## §4 Open questions for the owner

1. **Timer length.** A 48-hour timer means a blocked summoner for 50 hours. Shorter timers make the
   cooldown meaningful; longer ones make the raid easier to fill. These pull against each other.
2. **What happens to the Pitchforks if nobody kills it?** Refund on expiry, or is the cost the risk?
   The paper's warning about currencies that evaporate is relevant.
3. **Gold magnitude.** "More" needs a number. It should be worth summoning even when the 0.3% misses.
4. **Does it count for achievements, masteries and the Collector counters?** Everything else does; a
   silent exclusion would be a bug report.

## §5 Deliberately not decided here

Guild-side communal raids. The owner has said guild content is assessed later, and one communal raid
is enough to learn from before a second exists.

---

*Depends on nothing. Blocks nothing. Cannot be built until the third Orange set exists, because the
0.3% roll needs a pool to roll from.*
