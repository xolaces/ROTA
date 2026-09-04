# Agent backlog — the self-supplied work queue

This file IS the queue. It survives context loss; conversation memory does not. Any session picking
up unattended work starts here and finishes here.

## Protocol

1. Read this file top to bottom before doing anything else.
2. Take the highest-ranked item in **Ready** that is not `BLOCKED`.
3. Do it. Verify it (build + tests, or an explicit calculation shown in the artefact).
4. Move it to **Done** with the commit sha and one line on what actually changed.
5. If the work reveals new work, add it to **Ready** ranked, or to **Owner decisions** if it needs a
   human call. Never silently expand an item's scope — split it instead.
6. Commit. Small commits, explicit paths.
7. **Stop the running API before building.** A `dotnet run` instance on port 5035 holds
   ROTA.Application.dll and ROTA.Infrastructure.dll, so `dotnet build` fails with MSB3027/MSB3021
   file locks — and `dotnet test --no-build` then passes against the STALE binaries, which reads as
   a clean run. Stop it, build, test, restart it:
   `Get-Process ROTA.Api | Stop-Process -Force`

## Standing rules that override any item here

- **Core game fundamentals never change.** Stat allocation is the point of the game. The energy wall
  is intended design. Server authority is not negotiable. If an item seems to require changing a
  fundamental, it becomes an Owner decision instead.
- **`git add -A` and `git add src` are forbidden.** They sweep the held content JSON into commits.
  Stage explicit paths, every time. (This has happened twice and had to be amended out both times.)
- **The owner applies production migrations.** Local dev auto-migrates in Development; the droplet
  does not.
- **The Application layer never touches EF Core, Redis, or the database directly.**
- **`xolaces/ROTA` is public.** A commit describing a weakness that is FIXED in that same commit is
  safe to push. One describing a weakness still LIVE is not. Three branches stay held and unpushed:
  `fix/forwarded-headers-prod-guard`, `docs/state-reconciliation`, `docs/security-sweep-2026-08-26`.
- **No Dawn of the Dragons art in the asset tree.** Kabam / 5th Planet IP.
- **UI copy voice:** say the fact, not the explanation. Short and human.
- Verify every claim against the code before writing it down. Prove a test catches a defect by
  temporarily neutering the fix and watching it fail, then restore.

---

## Ready — ranked

> **Note for an autonomous tick (2026-09-04): R0 and R4 live in `C:\Dev\ROTA.Client6`, not here.**
> The tick protocol's gate is `dotnet build ROTA.slnx` + `dotnet test tests/ROTA.UnitTests`, and
> neither compiles a line of Unity, so an agent cannot VERIFY either item under the rule it is given.
> R0's actual fix is also an Inspector checkbox, which is not a code change at all. Both need the
> owner, or a tick with a Unity headless-compile gate added to the protocol. They stay ranked because
> they matter; they are simply not takeable here.

### R0. Client runs in MOCK mode — the playtest never touched the backend
`AppBootstrap.useMock` defaults to `true` and the scene's serialized value wins over the code default,
so `Assets/Scenes/Main.unity` starts on canned data. The console says `[ROTA] client started (MOCK).`
and the profile shows DEV_Owner at Lv 2498 with 24.8M gold — none of it from the API.

Fix is one checkbox: select `UIDocument` in the Hierarchy, Inspector → AppBootstrap → Backend →
uncheck **Use Mock**. The log line becomes `[ROTA] client started (http://localhost:5035).`

Worth doing properly rather than leaving as a checkbox: `ApplyConfigOverrides()` already reads
`ROTA_USE_MOCK` / `ROTA_BASE_URL` and a `rota-config.json`, but neither reaches the Editor
conveniently. Consider an editor-only default of live-when-a-backend-answers, or a visible on-screen
badge when mock is active — the current failure mode is silent and cost a whole playtest window.

