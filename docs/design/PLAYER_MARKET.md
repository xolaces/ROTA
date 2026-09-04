# Player market — design analysis

Status: **owner proposal, not designed and not built.** Nothing in the codebase supports player-to-
player trade today.

Owner intent (2026-09-03): a player market with gold as the medium and hefty transaction costs on
both purchase and sale, 25% potions priced in the hundreds of thousands, so that gold acquires meaning
and the economy bridges two populations — whales who spend, and players who grind and spend little or
nothing.

**The instinct is right and one part of it is structurally correct.** The transaction tax is a
percentage, which makes it a scaling sink — the only kind that survives contact with this game's
curves. The fixed potion price is not, and would decay to pocket money exactly where it needs to bite.

Everything below is arithmetic from the shipped constants, shown inline.

---

## 1. Gold today: a linear faucet, and one sink nobody can afford to use

Faucets: quest gold (`quest.GoldReward × difficulty × Hoard`) and raid gold (`stamina × Uniform[3,8]`,
mean 5.5/stamina).

Sinks, in their entirety: **guild creation (25,000, one-off), crafting recipe costs, and the
consumable shop** — `ItemService.BuyItemAsync`, gold-priced consumables only.

That third one matters more than it looks, because **gold already buys potions today**:

| item | gold | restores | gold per point |
|---|---|---|---|
| Minor Energy Draught | 4,000 | 25 energy | 160.0 |
| Energy Draught | 9,500 | 60 energy | 158.3 |
| Minor/Energy Stamina equivalents | same | same | same |

### The existing potion price is already shape-correct — and unusable

Buying a full energy pool from the shop, funded by raid gold:

| level | energy pool | a 60-Draught is | cost to fill the pool | **in stamina-drains** |
|---|---|---|---|---|
| 500 | 1,888 | 3.179% of pool | 298,854g | 58.0 |
| 3,500 | 13,062 | 0.459% | 2,068,229g | 57.6 |
| 7,500 | 27,962 | 0.215% | 4,427,396g | 57.6 |
| 26,184 | 97,560 | 0.062% | 15,447,063g | 57.6 |
| 50,000 | 186,275 | 0.032% | 29,493,542g | **57.6** |

**Constant at every level.** Gold income per drain is linear in level and pool size is linear in
level, so the ratio cancels — the price does not decay with progression, which is exactly the property
a fixed gold price on a *percentage* potion would lack.

But it is set at **57.6 stamina-pool drains to buy one energy-pool refill**, which no player will ever
pay. And the flat 25/60 restore is the same fixed-against-linear defect as everywhere else: a Draught
is 3.2% of a pool at level 500 and 0.03% at level 50,000, so the number of potions needed grows
without bound while each costs the same.

So the shipped gold economy is not missing a price mechanism. It has one, it is the right shape, and
it is scaled ~57× beyond use. **That is the number a market has to be set against**, and it is a far
better anchor than a guess.

Raid gold per full stamina drain, 50/50 build:

| level | stamina pool | gold per drain |
|---|---|---|
| 500 | 936 | 5,149 |
| 3,500 | 6,524 | 35,881 |
| 7,500 | 13,974 | 76,856 |
| 26,184 | 48,773 | 268,250 |
| 50,000 | 93,130 | 512,215 |

**Gold per drain is linear in level. Gold per day from regen is a flat 1,584 at every level**, because
regen is flat (see `AUTOLEVELLING_AND_PACING_EVAL.md` Finding 4). So the faucet's real rate is set by
how many drains a player can fund — which is set by potions. **Potions are already the gold faucet's
throttle**, before any market exists.

## 2. A flat price is the wrong shape — the same defect as everywhere else

A 300,000 gold potion, measured in stamina-pool drains:

| level | 300,000g costs |
|---|---|
| 500 | 58.26 drains |
| 3,500 | 8.36 drains |
| 7,500 | 3.90 drains |
| 26,184 | 1.12 drains |
| 50,000 | **0.59 drains** |

**A 99× swing.** Unaffordable at 500, pocket change at 50,000. This is the identical constant-against-
linear mismatch as the quest cost cap, the Defense mitigation cap, the regen rate and the shipped
potions' flat restore — the fifth instance of one pattern.

**What a 25% potion is actually worth** also grows with level, which is the point of a percentage
potion:

