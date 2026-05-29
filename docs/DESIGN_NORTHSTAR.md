# ROTA — Design North Star

*Version 0.1 — 2026-05-29*
*Owner: Xolaces (DEV_Xolaces)*

This is ROTA's design bible: the durable vision the backend (and later the Unity client)
is built toward. It sits **above** the research paper `dotdhistoryresearchpaper.pdf`.
The paper is an input, not gospel — where ROTA diverges from it, the divergence is
recorded here as a deliberate **amendment**, with the reasoning, so future-me and any
agent build toward *this* vision rather than the paper's defaults.

---

## 0. One-sentence thesis

**ROTA is an unapologetic grind-progression RPG where deep, time-invested account power
is the point — and the Gauntlet competitive ladder is the spine that gives the grind
meaning.** We keep Dawn of the Dragons' soul (social raids, collection depth, the long
climb, the community) and soften only the parts that punished *willing* players, while
preserving the friction the dedicated base actually loved.

---

## 1. Friction philosophy — keep it, but make it a throttle, not a wall

The research paper's headline prescription is "eliminate hard energy gates, combat always
free." **We amend this.** The operator (Xolaces) values the energy/stamina friction
*specifically* because it powers the consumable economy — the buy/use-a-potion-to-keep-going
loop is a feature, a resource sink, and a monetization lane all at once.

Our position:

- **Energy and Stamina bars stay.** They create healthy pacing, drive consumable demand,
  and give consumables (and premium refills) a reason to exist.
- **Regen accelerates with level / progression tier.** The wall softens as you grow — a
  felt sense of increasing freedom and power. Early game is tighter; veterans flow.
- **Consumables are the escape valve.** A willing player is never told "come back in 14
  hours." They can spend potions / refills (earned or bought) to keep playing *now*. The
  throttle has an off-ramp; it is not a dead gate.
- **No appointment misery.** We drop the anti-pattern the paper correctly identified:
  multi-hour real-time cooldowns that force you to log in on the game's schedule. Friction
  is about *resource spend*, not *clock-watching*.
- **The Gauntlet gets its own action currency (Strikes)** — the purest expression of the
  loved consumable loop, isolated to the competitive mode (see §4).

**The distinction that matters:** we keep *investment friction* (the grind to build power
via fodder, collection, and the level climb) and *consumable friction* (spend to keep
going). We remove only *dead-wall friction* (you physically cannot play and cannot pay to
play right now).

### Progressive pacing with estimated timelines
ROTA is deliberately paced: each progression band has a **target time budget** the designer
estimates and tunes (e.g., "band X is ~N active hours for an average player, less with
consumables, faster at higher regen"). Pacing is intentional and measured, not accidental.

---

## 2. Progression & the long climb — no resets

Operator data corrects the paper: the level ceiling was reached far more often than the
paper implies (the paper's figures are **understated** — multiple level-20,000 players
existed on a single server, with level-10,000 common). This **validates** ROTA's existing
convergence tiers running to **Eternal at L25,000** (Ancient L10k / ElderAncient L15k /
Eternal L25k). The long climb is real, intended, and prized.

**Amendment — we reject the paper's "3–4 month seasonal resets."** The long climb *is* the
pride and the veteran moat; wiping it would destroy the thing the dedicated base loves.
Instead we solve the "unbridgeable veteran gap" with:

- **Capped power bands** (see §3) so veterans/whales are stronger but *bounded*, keeping
  new and old players within a competitive band rather than a chasm.
- **Catch-up acceleration** for low levels (boosted XP / fodder / regen early).
- **"Tourist mode"** on old content so newcomers can experience the lore and earn without a
  power wall.
- **The Gauntlet ladder for the "reset feeling"** — leaderboards reset every cycle, giving
  fresh competition and seasonal rhythm, while **account power never wipes** (see §4).

---

## 3. Economy & monetization — generous floor, bounded ceiling

Two rails, both deliberate:

- **Floor (F2P / dolphin) — pity systems.** When we build gacha / mystery packs and item
  packs (Dawn-style), they ship with **pity** (guaranteed payout within N pulls) and
  published rates up-front. Anyone who invests *time* stays competitive without needing to
  mega-whale. (Keeps the paper's lockbox-reform guidance: pity, spark/exchange, transparent
  odds.)
- **Ceiling (whale) — capped vertical scaling.** Whale-facing scaling rewards remain real
  and purchasable — the **Infinite-Dawn-scaling-troops analog stays** — **but hard-capped.**

**Amendment — the cap is the valve Dawn never had.** The paper's single worst-named
anti-pattern is "Can't Catch Up" / infinite power creep (Infinite Dawn troops scaling
forever, every anniversary obsoleting the last). ROTA keeps the *appeal* of buyable vertical
power but bounds it so the gap between a pity-fed grinder and a mega-whale stays a **band**,
not a runaway chasm. Power cannot creep to "absolute unreasonable levels."

**Currency discipline (kept from the paper):** resist currency bloat. Target a tight set —
**one soft, one premium, one event currency** — plus tightly-scoped mode currencies
(e.g., Gauntlet Tokens) only where a mode genuinely warrants its own shop. No 30-resource
dead-inventory sprawl.

---

## 4. The Gauntlet — ROTA's competitive spine (core, not occasional)

In Dawn, the Gauntlet was a periodic competitive PvE event:
- Spend **Gauntlet Strikes** (a dedicated currency) to attack Gauntlet Raids; each raid
  defeated returns Strikes + **Gauntlet Tokens**.
- Strikes come from play, dailies, and **premium purchase** — the consumable throttle.
- **Level-banded leagues** (Whelpling 20–999 / Wyrm 1000–2499 / Dragon 2500+); you compete
  only within your band.
- **Top-500-per-league** rewards, some **permanent/prestige**.
- A **separate Battalion loadout** with paid expansion slots; a **Gauntlet Token shop**.

**Amendment — in ROTA the Gauntlet is core and recurring, not occasional.** Xolaces played
Dawn largely *for* the Gauntlet, running multiple accounts to win different brackets. ROTA
elevates it to a first-class, regularly-cadenced ladder (e.g., recurring cycles + periodic
grand brackets — exact cadence TBD), designed so:

- **Grinding account power directly translates to higher placement** → the carrot that makes
  "the goal is grind" pay off. Grind → place higher → prestige + tokens → grind more.
- **Level/power-banded brackets** (aligned to ROTA's convergence tiers) keep competition
  fair *and* support specialized/parked accounts — a deliberately supported playstyle.
- **Strikes** are the dedicated consumable currency (earned + buyable, within the §3 cap
  discipline so buying accelerates but does not trivialize the bracket).
- **Leaderboards reset per cycle** = the "season reset feeling" without touching account
  power (§2).
- **Permanent prestige rewards** for top placers; a **Gauntlet Token shop** as a parallel
  reward economy.
- **Separate loadout** once ROTA has the collection/troop layer (§5).

Status: **design pillar, Phase 2+/3 content.** No troop/battalion system, event scheduler,
or leaderboard service exists yet. Sketched here because it is central to the vision and
should inform foundational choices (e.g., contribution/scoring infra, level bands).

---

## 5. Collection & fodder — first-class pillar

The paper praises Dawn's "horizontal collection vector" (familiars, mounts, generals,
troops). ROTA makes collection + fodder a core pillar, combining:

- **Horizontal breadth** — a wide collection vector with set/collection bonuses.
- **Vertical fusion treadmill** — collectible units (troops / familiars / dragons) leveled
  and fused using lower-tier **fodder** units. Lower-tier collection feeds higher-tier
  power; **time investment compounds**.

This is where "fodder collecting and good time investment being crucial" lives. It is the
engine behind the grind, and the loadout source for the Gauntlet Battalion. Phase 2+.

---

## 6. Community & governance — build the community first

The paper's #1 lesson, **kept wholesale**: "build the out-of-game community first, because
that is what survives when the game itself does not." Dawn's studio spent ~25% of payroll on
community. ROTA's earliest infrastructure (closed beta via single-use keys, a dev/admin
account, staff roles) exists precisely to cultivate that core community before a client
exists.