### R1c. The Gauntlet shop is the last untested economy seam — BLOCKED on an owner call
Gems (`216c985`), raid loot (`a9ad926`) and quest rewards (`3adb93a`) are all now proven under
contention. The Gauntlet shop is the remaining one, and it cannot be tested yet: its gem-bundle
repeatability is an open owner decision (see below). A test written now would pin whichever behaviour
happens to exist rather than the intended one, which is worse than no test.

**BLOCKED on Owner decision 2.** Once settled, reuse the QuestRewardConcurrencyTests shape and
neuter the guard to confirm the test fails for the right reason.

### R4. Client support for nine-rung ladders
The zone ladder went 6 → 9 rungs and raids gained a 9-rung per-raid ladder
(`AchievementCategory.RaidMastery`). Check `C:\Dev\ROTA.Client6` for anything that assumes six tiers,
a rarity-keyed achievement id, or a fixed-height achievement list. The ids changed shape:
`ach_zonererun_c1z0_grey` → `ach_zonererun_c1z0_t10`.

### R6b. Discernment still has no SCALING sink — the retune deferred the problem
`f59e481` moved the crit and rare-drop caps out 100x. That is "extend the fixed sink", not "make the
sink scale", so the same value erosion recurs at 100x the Discernment: crit saturates at 500,000
against an endgame of 15,000,000–100,000,000.

The genre answer (Cook) is a sink that scales with the source rather than a larger fixed one. ROTA has
exactly one textbook-correct example to copy: the Gauntlet HP curve,
`H(n) = StageHpBase x StageHpGrowth^(n-1)` at 1.0493 — which is in the same band as the 1.07 the
literature quotes for AdVenture Capitalist.

This is a DESIGN question, not a defect, and it interacts with Owner decision 6 (whether the
early-game crit nerf stands). Do not invent a new sink autonomously — write up options with numbers
and let the owner choose. Evidence in `docs/research/ECONOMY_VS_GENRE_STANDARDS.md`.

### R7b. Which drops should use the asymptotic curve rather than the capped multiplier
The generic Discernment drop multiplier — `base x (1 + D x 0.03)` capped at 0.95 — saturates at 3,133
Discernment for a 1% drop and at 30 for a 50% drop, against an endgame of 15M–100M. Rare-scaling drops
deliberately skip it for the asymptotic curve, so the chase set is handled; everything unflagged is
not.

Audit which loot entries carry `rareScaling` today, and decide whether the unflagged ones should move
to the asymptotic curve as well. Include the raid-side twin, `MaxThresholdDropChance` (0.95), which is
the same shape driven by Hoard mastery rather than Discernment — Hoard's percentage curve was not read
during the sweep and needs quantifying the same way.

This is a content + balance decision, not a pure defect: some drops may be *intended* to reach their
ceiling early. Bring numbers, do not re-flag content autonomously. Evidence in
`docs/eval/SATURATING_SINKS_SWEEP.md`, Finding 2.

### R10. Is 2,000,000 Discernment reachable near level 7,500?
Open tuning question left by the pacing eval. If quest cost tracks the pool, R depends only on
Discernment (0.366 floor -> 1.025 ceiling), which matches the owner's stated intent — autolevelling
available past 7,500, earned rather than reached. Whether the ceiling ANCHOR is right then depends on
whether ~2M Discernment is realistically held around level 7,500.

At 300 SP/raid that is ~6,667 raids. Nothing in this repo maps raids to level, so this needs either a
play-rate assumption from the owner or telemetry from the beta. If 2M lands far past 7,500 the anchor
moves down; the SHAPE is right either way, which is the good position to tune from.

### R11. The tutorial's "four passes" line is exact and nothing pins it
`docs/design/TUTORIAL_OPENING.md` beat 2 tells the player four attempts of `q001` will level them.
That is exact arithmetic — 4 x 7.5 XP = 30 = TNL(1) — not a rounded estimate. Retuning `q001`'s
5-energy cost, `XpPerEnergyRoll*` (1.5), or the level-1 XP curve makes the line a lie, and it is the
kind a player checks on their first four clicks.

