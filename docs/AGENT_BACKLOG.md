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

### R5d. Write the partial-index migration — OWNER APPLIES
R5c is answered: the index is worth roughly 14 to 1, measured in
`docs/eval/PARTIAL_INDEX_BENCHMARK.md`. Reads 1.662 ms -> 0.096 ms on a 932,000-row table; writes
+10.9 us per incremented row; index size 432 kB against 56 MB for the existing composite.

    CREATE INDEX ix_ap_player_incomplete ON achievement_progress (player_id)
        WHERE NOT is_completed AND NOT is_deleted;

Add it via `dotnet ef migrations add` so the model snapshot stays in step — do NOT hand-write raw SQL
into a migration here, the snapshot is what keeps future migrations honest. **The agent must not apply
it.** Four migrations are already pending owner application; this would be the fifth.

Worth pairing with a `CONCURRENTLY` build if it is ever applied to a live database with real traffic,
since a plain CREATE INDEX takes a write lock for its duration.

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

0. **The potion design ends the energy economy at level 13,092 — decide before it is built.**
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

- `bcaba3c` — **partial index benchmarked: worth ~14 to 1.** Reads 1.662 ms -> 0.096 ms (440 of 466
  heap fetches eliminated); writes +10.9 us/row. First write benchmark was invalid — compared arms
  against different table states — and was redone with identical state per arm. Migration deliberately
  not written: that is R5d and the owner applies migrations.
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