| level | pool | 25% returns |
|---|---|---|
| 3,500 | 13,062 | 3,266 energy |
| 7,500 | 27,962 | 6,991 energy |
| 26,184 | 97,560 | 24,390 energy |
| 50,000 | 186,275 | 46,569 energy |

So the *good* is linear in level and the *price* would be constant. Any fixed number is wrong at all
but one level.

**Fix: price in pool-fractions, not gold.** A 25% potion listed at, say, "1.5 stamina-pool drains"
resolves to a gold number at listing time from the seller's level, or from a server-published index.
Then the price tracks the faucet automatically and stays meaningful forever. This is the same
"make the two curves the same shape" fix as everywhere else in this repo.

## 3. The transaction tax is the right instrument, and the only correct sink here

A percentage fee on purchase and sale is:

- **A scaling sink.** It grows with whatever the market bears, so it never decays like a fixed price.
- **Self-tuning against inflation.** More gold chasing goods raises prices, which raises the absolute
  tax take, which drains faster. It is a negative feedback loop rather than a fixed drain.
- **The only repeatable gold sink anyone would actually use.** Guild creation is one-off, crafting is
  small, and the consumable shop — the one genuinely repeatable sink that exists — is priced at 57.6
  stamina-drains per energy-pool refill, so it drains nothing because nobody buys from it.

Rates worth considering, given there is nothing else draining gold: a combined **10–20%** across both
sides is normal for this genre and would be doing real work here. Splitting it (e.g. 5% listing fee
paid up front and non-refundable, 10% sale fee) also discourages spam listings and price-probing.

**One caution.** A tax high enough to matter as a sink also suppresses trade volume, and a market
nobody uses bridges nobody. In a closed beta with few players that risk is much higher than in a live
game — thin markets are fragile. Start lower than the target and raise it, because raising a fee is
far easier to explain than lowering one and then needing it back.

## 4. The two-sided bridge — how it actually works, and what it costs

The mechanism the owner describes is real and well-proven (EVE's PLEX, RuneScape bonds, the WoW
Token): gems flow whale → F2P as goods, and time flows F2P → whale as goods, with gold as the clearing
medium. It genuinely lets a player with time and no money reach content otherwise gated by money.

It also changes ROTA's threat model in three specific ways.

**Every economy bug becomes an economy-wide event.** Today a duplication exploit inflates one account
and can be corrected by touching that account. With a market it launders into everyone's balance
within hours and cannot be unwound without a rollback. The four seams proven this week
(`216c985`, `a9ad926`, `3adb93a`, plus the gem ledger) stop being good hygiene and become
load-bearing.

**The Gauntlet gem bundle becomes a money printer.** It is already an open owner decision: its
idempotency reference is constant per account, so a second purchase charges nothing, grants nothing
and returns SUCCESS — and the drafted fix was deliberately reverted because the spec and the enum
disagree. Untested and unresolved, it is a curiosity. Behind a market, it is the exact shape of bug
that ends game economies. **This must be settled before a market ships**, not after.

**Gold acquires real-money value, which is what botting is for.** ROTA is async clicking — the most
automatable shape there is — and the design's own reference point is a community that ran NOX with
mouse scripts to stay supplied. A market makes automation economically rational rather than merely
tempting, and the payoff scales with the market's liquidity.

What that implies concretely, none of which exists yet:

- An append-only **trade ledger** with both sides, price, and tax taken — the audit tables already
  follow this pattern.
- **Per-account trade rate limits**, separate from the HTTP limiter, since the abuse is economic
  rather than volumetric.
- **A minimum account age or level to trade**, which is the cheapest anti-mule measure available.
- **Trade caps for new accounts**, to blunt farm-and-dump.

## 5. What the market does to the pacing curve

This is the part most easily missed. Potions are already the pacing lever past low level
(`AUTOLEVELLING_AND_PACING_EVAL.md` Finding 4). A market lets a player **convert gold into potions**,
which means gold becomes a second faucet into the same curve.

That is not automatically wrong — it is what gives grinding players a route to sustained play. But it
means the refund ratio R is no longer the only control on pacing: a player with gold can push their
effective R above what their Discernment earns them. **The autolevelling threshold becomes
purchasable.**

Note this is already *technically* true through the consumable shop — but at 57.6 stamina-drains per
energy-pool refill the exchange rate makes it irrational, so the pacing curve is not actually
threatened today. A market would set a real price, and a real price is what makes the interaction
bite. **The 57.6 figure is the safety margin that currently exists by accident**, and any market
pricing should be a deliberate choice about how much of it to give up.