Either derive the number at runtime from the same config the server uses, or pin it with a unit test
asserting `ceil(TNL(1) / (q001.BaseEnergyCost x XpPerEnergyRollMin)) == 4`. The test is cheaper and
catches it at build rather than in a player's first minute.

---

## Blocked

*(B1 cleared 2026-09-04 — see Done.)*

---

## Owner decisions — the agent must not decide these

0f. **Should the market be switched on, and what must be settled first?**
   System 27 ships in `5cdc3c1` with `MarketConfig.Enabled = false`. Three calls belong to the owner:
   **(i)** the Gauntlet gem bundle (Owner decision 2) is a STATED PREREQUISITE in
   `docs/design/PLAYER_MARKET.md` §4 and is still open — its idempotency reference is constant per
   account, so a second purchase charges nothing and returns SUCCESS. Behind a market that is a money
   printer. **(ii)** the combined fee is 2% + 8% = 10%, the bottom of the band the design doc calls
   normal; raising it is easy, lowering it and needing it back is not. **(iii)** consumables and gems
   are NOT tradeable, which sidesteps the pacing interaction and declines to build the whale/F2P
   bridge. Both are config, both are reversible, and both change what the market is for.

0g. **Is there a bind-on-pickup concept?**
   Nothing in the item model marks an item untradeable, so every Orange in the game — the Armory
   relics, Pano's set — is listable the moment the market opens. `MarketConfig.Untradeable` is a
   by-id stand-in. The question `PLAYER_MARKET.md` open question 5 raised is now live rather than
   theoretical: a chase item that can be bought changes what the rare-drop curve means.

0e. **Should raid threshold gear roll against its chance, or stay unconditional?**
   `RaidService.DistributeKillRewardsAsync` grants threshold gear with **no chance roll** — the
   comment states it deliberately: *"a GUARANTEED drop, so it is intentionally NOT Hoard-scaled."*
   Threshold rewards are also cumulative, so gear on rung *n* is granted once for every rung at or
   below the player's damage. Together those mean a `chance` value on a `GearDropChance` inside a
   RAID table is **dead data** — the field exists, the JSON carries it, nothing reads it. (In QUEST
   tables the same field IS read, which is what makes this a trap.)
   The 26 raid tables written on 2026-09-04 now put exactly one piece on the last rung at chance
   1.0, so the JSON states what the engine does. That is a workaround, not a decision. The call:
   **(a)** leave it unconditional and treat one top-rung piece as the raid's gear reward, **(b)**
   make raid gear honour `Chance` like quest gear does, so a raid can carry a chase piece, or
   **(c)** drop `Chance` from `GearDropChance` on the raid path so the dead field stops inviting
   the mistake. (b) is the only one that lets a stamina build chase gear the way an energy build can.

0d. **Regen is flat while pools grow linearly, so nothing paces play past low level.**
   `RegenMinutesPerPoint` grants one point per interval regardless of level, so daily regen is a
   constant 288 while the pool it fills is linear in level. A level costs 5.4 days at level 500 and
   92.6 days at 7,500 on regen alone; the "little or no waiting" early game exists only below roughly
   level 200. Potions are therefore not a supplement to the pacing curve past low level — they are
   the whole curve. Four options (proportional regen, level-scaled interval, accept potions as the
   lever, or target R at 0.6-0.8) are in `docs/eval/AUTOLEVELLING_AND_PACING_EVAL.md` Finding 4.

0c. **Defense is a dominated stat — there is no build where it is the right allocation.**
   It is weighted 1x against Attack's 4x in both raid damage (`RaidService.cs:918`) and Gauntlet
   battalion power (`GauntletBattalionService.cs:140`). Its only unique role, Gauntlet damage
   mitigation, saturates at **800 Defense** and protects a resource that gates nothing —
   `RaidService.cs:899` clamps health at 0 and never blocks a hit. Ordinary and guild raids have no
   Defense mitigation at all.

   This matters more than an ordinary balance number because stat allocation is the stated point of
   the game, and this makes the Attack/Defense half of it not a choice. Four options with trade-offs
   are in `docs/eval/SATURATING_SINKS_SWEEP.md`; only enabling the 0-health gate AND giving mitigation
   a scaling curve makes Defense real, and the gate means a third pool starts rationing play.

