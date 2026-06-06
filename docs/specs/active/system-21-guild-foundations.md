# System 21 — Guild / Clan Foundations (design + research + slice plan)

*Drafted 2026-06-06. **STATUS: DESIGN — open decisions pending owner (see §5); fundamentals-first.***
Owner vision: build **fundamentals now** (identity, membership, roles, guild chat, **guild raids** as the
first cooperative content). **Guild campaigns** (seasonal co-op PvE) and **guild wars** (async GvG) are
the rewarding follow-ons — designed here at a high level, built as their own systems later. Everything
guild-related is **rewarding** (that is the retention thesis), but v1 = the social + cooperative spine.

This spec has two halves: **Part 1** is the cross-game "what a guild/clan really *is*" understanding the
owner asked for; **Part 2** turns it into a ROTA build. Read Part 1 to internalize *why*, Part 2 to build.

---

## PART 1 — What a "guild / clan" really is (across games)

A guild/clan is not a feature; it is a **persistent social container that converts solo players into a
group with shared identity, obligations, and goals that exceed any individual.** Its real job is
**retention**: it manufactures (a) *belonging/identity*, (b) *social debt* (you log in for your
guildmates, not the game), and (c) *goals too big to solo* (group bosses, rankings, wars). Every
mechanic below is downstream of that psychology — build the psychology, not just the tables.

### The nine layers (every mature guild system has some subset)

1. **Identity** — name, **tag/abbreviation** shown beside your name, crest/emblem/banner, description/
   charter, MOTD (message of the day), a public guild level/reputation. Identity = *status you wear*.
   (WoW tabards, Clash of Clans badges, Dawn of the Dragons guild tags.)

2. **Membership & hierarchy** — the social structure and its rules:
   - **Roles**, leader → officers/co-leaders → members → recruits. (Clash: Leader/Co/Elder/Member;
     WoW: fully custom rank ladder; EVE: granular corp roles.)
   - **Permission matrix** per role: invite, kick, promote/demote, edit settings/MOTD, accept
     applications, start/manage events, manage treasury, declare war, disband.
   - **Member cap** (often scales with guild level).
   - **Succession / inactivity** — the most-overlooked design point: what happens when the *leader* goes
     inactive. Clash auto-promotes the most active co-leader after a timeout; others require manual
     transfer. Without this, guilds die orphaned.

3. **Join models** — **open** (anyone), **application + approval** (apply → officer accepts; gate by
   level/score), **invite-only**, or request-with-auto-accept thresholds. Plus **discoverability**: a
   guild browser/search with filters (language, activity, min level, playstyle) and recommendations.

4. **Progression** — guilds **level up** from member activity/contribution, unlocking higher caps,
   **guild-wide perks/buffs** (XP%, gold%, resource%, stat boosts — "the guild as a power source"),
   cosmetics, more event slots, bank tabs. (WoW guild perks, gacha "guild research/tech trees,"
   Lords Mobile guild buffs.) This makes a *good* guild mechanically valuable, not just social.

5. **Economy / treasury** — shared bank/storage, a **guild currency** earned by activity,
   donations/contributions, and a **guild shop** to spend it. (Clash troop donations; gacha guild coins →
   guild shop.) Contribution tracking here = fairness.

6. **Cooperative (PvE) content — "do things together":**
   - **Guild bosses / guild raids** — a shared boss with a large HP pool the whole guild chips at;
     **contribution-based rewards**; usually daily/weekly with resets. *This maps 1:1 onto ROTA's existing
     raid + contribution-tier engine, scoped to the guild and paid with GuildStamina.*
   - **Guild campaigns / expeditions** — a multi-stage PvE track the guild advances **together over a
     season**, with milestone + ranking rewards.
   - **Guild quests / dailies** — shared objectives ("guild deals 10M damage this week") with collective
     rewards; drives daily logins.
   - **Help / donation** — low-friction reciprocity (Clash donations, "help" buttons) that builds habit.