If autolevelling is meant to be earned through raid SP (the shape recommended in the pacing eval),
then a market that sells potions for gold partially undoes that — unless the tax is steep enough that
buying is materially worse than earning. That is a design tension worth naming before it is built,
not a reason against it.

---

## Open questions for the owner

1. **Is the price a gold number or a pool-fraction?** Section 2 — a fixed gold price is wrong at
   every level but one.
2. **What is the combined tax rate**, and is it split across listing and sale? Section 3.
3. **Does the market sell potions at all**, given section 5? Restricting it to gear, magics and units
   would sidestep the pacing interaction entirely while keeping the whale/F2P bridge intact.
4. **Settle the Gauntlet gem bundle first.** Owner decision 2. It is a prerequisite, not a parallel
   task.
5. **Is there a bind-on-pickup concept?** Nothing in the item model marks an item untradeable today,
   and chase-set items reaching a market changes what the rare-drop curve means.

## What this document does not do

No schema, no endpoints, no pricing formula beyond the shape argument. It is an analysis of whether
the proposal fits the economy it would sit in, and the answer is: the tax does, the fixed price does
not, and the prerequisites are real but small.

---

## 6. What was built (2026-09-04) — and what it costs to run

System 27 ships as `5cdc3c1`, **disabled by default**. The shape is consignment, not trade: no
endpoint moves an object between two named players. Everything below is the honest list of what
turning `MarketConfig.Enabled = true` actually commits the operator to.

### The prerequisites from §4, and their status

| Prerequisite | Status |
|---|---|
| Append-only trade ledger with both sides, price and tax | **Built.** `market_transactions`, unique on `listing_id`. |
| Per-account trade rate limits, separate from the HTTP limiter | **Built as daily gold caps**, read from the ledger. Not a request-rate limit — the abuse is economic, and 50 requests that move 50M gold matter more than 5,000 that move none. |
| Minimum account age or level to trade | **Built.** Level 20, 48 hours. Verified live: a fresh account is refused. |
| Trade caps for new accounts | **Partially.** The caps are global, not tiered by account age. A tiered ramp is the obvious next step and is NOT built. |
| Settle the Gauntlet gem bundle first | **NOT DONE. Still open.** See below. |

### The three things that get worse the moment this is switched on

**1. The Gauntlet gem bundle becomes a money printer, and it is still open.** §4 named this a
prerequisite, not a parallel task, and it remains Owner decision 2. Its idempotency reference is
constant per account, so a second purchase charges nothing, grants nothing and returns SUCCESS.
Untested and unresolved it is a curiosity; behind a market it is the exact shape of bug that ends
game economies. **Do not enable the market before this is settled.**

**2. Every duplication bug becomes economy-wide within hours.** The market itself was pen-tested for
duplication (33/33, including eight concurrent buys and six concurrent cancels), but it launders
*other* systems' bugs. The four seams proven in `216c985`, `a9ad926`, `3adb93a` and the gem ledger
stop being hygiene and become load-bearing.

**3. Gold acquires real-money value, so botting becomes rational.** ROTA is async clicking, the most
automatable shape there is. Nothing in this prototype detects automation; the daily caps bound the
damage per account per day, which is a ceiling on the *rate*, not a defence. Budget for the
support load, not just the code.

### What the prototype deliberately does not answer

- **Consumables are not tradeable**, which sidesteps §5's pacing interaction entirely rather than
  pricing around it. That is a choice made *for* the owner and is trivially reversible in config —
  but reversing it re-opens "the autolevelling threshold becomes purchasable".
- **Gems are not tradeable at all**, so the whale-to-F2P bridge the original proposal described is
  NOT built. This market moves goods for gold between players. It does not bridge money to time.
- **Nothing is bind-on-pickup.** §5's open question 5 stands; `MarketConfig.Untradeable` is a
  by-id stand-in, not a model concept. Chase items reaching a market changes what the rare-drop
  curve means, and every Orange in the game is currently listable.
- **Fees are flat percentages, not a curve.** §2's argument that a flat gold price is the wrong
  shape does not apply to a percentage — but it does mean the *sink* scales with prices the market
  sets, which is the intended behaviour and also means the sink's size is not something the owner
  controls directly.