0b. **There is no prestige mechanic, and the literature treats one as the standard release valve.**
   ROTA has mastery (additive, permanent) and one-way stat allocation with no respec — nothing that
   resets any curve. The idle-game literature treats prestige as the normal answer once late-game
   costs make progression prohibitively long, which is the exact shape ROTA's late game has. A
   Dawn-faithful async RPG may deliberately not want one; the point is that its absence should be a
   decision rather than an omission. See `docs/research/ECONOMY_VS_GENRE_STANDARDS.md`.

0. **The potion refund ratio has the wrong SHAPE — decide before it is built.**
   *(Reframed 2026-09-03 after the owner clarified that autolevelling is wanted past 7,500. The
   original framing — "the design ends the energy economy" — was written assuming autolevelling was a
   defect. It is not; the ratio is simply mis-sited.)*

   Against the stated intent the design is too STINGY early and unbounded late: at level 7,500 a
   50/50 player gets R = 0.287 where ~1.0 is wanted, then R climbs to 3.8 by level 50,000. Both halves
   are one cause — R is proportional to level because the pool grows and quest cost caps at chapter
   16. Letting cost track the pool removes the level term and leaves R depending only on Discernment
   (0.366 -> 1.025), which matches the intent by construction and makes autolevelling earned through
   raid SP. See `docs/eval/POTION_ECONOMY_EVAL.md` and `AUTOLEVELLING_AND_PACING_EVAL.md`.

   ORIGINAL NOTE, still true: quest cost caps at chapter 16 while the pool grows forever, and below
   the 200-energy gate the cost cancels out of the ratio entirely, so no choice of quest avoids it.
   `QuestConfig.ChapterScalingCap = 16` caps quest energy cost at chapter 16 while the pool grows
   linearly with level forever, so the refund ratio crosses 1.0 and keeps climbing. Below the
   200-energy gate the cost cancels out of the ratio entirely, so no choice of quest avoids it and a
   player who never invests in Discernment still crosses at 36,716. Four options with trade-offs are
   in `docs/eval/POTION_ECONOMY_EVAL.md`; only "make cost track the pool" removes the term rather
   than bounding it, and it is what the design doc already assumed was true. This is a core-design
   call about how quest cost works, so the agent will not pick.

Full context in `docs/EVALUATE_LATER.md`. Summarised here so the queue is self-contained:

1. **Should zone Guardians drop items at all?** 23 of 25 raids carry no loot table. Empty is a
   supported state, not a defect — the loot pass short-circuits before `GetById`.
2. **Can a Gauntlet gem bundle be bought more than once?** Its idempotency reference is constant per
   account, so a second purchase charges nothing, grants nothing and returns SUCCESS. A fix was
   drafted and deliberately reverted: `GauntletShopIdempotencyTests` pins buy-twice-charges-once as a
   System 16 Slice 6 spec requirement while the enum calls the entry repeatable. Both cannot be right.
3. **How long should a guild invite live?** `GuildJoinRequest.Expire()` has no callers and no time
   field to drive it, so a Pending invite lives forever and silently blocks re-invitation.
4. **A retention window for four unpruned tables.** Refresh tokens need a WINDOW, not a purge —
   deleting them silently breaks replay detection. Also the mastery/achievement idempotency ledgers,
   and whether to materialise a balance column instead of SUMming the lifetime ledger on every
   profile read. No purge job exists anywhere in the repo.
5. **Gauntlet settlement is now automatic** — prizes go out on a timer with no human review. Say if
   it should auto-close only and leave payout manual. One-line change.