**Governance principles (binding):**
- **Every punishment, by any role, against any player, is logged** — actor, role, target,
  type, reason, duration/expiry, timestamp. Append-only, like the audit log. Non-negotiable.
- **A reason must be stated before any ban or mute.** No reasonless punishments, ever.
- **Moderator role** (world-chat era): chat **mute**, **temporary bans up to 3 days**
  (reason required), and **staff reports** that escalate a **priority report to the admin
  (Xolaces)**. Admins retain permanent bans and full role management.
- **Roles are RBAC flags** (Player / Moderator / Admin), grantable/revocable by admins, with
  a last-admin lockout guard and immediate session revocation on demotion.

---

## 7. Amendments to the research paper — quick reference

| Paper's prescription | ROTA's decision | Why |
|---|---|---|
| Eliminate hard energy gates; combat always free | **Amend:** keep Energy/Stamina, soften with level + consumables; no dead walls | Consumable friction is a loved feature, a sink, and a revenue lane |
| 3–4 month seasonal resets to fix veteran gap | **Reject:** no account resets | The long climb is the prized moat; operator data shows L20k+ players valued it |
| Infinite vertical scaling is toxic (Can't Catch Up) | **Amend:** keep buyable vertical scaling but **hard-cap** it | Retains whale appeal *and* adds the anti-creep valve Dawn lacked |
| Level ceiling rarely reached; gap unbridgeable | **Correct:** ceiling reached often (L20k+ real); fix gap via caps + catch-up + tourist mode, not resets | Operator firsthand data; paper figures understated |
| Gauntlet was an occasional side event | **Elevate:** Gauntlet is a **core, recurring** competitive spine | It's the operator's primary play motivation; the carrot for the grind |
| Pity / transparent odds / spark on lockboxes | **Keep** | Sound F2P-floor design |
| Tight currency set (one soft / premium / event) | **Keep** (+ scoped mode currencies like Gauntlet Tokens) | Avoids dead-inventory bloat |
| Build out-of-game community first | **Keep wholesale** | The lesson that outlived the game |
| Damage-tier raid loot + live contribution leaderboards | **Keep** (already in ROTA's raid system) | Proven; the social-raid soul |
| 2×/4×/8× speed + sweep after first clear | **Keep** as a planned QoL layer | Removes appointment grind without removing the grind |

---

## 8. Build-order implications (non-binding pointers)

- Closed-beta access control (roles, beta keys, admin seed) — **in progress (System 12).**
- Moderation & punishment logging — **queued (System 13);** governance §6 is binding.
- Soft-gate / consumable / faster-regen tuning — after core loop content.
- Collection/fodder + troop/battalion layer — Phase 2+, prerequisite for full Gauntlet.
- Gauntlet ladder (scoring, level bands, strike currency, token shop) — Phase 2+/3.
- Gacha/mystery + item packs with pity — when monetization is built.

*This document supersedes the research paper wherever they conflict. Update the version
and date on every material change.*
