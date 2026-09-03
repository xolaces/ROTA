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

### R2. Eval sheet — potion economy end to end
`docs/eval/POTION_ECONOMY_EVAL.md`. A reproducible table, not prose: R (refund ratio) against
Discernment × energy-per-click × pool size, with the self-supply crossover marked. Include the
sensitivity: how far does the 2M anchor move if SP/raid goes 200 → 400 → 1000 → 2000. State the
assumption set at the top so the numbers can be re-derived rather than trusted.

### R3. Quest energy cost must keep scaling with chapter
The single unresolved structural risk in the potion design. Potion value is a PERCENTAGE of the pool
so it grows linearly with level; quest cost currently flattens. Linear beats capped, always — this is
the same shape as the stamina-pool-vs-XP-curve bug past level 139 and the auto-levelling bug.
Confirm whether `QuestConfig` / the content actually caps cost, and if it does, write the fix as a
chapter-scaled cost so the two curves share a shape. **Do not ship a drop-rate suppression instead** —
that is the punitive alternative.

### R4. Client support for nine-rung ladders
The zone ladder went 6 → 9 rungs and raids gained a 9-rung per-raid ladder
(`AchievementCategory.RaidMastery`). Check `C:\Dev\ROTA.Client6` for anything that assumes six tiers,
a rarity-keyed achievement id, or a fixed-height achievement list. The ids changed shape:
`ach_zonererun_c1z0_grey` → `ach_zonererun_c1z0_t10`.

### R5. Eval sheet — achievement pacing against the stated endgame
`docs/eval/ACHIEVEMENT_PACING_EVAL.md`. The 5,000 top rung is a deliberate chase ceiling: maxing it
across 25 raids is 125,000 clears against a ~50,000-raid endgame, so ~40% of the roster's top rung is
reachable in a full playthrough. Model the AP curve: total AP available, AP per hour at plausible
clear rates, and where the ladder stops rewarding. Flag whether the 10 → 5000 span has dead stretches.

### R6. Cross-check the economy against comparable games
Genuine research, written up as `docs/research/`. The useful comparison set is async/idle RPGs with
energy economies and long ladders. What to extract: how they pace a sink that must stay alive across
several orders of magnitude of stat growth, and how they avoid the linear-vs-capped crossover. ROTA's
specific problem — every Discernment sink saturating inside 0.5% of the endgame — is a known genre
failure mode and is worth naming properly. Cite sources; do not invent numbers for other games.

### R7. Sweep for other saturating sinks
The crit/rare-drop retune fixed two. Find the rest: any `Math.Min(cap, stat × rate)` or `d / (d + H)`
in the codebase, tabulate its saturation point, and compare against the endgame range for the stat
that feeds it. Output a table in `docs/eval/`. This is the same audit shape that found the crit
problem and it is cheap to repeat.

### R8. Numeric-headroom sweep
`GetCritProfile` is now `long`, which removed the thinnest margin. Repeat the sweep across every
narrowing cast and every accumulator that endgame values feed: raid damage totals, lifetime ledgers,
leaderboard scores, AP totals. Report margin as a multiple of the stated endgame value, not as
"fits in an int".

---

## Blocked

### B1. Held content — three Standard-tier raids
`src/ROTA.Api/content/raids.json` and `quests.json` carry `raid_lastwatch_relay`, `_vault`, `_lamp`,
all with `lootTableId: ""` so they grant gold and XP only. Held pending the zone-Guardian loot
decision (Owner decisions, below). `q_lastwatch_boss` additionally fails the hardened content
validator — a real defect in unreviewed Copilot content, which must be fixed before these ship.
**BLOCKED on the loot decision.**

---

## Owner decisions — the agent must not decide these

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