6. **Does the early-game crit nerf stand?** Moving crit saturation out 100x means 1,000 Discernment
   is now worth +0.1pp instead of a capped +10pp. Endgame is correct; early game got quieter.
7. **Block scope** — PMs only, or no contact at all.
8. **Developer leaderboard eligibility** — in or out.

---

## Done

- `643b926` — **R5d: the partial index, as a migration.** `ix_ap_player_incomplete` on
  `achievement_progress (player_id) WHERE NOT is_completed AND NOT is_deleted`, added to the EF model
  and generated with `dotnet ef migrations add` so the snapshot stays in step (MigrationSnapshotTests
  green is what proves it). The benchmark it implements: reads 1.662 ms -> 0.096 ms, writes +10.9 us
  per incremented row, 432 kB against the 56 MB composite. NOT APPLIED — fifth pending. The migration
  carries the live-database procedure in its doc comment, including that a re-run over an existing
  index FAILS rather than no-ops, so the CONCURRENTLY route needs the history row written by hand.
- `5cdc3c1` — **System 27, the player market**, off by default. Consignment only: no endpoint moves
  an object between two named players. Escrow at listing, a status latch before any gold moves, an
  atomic seller credit (new `IPlayerRepository.AddGoldAsync`), daily gold caps read from an
  append-only ledger, and an expiry sweep so a lapsed listing gives the stack back. 22 unit tests
  plus a 33-case live pen suite (`tools/pentest/market_pentest.py`): 8 concurrent buys -> 1 winner
  and 1 debit; 6 concurrent cancels -> 1 success and the stack back once; gold conserved with the
  fee destroyed. Raised Owner decisions 0f and 0g.
- `d40dc84` — **Raid gear was four guaranteed copies a clear.** My own defect from `66808bf`: I put
  gear on the top four of eight cumulative rungs with a `chance` field the raid path never reads, so
  a Mythic clear paid 4x a Purple mount and 4x an ORANGE relic, every time. Now one piece on the last
  rung at chance 1.0, never Orange. The two hand-authored raid tables are byte-untouched. Raised
  Owner decision 0e.
- `3b0d2b1` — **A relic item stopped being rare at 63,300 Discernment.** `RareScaling` was on
  `GearDropChance` and not on `ItemDropChance`, so every item chance-drop used the generic
  `base x (1 + 0.03d)` curve clamped at 0.95. A 0.0005 relic hits that clamp at
  `d = (1900-1)/0.03 = 63,300`. Added the flag and honoured it; four tests, one of which asserts the
  OLD behaviour so the trap stays visible. Neutered: 2 of 4 fail.
- `66808bf` — **Every zone and raid now drops something, with a reason.** 134 quest nodes and 26
  raids had no loot table at all. +56 tables. Road reagents (energy/quests) and Field reagents
  (stamina/raids) never appear in each other's tables, so no single pool buys the other's materials.
  Verified live: cleared ch1 z0 against the running API and the Ashen Causeway paid `mat_causeway_ash`
  x144 off the new `lt_zone_c1z1`.
- `369e441` — **B1 cleared.** The held Black Archive nodes and raids are committed (separately, so
  they stay easy to drop), and `q_lastwatch_boss` got the sigils map it was missing — it declared
  `sigilDropChance: 0.2` against a null map, which the content validator refused, blocking all
  content verification.
- `398f134` — **59 new items.** A full Green/Blue/Purple gear ladder across all 8 slots (the game had
  8 Grey, 1 Blue, 8 Orange and nothing between), six Armory relics from Master Canon XIX, two reagent
  lines, the upper draught rungs, two stat-bag rungs, four Black Archive sigils.
- `e9ce608` — **A large `page` 500'd three endpoints.** `(page - 1) * pageSize` in int arithmetic
  overflows at page 10,737,420 against the shipped PageSize 200; PostgreSQL then refuses the negative
  OFFSET. Reproduced live on `/api/leaderboards` and `/api/guilds`, fixed in one shared helper,
  re-verified live.