7. **Competitive (PvP / GvG) content — "us vs them":**
   - **Guild wars**, models vary widely (pick by what fits an async game):
     - *Async attack/defense* (Clash war: each side a war map; members attack enemy bases for stars over
       a prep+battle window; most stars wins). **Best fit for an async RPG.**
     - *Siege / territory* (Summoners War Siege: place defenses on a map, attackers break through over
       days; capture buildings for guild buffs).
     - *Aggregate-damage race* (gacha guild war: both guilds hit shared bosses; higher total wins the
       bracket). **Simplest fit; reuses the raid engine.**
     - *Real-time battlefield* (MMO open-world GvG, EVE sov) — **out of scope** for async ROTA.
   - **Matchmaking & seasons** — guilds matched by rating/tier; ladder with placement rewards;
     promotion/relegation.
   - **Territory / control** — capturable nodes granting guild-wide buffs while held (persistent stakes).

8. **Communication** — a **guild chat channel** (non-negotiable), MOTD, roster with last-online/activity,
   announcements, an events calendar. (ROTA already has a SignalR `ChatHub` — a guild channel is a
   natural add alongside world/raid.)

9. **Fairness & anti-abuse** — **contribution tracking** (rewards reflect effort, not freeloading),
   kick-inactive tooling, war opt-in/lineup (so AFKs don't sink the team), anti-poaching, alt/multi-acc
   guards, war-dodge penalties.

### Why guild systems FAIL (design these out from day one)
- **Leader abandonment → orphaned dead guilds.** → succession + leadership transfer + inactivity
  auto-promotion.
- **Freeloaders.** → contribution tracking + activity minimums + contribution-gated rewards.
- **Pay-to-win guild buffs.** If guild power is whale-funded, it warps competition. **Tie progression to
  activity, not spending** — this is mandatory for ROTA (capped-scaling North Star).
- **Matchmaking blowouts / dead-guild spiral.** New/small guilds get crushed → churn. → tiered brackets +
  size normalization.
- **Scheduling/timezone pain.** Real-time GvG punishes async audiences. → **favor async war windows.**
- **Loot/rank drama.** → transparent contribution + crisp permission rules.

### The ROTA lens (how the above constrains our design)
ROTA is an **async, server-authoritative, capped-scaling** RPG. Therefore the guild model must be:
- **Async cooperative content** (guild raids paid with **GuildStamina**, hit on your own schedule).
- **Contribution-based rewards** that **reuse the existing raid contribution-tier engine** (Legendary
  1/2/3 → Epic → Rare → Participant).
- **Progression tied to member activity, not pay-to-win** (fits the capped-scaling vision).
- **Clear role/permission hierarchy with inactivity succession** (no orphaned guilds).
- **Wars are async** (aggregate-damage or attack-window), never real-time.
- **Composed from existing systems**, not parallel ones (see §2 "what exists").

---

## PART 2 — ROTA Guild build (fundamentals)

### What already exists to build on (reuse, do not rebuild)
- **`Player.GuildId` (Guid?) + `Player.GuildRank` (string?)** already on the entity (currently unused) —
  the denormalized "what guild am I in" hooks.
- **`GuildStamina`** is already a player resource (`ResourceType.GuildStamina`, `MaxGuildStamina = level`,
  regenerates) with **no sink** — guild raids are its reason to exist. Spend via the existing
  `IEnergyService` (`SpendEnergyAsync(playerId, ResourceType.GuildStamina, amount)`).
- **Raid + contribution engine** — `ActiveRaid` (`Tier` already includes a `Guild` value), `RaidParticipant`,
  `RaidService.HitRaidAsync` (server-seeded RNG damage, contribution tiers, cumulative-threshold loot,
  reward atomicity, idempotency cache). Guild raids = this engine, scoped to a guild + GuildStamina cost.
- **SignalR `ChatHub`** (`/hubs/chat`, world + raid groups, mute gate, `SubUserIdProvider`) — add a guild
  group/channel the same way.
- **Leaderboards** (`system-17`) — guild leaderboards are a later add over the same aggregate pattern.
- **Audit + admin + validation + idempotent-ledger patterns** — all established (see CLAUDE.md).

### Proposed data model (fundamentals) — *snake_case; id/created_at/updated_at/is_deleted; FKs indexed; private setters; Fluent configs*
- **`Guild`** — `Id, Name (unique), Tag (unique, short 2–5), Description, CrestId (string), LeaderId (FK player, idx),
  Motd, MemberCap, Level, Xp, MemberCount (denormalized), created_at/updated_at/is_deleted`.
  Methods: `Create`, `Rename`, `SetTag`, `SetDescription`, `SetMotd`, `SetLeader(playerId)`, `AddXp`, `Disband`.
- **`GuildMembership`** — `Id, GuildId (FK idx), PlayerId (FK, **unique** — one guild per player), Rank
  (GuildRank enum), ContributionTotal (long), JoinedAt, created_at/updated_at/is_deleted`. Unique
  `(player_id)` partial where `is_deleted=false` (re-join after leave, mirrors the friendship partial-index
  lesson). Keep `Player.GuildId`/`GuildRank` in sync (denormalized) for O(1) reads.
- **`GuildJoinRequest`** — `Id, GuildId (FK idx), PlayerId (FK idx), Kind (Application|Invite), Status
  (Pending|Accepted|Rejected|Withdrawn|Expired), created_at/updated_at/is_deleted`. Application = player→guild
  (officer accepts); Invite = officer→player (player accepts).
- **`GuildRank` enum** — `Member=1, Officer=2, Leader=3` (optionally `Recruit=0`). Permission checks compare rank.
- **Guild raids** — add nullable `guild_id` (FK, idx) to `active_raid` (migration), stamped at summon;
  hit access gated to guild members; hit cost = **GuildStamina**; rewards via the existing engine +
  `GuildMembership.ContributionTotal` accrual. (Could also add a `GuildContribution` ledger later for
  per-event fairness; v1 can use the running total.)
- **Audit** every state change: create/disband, join/leave/kick, promote/demote, transfer, raid summon.

### Permission matrix (v1)
| Action | Member | Officer | Leader |
|---|---|---|---|
| Chat, hit guild raids, contribute | ✓ | ✓ | ✓ |
| Invite / accept applications / kick recruits & members | | ✓ | ✓ |
| Promote/demote (below own rank), set MOTD, summon guild raid | | ✓ | ✓ |
| Kick officers, rename/tag/description, transfer leadership, disband | | | ✓ |

### Proposed slices (build order; each: build + tests green + commit, never bundle)
- **Slice 1 — Guild core + membership + join flow.** `Guild`/`GuildMembership`/`GuildJoinRequest`
  entities + configs + migration; `GuildService` (create/disband, apply/accept/invite/accept-invite,
  leave, kick, promote/demote, transfer leadership, **inactivity succession**, roster); `GuildController`
  `[Authorize]` + DTOs + FluentValidation; one-guild-per-player guard; name/tag uniqueness; audit. Tests.
- **Slice 2 — Guild chat + identity polish.** `ChatHub` guild channel (`JoinGuild`/`SendGuildMessage`,
  member-gated, mute-gated) + history; MOTD/announcements surfaced. Tests on the store/gate.
- **Slice 3 — Guild raids (first cooperative content).** `active_raid.guild_id`; officer-gated summon
  (cost TBD — see §5); guild-member hit gate; **GuildStamina** spend; contribution-tier rewards via the
  existing engine; `GuildMembership.ContributionTotal` accrual; guild-raid list. Tests (access gate,
  GuildStamina spend/insufficient, contribution accrual, reward idempotency).
- **Slice 4 (optional in v1) — Guild progression.** Guild XP/level from member activity → member-cap
  bumps + 1–2 modest **activity-funded** perks (NOT pay-to-win). Tests.

### API surface (v1, indicative)
`GET /api/guilds` (browse/search) · `POST /api/guilds` (create) · `GET /api/guilds/{id}` (detail+roster) ·
`POST /api/guilds/{id}/apply` · `POST /api/guilds/{id}/requests/{reqId}/accept|reject` ·
`POST /api/guilds/{id}/invite` · `POST /api/guilds/invites/{reqId}/accept` ·
`POST /api/guilds/{id}/leave` · `POST /api/guilds/{id}/members/{playerId}/kick|promote|demote` ·
`POST /api/guilds/{id}/transfer` · `PUT /api/guilds/{id}` (name/tag/desc/motd) · `POST /api/guilds/{id}/disband` ·
`POST /api/guilds/{id}/raids/summon` · `GET /api/guilds/{id}/raids`. (Chat over the hub.)

---

## PART 3 — Roadmap (design now, build as their own systems later)

- **Guild Campaigns (future system)** — a multi-stage seasonal PvE track the guild advances *together*
  (clear stages → accrue guild progress → milestone + ranking rewards). Reuses quest/raid content + a
  guild-progress ledger. Drives sustained collective goals between Gauntlets.
- **Guild Wars (future system)** — **async GvG**. Recommended v1 model: **aggregate-damage bracket** (two
  matched guilds hit shared war bosses over a window; higher total guild damage wins; placement rewards)
  — cheapest, reuses the raid engine, no scheduling pain. Evolution: **attack-window / siege** with
  capturable territory granting guild-wide buffs. Needs matchmaking by guild rating + seasons. All rewards
  flow to the guild treasury + members by contribution.
- **Guild leaderboards, treasury/guild-currency + guild shop, guild perks tree** — layer on as the
  competitive + economy depth grows. Keep all power **activity-funded**, per the capped-scaling North Star.

---

## PART 4 — Relationship to System 16 (Gauntlet)
Guilds and the Gauntlet are the **two competitive/cooperative pillars** and they reinforce each other:
the Gauntlet is **individual** competition (your power → your placement); guilds are **collective**
(shared bosses, later wars). The same character power feeds both. Build the **Gauntlet first or in
parallel** — its spec is decision-locked (`system-16-gauntlet.md`); guilds need the §5 decisions resolved
before Slice 1. They share zero tables, so they can progress independently.

---

## PART 5 — OPEN DECISIONS (resolve with owner before Slice 1)
1. **One guild per player?** (recommend **yes** for v1.)
2. **Member cap** — fixed (e.g. 50) or scales with guild level? (recommend start fixed, scale in Slice 4.)
3. **Default join model** — application+invite (recommend) vs open vs leader's choice per guild.
4. **Role tiers** — 3 (Leader/Officer/Member) or 4 (add Recruit)?
5. **Guild creation cost / gate** — gems? gold? min level? (prevents spam; recommend a gem or gold cost + min level.)
6. **Inactivity succession** — auto-promote the most-active officer after N days of leader inactivity? (recommend yes, N≈7–14.)
7. **Guild raid summon** — who (Officer+?) and what gates it: a **guild sigil**, gold/gem cost, or a
   cooldown? And **GuildStamina cost per hit** (size 1/5/20 → ?).
8. **Contribution metric** — damage dealt, GuildStamina spent, or a points blend? (recommend damage, reusing the raid tiers.)
9. **Guild leaderboards** in v1 or deferred? (recommend deferred to after fundamentals.)
10. **Disband semantics** — leader-only; members released to guild-less; cosmetics/treasury fate.
11. **Name/tag rules** — length, uniqueness (case-insensitive), profanity/reserved-name filter (reuse the
    reserved-username validator).

---

## Constraints (every slice — binding, mirrors the Gauntlet/Legion specs)
- Domain entities: private setters, no EF attributes; state via methods/factories.
- EF Fluent only, snake_case; every table `id`/`created_at`/`updated_at`/`is_deleted`; FKs indexed. Heed
  the **EF enum + store-default `HasSentinel`** rule for enum columns with non-zero defaults. Use a
  **partial unique index** where soft-deleted rows can recur (the friendship lesson).
- Server-authoritative; controllers thin; `PlayerId` from JWT `sub`. All inputs FluentValidation'd before
  the service layer. Every state change writes `audit_log`. Idempotent ledgers where value is granted.
- Reuse the raid/contribution engine + chat hub + energy (GuildStamina) — **no parallel combat path.**
- Branch off `main`; build 0 warnings; `dotnet test` green (Docker up) before commit; migrations added but
  coordinate `database update`; **no co-author**; one branch/merge per slice; don't push until owner says.