- (R9) — **E/S parity pinned to all four constants that create it**, not the two the item named: the
  LSI weight, stamina's skill-point price, and both XP rates. Tests read the config rather than
  repeating it. Verified by retuning each constant alone — every one breaks parity. Also collapsed
  the LSI stamina weight from a literal duplicated in `PlayerStats` and `StatService` into one
  `PlayerStats.StaminaLsiWeight`.
- `8e94cf4` — **R12: the pool-cap refusal now teaches.** Was a cap constant and a current value with
  nothing to act on; now states how many points fit at this level, that the ceiling rises, and — only
  when it is actually biting — that Stamina counts twice. Five tests assert the count against the cap
  arithmetic rather than a fixed string, so retuning 7.45 does not falsify them. Verified by
  restoring the old message: six tests fail.
- `91c3980` — **stamina now costs 2 SP.** It counts double toward the LSI cap but charged 1, so a
  stamina build reached the same ceiling for half the skill points and banked the rest in
  Attack/Defense/Discernment — strictly better by exactly 2x. Price moved to
  `LevelingConfig.SkillPointCostByStat`, every `AllocateToX` now takes the charge explicitly so a
  future price cannot silently no-op, and Energy 1 / Stamina 2 is recorded as owner-locked. Verified
  by reverting the price: the parity test reports 372 SP against 745.
- `223dcd2` — SP cost-curve options, the tutorial opening design, and a correction: my earlier "all
  E:S splits are equal" finding was true per LSI point and false per skill point.
- `2d7ae80` — **R8 numeric-headroom sweep, and the one real find fixed.** All the big accumulators are
  already `long` (TotalDamageDealt, GauntletEntry.Score, gem ledger) and the narrowing casts are safe
  by construction. The exception: `GauntletConfig` had NO validation, and raising `MaxLadderStage` from
  250 to 300 overflows `long` by ~4,000x — silently, because the double→long cast is undefined and
  yields a garbage MaxHp that reads as a timer-only raid. Now a boot guard with tests.
- `a55a226` — **saturating-sinks sweep.** Found that Defense is strictly dominated by Attack
  everywhere: 1x vs 4x in damage, its only unique role (Gauntlet mitigation) capped at 800 Defense,
  and the health it protects gates nothing. Now Owner decision 0c. Also found the generic Discernment
  drop multiplier saturating between 30 and 3,133 Discernment (now R7b). Full inventory of every
  capped sink in the document.
- `9fbfab8` — **economy cross-checked against published genre standards.** The potion design is the
  worst shape the literature names (constant sink vs linear source); the Discernment retune is
  "extend the fixed sink" rather than "make it scale" (now R6b); no prestige mechanic exists (now
  Owner decision 0b). Corrected my own error mid-write: the Gauntlet health ramp is piecewise LINEAR,
  not exponential — the exponential is the HP curve at 1.0493, which sits in the same band as the
  1.07 the literature quotes.
- `bcaba3c` — **partial index benchmarked: worth ~14 to 1.** Reads 1.662 ms -> 0.096 ms (440 of 466
  heap fetches eliminated); writes +10.9 us/row. First write benchmark was invalid — compared arms
  against different table states — and was redone with identical state per arm. Migration deliberately
  not written: that is R5d, now written in `643b926` and still awaiting owner application.
- `409a3f8` — **achievement sweep narrowed.** The completion sweep no longer fetches completed rows
  (new filtered repository method — `GetForPlayerAsync` untouched, the overview needs every row) and
  the loop is driven by the player's incomplete rows rather than all 466 definitions. Reduction
  measured rather than asserted; verified by neutering the filter, which fails all three sweep tests.
- `4790326` — **achievement pacing eval.** No dead stretches: the reward rate holds inside
  0.467-0.800 AP/clear across a 500x span of thresholds. 5,000 across the roster is 125,000 clears,
  2.5x the stated endgame — unreachable, as intended. Surfaced that extending the ladder moved 89.3%
  of all AP behind a 9x longer grind, and that the hot path got ~3x heavier (now R5b).
- `daa2801` — **potion economy eval sheet, and it answers the old R3.** Quest energy cost IS capped
  (`ChapterScalingCap = 16`), which reinstates the runaway the design doc warned about. Below the
  200-energy gate the cost cancels out of the refund ratio, so R = 1.025e-5 x pool and crosses 1.0 at
  level 13,092 regardless of which quest is played. Promoted to Owner decision 0 — the fix is a
  core-design call.
- `a67fbbe` — corrected a stale QuestService comment claiming reward steps were not transactional.
  They have been for some time; the note predated the mutation lock and told readers the opposite of
  the truth.
- `3adb93a` — **quest reward grant pinned.** Five cases. The neuter was the informative part: removing
  the mutation lock failed only the ATOMICITY tests and left the concurrency test passing, because
  the resource row's SELECT ... FOR UPDATE prevents overspend on its own. The two guards do different
  jobs, and removing the mutation lock on the reasoning that the row lock covers it would silently
  reintroduce charge-without-reward.
- `a9ad926` — **raid loot claim pinned.** Four cases against the real composition (advisory lock +
  conditional latch + grant on the same transaction). Verified against TWO separate neuters: dropping
  the latch condition failed three tests; making the loser commit rather than roll back failed the
  rollback test with 1099 gems where 100 was expected. Implementation was already correct.
- `216c985` — **gem double-spend pinned.** Five concurrency cases; verified against the real bug by
  neutering the advisory lock, which charged 11 spends against a 10-gem balance and took it to -1.
  The implementation was already correct — this closes the open defect as tested, not as argued.
- `b8191c3` — **CRITICAL: the API issued tokens it would then reject.** No `Jwt` section existed in
  appsettings.json, so issuer/audience were undefined; signing omitted them while validation still
  required them. Every login returned 200 and every authenticated request then 401'd, silently. Found
  by probing the running API, not by reading it.
- `15daf86` — security response headers (nosniff, DENY, no-referrer, Permissions-Policy, strict CSP
  outside Development), set early so they land on 401/429/403/500 too. Four pipeline-level tests,
  verified by neutering the registration.
- `a7abe05` — ForwardedHeaders now refuses to boot on an ambiguous config instead of warning.
- `docs/eval/SECURITY_AUDIT_2026-09-03.md` — the full audit, including what was NOT tested.
- `b5244b3` — **docs: potion economy rewritten to the shipped model.** Axes were documented
  backwards (Discernment→frequency); corrected to Discernment→tier, energy→frequency. All figures
  re-derived from the model with the arithmetic shown. Surfaced three things not previously written
  down: self-supply lands at D=1,808,205 rather than the 2M anchor (R settles at 1.025, so it crosses
  1.0 early); above level 13,420 R is level-INDEPENDENT because cost tracking pool makes
  MaxPool/QuestCost constant; below it the energy gate scales R down linearly, which is the
  "pushback not a wall" as one number (7.7% recovery at L1,000).
- `43dbb7f` — chore: Unity scratch dirs ignored in the backend repo after the editor was pointed at
  it by mistake and generated Library/, Temp/, UserSettings/, ProjectSettings/ beside the .NET source.
- `f59e481` — **balance: stretch every Discernment sink across the real endgame.** Crit saturation
  ×100 (chance 1k → 100k, damage 5k → 500k, caps unchanged); rare drops re-anchored on 1M with a hard
  10M ceiling (halfway 50k → 111,111 + `RareDropDiscernmentCap`); zone ladder 6 → 9 rungs to 5,000;
  new per-raid `RaidClears` ladder scoped by raid definition id; ladder ids re-keyed on threshold
  because `ItemRarity` stops at Orange permanently; `GetCritProfile` widened to `long`. 1143 unit
  tests pass, 0 warnings.
