# System 17 — Global Leaderboards (spec + sliced task queue)

*Spec drafted 2026-06-02. Persistent, **time-windowed GLOBAL leaderboards** — distinct from the
**System 16 Gauntlet event ladder**. The Gauntlet ranks players inside a discrete, admin-opened
competitive *event* by cumulative damage to dedicated event raids (`gauntlet_event_id`-scoped),
in level-banded leagues, with a token economy and prize settlement. **System 17 is none of that.**
It is a set of always-on, periodically-refreshed global boards over **normal play** — keyed by a
**calendar/rolling time window** (day/week/month), not by an event. Build against this exactly.
MEDIUM epic — one slice per branch, build+test green, commit/merge/tag independently, never bundle.
Auditor reviews after a batch.*

> **STATUS: DRAFT — BLOCKED on owner open questions.** The five boards the owner specified are fixed
> (Stat, Questing energy/week+month, Raiders damage/week+month, Largest single hit/day). What is **not**
> settled is *how the windows are cut, what the Stat board ranks, and — critically — what new
> per-event persistence each board needs, because two of the four signals are not queryable today.*
> Do **not** start Slice 2+ until OPEN QUESTIONS Q1–Q14 are answered; each one changes the schema or a
> write path. This block will become the canonical decision record once the owner answers (mirroring the
> System 16 pattern).

---

## OPEN QUESTIONS (owner to confirm)

These are deliberately **not answered here** — each changes the schema, a write path, or the ranking.
Where options are sketched it is only to frame the choice; pick one.

**Windows, boundaries & timezone**
1. **Rolling vs calendar windows.** Are "day / week / month" **calendar buckets** (e.g. the week of
   2026-06-01 … 2026-06-07, frozen once it ends) or **rolling windows** (trailing 24h / 7d / 30d,
   recomputed continuously)? **This is the #1 blocker** — calendar buckets are cheap (one aggregate
   row per `(player, board, period_key)`, never recomputed once closed) and match the "season reset
   feeling"; rolling windows need continuous re-aggregation or a sliding event store. Pick one per board
   or one globally.
2. **Boundary + timezone.** If calendar: where do day/week/month boundaries fall, and in **which
   timezone** (UTC assumed, matching `DateTimeOffset.UtcNow` everywhere in the codebase)? Day = UTC
   midnight? Week starts Monday or Sunday? Month = calendar month? The boundary is encoded into every
   `period_key` (e.g. `day:2026-06-02`, `week:2026-W23`, `month:2026-06`) and must be deterministic and
   stable, so it cannot be left implicit.
3. **Retention / history of past periods.** Do we **keep** closed-period boards forever (queryable
   history — "top raiders, May 2026"), keep the **last N** periods, or only ever expose the **current**
   (live) period? Retention drives whether the aggregate table grows unbounded and whether the read API
   needs a `periodKey` selector or just "current".

**The Stat board — what does it rank?**
4. **Which stat(s).** The owner said "rank players by stats" without fixing which. Candidates, each a
   different read: (a) **total combat power** (a single computed scalar — but no such metric is persisted;
   it would be `effATK×4 + effDEF` or a fuller legion-inclusive number computed on read), (b) **effective
   combat power including legion/gear/trophies** (matches what actually hits, but is expensive to compute
   for every player and depends on the *active* legion loadout), (c) **per-stat boards** (separate ATK /
   DEF / Discernment ladders), or (d) **player level**. **Confirm exactly one (or a fixed set).** This
   decides whether the Stat board reads a cheap stored column (level, raw investments) or must compute an
   effective-power scalar per player.
5. **Is the Stat board time-windowed at all?** Stats are a *standing* value, not an accumulation. Is the
   Stat board a **live snapshot** (current ranking, no window — refreshed every N minutes) — unlike the
   four accumulation boards which are inherently per-period? (Recommend: Stat board = live snapshot, no
   period_key; the other four = windowed. Confirm — it changes whether the Stat board even uses the
   window machinery.)

**Capture — the two signals we don't persist today (read Core Insight first)**
6. **Energy-spent capture.** Energy spend is **not queryable today.** `EnergyService.SpendEnergyAsync`
   writes only a free-text `audit_log` row (`Action="EnergySpend:{type}"`, `ResultSummary="Spent N
   Energy"`) — no structured per-player amount column, and the audit table is append-only/parse-hostile.
   So the Questing board needs a **new write**. Options: (a) **increment a per-player-per-period energy
   aggregate row** at spend time (cheap, exact, the recommended path), or (b) **emit a structured
   `energy_spend_event` row** per spend and aggregate on read/rollup (more rows, full history, enables
   rolling windows). **Which?** And: does the board count **only Energy**, or also **Stamina /
   GuildStamina** spends? (Owner said "energy spent" — assume `ResourceType.Energy` only; confirm.)
7. **Largest-single-hit capture.** The per-hit `damageFinal` is computed inside `RaidService.HitRaidAsync`
   but **only the cumulative `RaidParticipant.TotalDamageDealt` is persisted** — the individual hit value
   is **not stored anywhere.** So the max-hit board needs a **new write** on the hit path. Options:
   (a) **conditional max-update** of a per-player-per-day row (`UPDATE … SET value = GREATEST(value, @hit)`
   — one row per player per day, cheap, exact, recommended), or (b) **append a `raid_hit_event` row** per
   hit (huge volume; enables "your top 10 hits" but heavy). **Which?** And: does max-hit count **all raid
   hits** (World/Event/Standard/Guild) or a subset? (Owner said "the biggest one raid hit in the game that
   day" — assume **all** raids; confirm.)
8. **Raiders / damage-dealt capture.** This is the one signal **partly derivable today** —
   `RaidParticipant.TotalDamageDealt` already accumulates per-player-per-raid damage. But it is **per
   active raid**, not time-windowed and not globally summed, and a raid summoned in one week can be hit in
   the next. Do we (a) **sum `RaidParticipant` rows on read** filtered by the participant's hit timestamps
   (imprecise — `TotalDamageDealt` is a running total, not per-window), or (b) **add a per-window damage
   increment** at hit time alongside the existing `RecordHit` (exact per-window attribution, recommended,
   and symmetric with the energy/max-hit aggregates)? **Strong recommendation: a single shared
   aggregate-increment write at the moment each signal is produced** (see Core Insight) so all three
   accumulation boards use one mechanism. Confirm.

**Storage model & scope**
9. **Aggregate table vs compute-on-read.** Given Q6–Q8, the recommended model is a **single periodic
   aggregate table** (`leaderboard_entry`, one row per `player × board × period_key`, value incremented
   at signal time / max-updated for the hit board) rather than computing on read from `RaidParticipant` /
   audit rows. Confirm the aggregate-table model (vs. compute-on-read), and confirm whether the **Stat
   board** lives in the same table (as a live-snapshot board, recomputed periodically) or is computed
   purely on read.
10. **Global vs per-league.** System 16 bands by convergence tier into 3 leagues. Are System 17 boards
    **purely global** (one worldwide ranking per board+window — the literal reading of "GLOBAL
    leaderboards"), or **also split per league/tier** like the Gauntlet? (Recommend **global only** for
    v1 — these are explicitly the global boards, leave league-banding to the Gauntlet. Confirm; if
    per-league is wanted, `league` joins the aggregate key and multiplies the boards.)
11. **Eligibility.** Are **banned / soft-deleted** players excluded from every board (assume **yes**)?
    Is there a **minimum level** to appear (System 16 used L20)? Are **admin/moderator** accounts excluded
    from public boards (assume no, unless told)?

**Ranking, refresh & paging**
12. **Tie-breaks.** When two entries share a value, rank by (a) **earliest-to-reach** (timestamp the
    value was last changed — the System 16 choice, requires storing that timestamp on the aggregate row),
    (b) **lower player level**, or (c) **player id** (stable but arbitrary)? Need a deterministic, stored
    tiebreak key so ranks are stable across refreshes.
13. **Refresh cadence.** How often are ranks recomputed? Options: **on-read** `ORDER BY` against the
    aggregate table (simplest, fine at beta scale — matches the System 16 ~60 s Postgres snapshot
    decision), a **periodic snapshot** into a `rank` column every N seconds/minutes, or a **Redis sorted
    set** updated at write time (real-time but new infra). (Recommend Postgres `ORDER BY` snapshot for v1,
    consistent with System 16. Confirm.)
14. **Page size + caller rank.** How many entries does a board return per page (System 16 returned top
    200 + caller's own rank)? Confirm the page size and that each board also returns the **caller's own
    rank + value** even when outside the returned page.

---

## Core insight

**Two of the four accumulation signals are not persisted as queryable data today — be explicit about
what is derivable vs. what needs a new write.** The whole epic turns on this split:

| Board | Signal | Persisted today? | What v1 needs |
|---|---|---|---|
| Raiders — damage dealt (week/month) | per-hit `damageFinal` | **Partially** — `RaidParticipant.TotalDamageDealt` accumulates it **per raid**, not per time-window, not globally summed | A **per-window damage increment** at hit time (Q8), OR an imprecise compute-on-read sum over participant rows |
| Largest single hit (day) | per-hit `damageFinal` | **No** — only the *cumulative* total is stored; the individual hit value is discarded after `RecordHit` | A **new max-update write** per hit (Q7) |
| Questing — energy spent (week/month) | energy spent per spend | **No** — only a free-text `audit_log` row (`EnergySpend:{type}`), not a structured queryable amount | A **new aggregate increment / event** at spend time (Q6) |
| Stat board | a standing stat scalar | **Yes-ish** — level / raw investments are stored columns; *effective combat power* is **not** stored (computed on read) | A **read-side projection** (which stat = Q4); likely a live snapshot, no window (Q5) |

**The unifying mechanic (recommended): one shared "record a leaderboard contribution" call.** All three
accumulation boards reduce to *the same operation* — "for this player, in the current period of this
board, **add** (energy, damage) or **max-update** (single hit) a value." That is a single repository
method (`IncrementAsync` / `MaxUpdateAsync`) against one `leaderboard_entry` table, invoked from exactly
the two places the signals are already produced:
- `EnergyService.SpendEnergyAsync` (right where it already writes the audit row) → increment the
  **energy/week** and **energy/month** entries.
- `RaidService.HitRaidAsync` (right where it already calls `participant.RecordHit(damageFinal)`) →
  increment the **damage/week** + **damage/month** entries and **max-update** the **maxhit/day** entry.

This keeps **System 17 a pure read/aggregation system with two tiny write hooks** — no new combat math,
no second damage path (the damage value is the *existing* `damageFinal`, already authoritative). The
authority for damage stays the single `RecordHit` write; the leaderboard increment is a **sibling write
in the same advisory-lock transaction** so it is atomic with the hit (consistent with the v0.2.5 reward-
atomicity fix that moved reward writes inside the lock). **Do not recompute damage; do not fork combat.**

**The Stat board is different in kind** — stats are a *standing* value, not an accumulation, so it is a
**live snapshot ranking** computed from stored player data (Q4 decides which: level / raw stats /
effective power), refreshed periodically. It uses the same read/rank API surface but **does not** use the
increment hook (Q5).

> **No new combat path. No second damage computation.** The only write-side additions are: (1) two
> increment/max calls on the existing energy-spend and raid-hit paths, and (2) a periodic Stat-board
> snapshot. Everything else is read, rank, and page.

---

## Locked design decisions (owner-specified — the five boards are fixed)

1. **Stat leaderboard** — rank players by stats. *Which* stat(s) = OPEN Q4 (total/effective combat power,
   per-stat ATK/DEF/Discernment, or level). Likely a **live snapshot**, no time window (Q5).
2. **Questing — energy spent** — two boards: **per week** and **per month**. Counts `Energy` spend
   (Stamina/GuildStamina inclusion = Q6).
3. **Raiders — damage dealt** — two boards: **per week** and **per month**. Damage = the existing
   per-hit `damageFinal` that already lands in `RaidParticipant.TotalDamageDealt`.
4. **Largest single hit** — one board: **per day**. The single biggest `damageFinal` of any one raid hit
   that day (all raids assumed — Q7).

That is **six logical boards** over **four signals**: Stat (live), Energy/week, Energy/month,
Damage/week, Damage/month, MaxHit/day.

**Distinct from System 16 (do not collide):** System 16's `GauntletEntry.Score` is event-scoped,
league-banded, and prize-bearing. System 17 boards are **global, window-keyed, prize-free** (no token
economy, no settlement, no trophies, no leagues in v1 — Q10). They share **no tables** with the Gauntlet.
Reuse the *patterns* (Postgres `ORDER BY` snapshot ranking, top-N + caller-rank read shape, idempotent
writes) but **not** the Gauntlet entities.

---

## Data model (whole epic)

*snake_case for all tables/columns/indexes; every table has `id` (UUID `gen_random_uuid()`),
`created_at`, `updated_at`, `is_deleted`; every FK indexed; private setters, no EF attributes; Fluent-only
configs in `Infrastructure/Persistence/Configurations/`. Shapes gated by OPEN QUESTIONS are marked — do
not finalize a migration for a gated entity until the question is answered.*

### Enums (`src/ROTA.Domain/Enums/`)

- **`LeaderboardBoard`** — the board identity, stable regardless of windowing choices:
  `{ Stat, EnergySpent, DamageDealt, LargestHit }`.
  *(The *period* — week vs month vs day — is **not** part of this enum; it is carried by the period
  granularity / `period_key`, so Energy/week and Energy/month are the same `Board` at different
  granularities. Pick this vs. a flatter `{ StatLive, EnergyWeek, EnergyMonth, DamageWeek, DamageMonth,
  MaxHitDay }` enum in Slice 1 — the flatter enum is simpler if granularities never vary per board.)*
- **`LeaderboardPeriod`** — `{ Live, Daily, Weekly, Monthly }` — the window granularity (Q1/Q2 decide how
  each maps to a `period_key`; `Live` = the Stat snapshot, no bucket).
- **`LeaderboardAggregation`** — `{ Sum, Max }` — how a contribution combines into the entry value
  (Energy/Damage = `Sum`; LargestHit = `Max`; Stat = snapshot, neither). Stored on the board's config, not
  the row.

### Entities (`src/ROTA.Domain/Entities/`) — private setters, no EF attributes, snake_case

- **`LeaderboardEntry`** *(the one aggregate table — Q9 recommended model)* — one row per
  `player × board × period_key`:
  ```
  Id              Guid
  PlayerId        Guid    (FK → players, idx)
  Board           LeaderboardBoard
  Period          LeaderboardPeriod
  PeriodKey       string  // "week:2026-W23" / "month:2026-06" / "day:2026-06-02" / "live"  (Q1/Q2)
  Value           long    // accumulated (Sum) or best (Max) — long, matches damageFinal/energy
  LastProgressAt  DateTimeOffset  // tiebreak source (Q12 option a — "earliest to reach")
  Rank            int?    // optional denormalized snapshot rank (Q13 if snapshot chosen; null if on-read)
  created_at / updated_at / is_deleted
  ```
  - **Unique index** `(player_id, board, period_key)` — the upsert/increment key; guarantees one row per
    player per board per window (idempotency-friendly, mirrors the gem-ledger unique-index discipline).
  - **Read index** `(board, period_key, value DESC)` — drives the ranked `ORDER BY` read directly.
  - Domain methods: `Create(playerId, board, period, periodKey, initialValue)`,
    `AddValue(long delta, DateTimeOffset at)` (Sum boards), `MaxValue(long candidate, DateTimeOffset at)`
    (Max board — only updates when `candidate > Value`), `SetRank(int)`. Migration: `AddLeaderboardEntry`.
  - **Why one table, not six:** the board/period/period_key triple discriminates every board; the same
    increment/max repository method serves all accumulation boards; closed calendar periods simply stop
    receiving writes and remain queryable (Q3 retention). The Stat board, **if** stored here, is a
    `Period=Live` board whose rows are **overwritten** by the periodic snapshot rather than incremented
    (Q5/Q9 — alternatively the Stat board is computed purely on read and is **not** a row here).

- **`EnergySpendEvent` / `RaidHitEvent`** *(ONLY if Q6/Q7 choose the event-row option (b) instead of the
  aggregate-increment option (a)).* Append-only structured rows (`PlayerId`, `Amount`/`Damage`,
  `OccurredAt`, FK idx) enabling rolling windows and full history at the cost of volume. **Do not build
  these unless the owner picks the event-store option.** The recommended path is the aggregate increment
  (no event table).

> **No changes to `RaidParticipant` or the audit log.** The damage authority stays `RaidParticipant`; the
> leaderboard reads/derives, it does not replace. The audit log keeps recording state changes (board
> writes included) but is **not** the leaderboard's data source (it is append-only and parse-hostile).

### Config: `LeaderboardConfig` (appsettings, `IOptions`; safe C# defaults)
```
WindowMode        enum    // Q1 — Calendar | Rolling (default Calendar)
WeekStartsOn      enum    // Q2 — Monday | Sunday (default Monday)
Timezone          string  // Q2 — default "UTC"
StatMetric        enum    // Q4 — Level | RawAttack | RawDefense | Discernment | EffectiveCombatPower
StatBoardEnabled  bool    // Q5 — whether the Stat board is a stored live snapshot or compute-on-read
EnergyCounts      enum[]  // Q6 — which ResourceTypes count (default [Energy])
MaxHitRaidScope   enum    // Q7 — AllRaids | WorldEventOnly (default AllRaids)
PageSize          int     default 200                       // Q14
TieBreak          enum    // Q12 — EarliestToReach | LowerLevel | PlayerId
RankRefresh       enum    // Q13 — OnRead | Snapshot | Redis (default OnRead)
RetainClosedPeriods bool? // Q3 — keep history vs current-only
Global            bool    default true                      // Q10 — global vs per-league
MinLevel          int     default 1                         // Q11
```

---

## Read API (endpoints + DTOs)

All read endpoints `[Authorize]`; thin controller; `PlayerId` from JWT `sub`; server-authoritative.

- **`GET /api/leaderboards`** — discovery: lists the available boards + their period granularities + the
  current `periodKey` for each (so the client knows what it can query). → `List<LeaderboardSummary>`.
- **`GET /api/leaderboards/{board}?period={daily|weekly|monthly|live}&periodKey={optional}&page={n}`** —
  the ranked page. `periodKey` optional (defaults to the current period; supplying a past key returns a
  historical board iff retention keeps it — Q3). Returns the top `PageSize` entries **plus the caller's
  own rank+value** even when off-page (Q14). Validate `board`/`period` combination is real (e.g.
  `LargestHit` only supports `daily`; `Stat` only `live`) → 400 on a bad combo; 404 if a requested
  `periodKey` is not retained.

### DTOs (`src/ROTA.Shared/DTOs/`)
```
LeaderboardEntryDto {
    int    Rank
    Guid   PlayerId
    string DisplayName       // hydrated from Player.DisplayName (fallback Username)
    long   Value
}

LeaderboardPageResponse {
    LeaderboardBoard Board
    LeaderboardPeriod Period
    string  PeriodKey
    int     Page
    int     PageSize
    int     TotalRanked            // count of ranked entries in this board+period
    List<LeaderboardEntryDto> Entries
    LeaderboardEntryDto? You       // caller's own rank+value (null if caller has no entry)
}

LeaderboardSummary {
    LeaderboardBoard Board
    string  Title                  // "Top Raiders", "Energy Spent", "Largest Hit", "Strongest Players"
    List<LeaderboardPeriod> Periods
    string  CurrentPeriodKey       // per the active period for display
}
```

Interface: **`ILeaderboardService`** —
`GetBoardsAsync()`, `GetPageAsync(board, period, periodKey?, page)` (ranked top-N + caller slice),
`RecordContributionAsync(playerId, board, value, at)` (Sum/Max per the board's `LeaderboardAggregation`),
`SnapshotStatBoardAsync()` (Q5 — recompute the Stat board's live rows / ranks).
**`ILeaderboardEntryRepository`** — `IncrementAsync(playerId, board, period, periodKey, delta, at)`,
`MaxUpdateAsync(playerId, board, period, periodKey, candidate, at)` (atomic `UPDATE … GREATEST`),
`GetPageAsync(board, periodKey, page, pageSize)`, `GetPlayerEntryAsync(playerId, board, periodKey)`,
`SnapshotRanksAsync(board, periodKey)` (Q13 if snapshot mode).

---

## Per-slice breakdown

### SLICE 1 — Config + enums + period-key resolver  *(additive · LIGHT)*

- Enums: `LeaderboardBoard`, `LeaderboardPeriod`, `LeaderboardAggregation`.
- `LeaderboardConfig` bound from appsettings (safe defaults above), registered via `IOptions`.
- **`IPeriodKeyResolver`** (singleton) — the deterministic boundary logic (Q1/Q2): given `now` (UTC) and a
  `LeaderboardPeriod`, returns the `period_key` string (`day:yyyy-MM-dd`, `week:yyyy-'W'ww`, `month:yyyy-MM`,
  `live`). Centralizes the timezone/week-start rule so every write and read agrees. Startup-validates the
  config (valid week-start, parseable timezone, known stat metric).
- Tests: period-key boundaries (a spend at 23:59:59 UTC vs 00:00:00 UTC lands in the right day/week/month;
  ISO week numbering across a year boundary; month rollover); bad config throws at startup.
- **Acceptance:** the resolver produces stable, correct keys for every period; build 0 warnings; tests green.
- **Review depth:** LIGHT (pure config + date math — but the date math is correctness-sensitive; cover edges).

### SLICE 2 — Aggregate entity + repository  *(additive + migration · MODERATE)*

- `LeaderboardEntry` entity + Fluent config (snake_case; unique `(player_id, board, period_key)`; read
  index `(board, period_key, value DESC)`; FK index on `player_id`); migration `AddLeaderboardEntry`.
  `DbSet`. **Do NOT run `dotnet ef database update`.**
- `ILeaderboardEntryRepository` + impl: `IncrementAsync` (upsert-then-add; race-safe via the unique index
  + `ON CONFLICT … DO UPDATE SET value = value + @delta` or an advisory-lock upsert),
  `MaxUpdateAsync` (`… DO UPDATE SET value = GREATEST(value, @candidate)`), `GetPageAsync`,
  `GetPlayerEntryAsync`. Both writes update `LastProgressAt` (tiebreak) and `updated_at`.
- Tests (integration where DB behavior matters — per the architecture caveat that mock-only tests miss
  store behavior): increment creates-then-accumulates; concurrent increments don't lose updates
  (advisory lock / `ON CONFLICT`); max-update only raises, never lowers; page read ordered by
  `value DESC, LastProgressAt ASC`; player-entry lookup hits the unique index.
- **Acceptance:** the aggregate table accumulates and max-updates correctly under concurrency; reads are
  ordered. **Commit independently.**
- **Review depth:** MODERATE (concurrency correctness on the upsert/increment — the lost-update class).

### SLICE 3 — Read service + endpoints  *(read-aggregation · MODERATE)*

- `ILeaderboardService.GetBoardsAsync` / `GetPageAsync` — resolve current `period_key` (or accept a past
  one per Q3), read the ranked page, hydrate `DisplayName` (`Player.DisplayName` fallback `Username`),
  compute the caller's own rank+value (a `COUNT(*) WHERE value > caller.value` + tiebreak, or read the
  snapshot `Rank`). Validate board/period combinations; reject impossible combos (400) and unretained
  periods (404). Apply eligibility filter (exclude banned/soft-deleted; `MinLevel` — Q11).
- `LeaderboardController` `[Authorize]`: `GET /api/leaderboards`, `GET /api/leaderboards/{board}`.
- DTOs above.
- Tests: page ordered + ranks contiguous; caller-rank correct when off-page; empty board; bad
  board/period combo → 400; banned/soft-deleted excluded; `You` null when caller has no entry.
- **Acceptance:** every board returns a correct ranked page + the caller's standing. **Commit independently.**
- **Review depth:** MODERATE (rank/tiebreak determinism + caller-rank correctness + eligibility filter).

### SLICE 4 — Write hooks (energy + raid-hit)  *(integration · MODERATE→DEEP)*

**The integration slice — touches the energy and raid-hit paths. Add the contributions without changing
energy/combat behavior.**

- **Energy hook:** in `EnergyService.SpendEnergyAsync`, after the successful atomic spend (where it
  already writes the audit row), call `RecordContributionAsync` for **Energy/week** and **Energy/month**
  (Q6 scope). The leaderboard write must **never** fail the spend — wrap so a leaderboard error is logged,
  not surfaced (mirror the audit-failure-swallow discipline), OR include it in the same transaction if the
  owner wants strict consistency (Q6 storage choice).
- **Raid-hit hook:** in `RaidService.HitRaidAsync`, **inside the advisory-lock callback** where
  `participant.RecordHit(damageFinal)` already runs (so it is atomic with the hit, per the v0.2.5
  reward-atomicity fix), call: increment **Damage/week** + **Damage/month** by `damageFinal`, and
  **max-update MaxHit/day** with `damageFinal` (Q7 raid scope). **No second damage computation — reuse the
  `damageFinal` already computed.**
- **No regression:** existing `EnergyService` and `RaidService` tests must pass unchanged; a player's
  energy spend and raid hit behave **identically** to today (the hook is a sibling write, additive).
- Tests (seeded RNG, mocked leaderboard repo): an energy spend increments the right two energy entries by
  the spent amount; a raid hit increments damage week+month by `damageFinal` and max-updates the day
  entry; a **smaller** later hit does **not** lower the day max; a hit on day N and day N+1 lands in
  different `period_key`s; a leaderboard-write failure does **not** roll back the spend (or does, per the
  chosen consistency model — test the decision); all prior energy/raid assertions still green.
- **Acceptance:** energy spends and raid hits feed the boards exactly, atomically with the action, with no
  behavior change for the underlying spend/hit. **Commit independently.**
- **Review depth:** DEEP-ish (atomicity with the hit transaction; the leaderboard write must not corrupt or
  block the money path; no double-count vs. `RecordHit`).

### SLICE 5 — Stat board snapshot  *(MODERATE)*

- `ILeaderboardService.SnapshotStatBoardAsync` — recompute the Stat board (Q4 metric) for all eligible
  players and **overwrite** their `Period=Live` rows (or expose a pure compute-on-read query if Q9 chooses
  no stored Stat rows). If the metric is **EffectiveCombatPower**, document the cost (per-player active-
  legion + gear evaluation) and consider a cheaper stored proxy (level / raw stats) for v1.
- **Trigger:** a periodic refresh (Q13 cadence). For v1 the recommendation is an **admin/CLI-triggered or
  interval refresh** (no scheduler exists today — same posture as the System 16 deferred-scheduler note),
  exposed as `POST /api/admin/leaderboards/stat/refresh` `[AdminOnly]` and/or a CLI hook
  (`leaderboard-refresh-stat`).
- Tests: snapshot ranks players by the chosen metric; banned/soft-deleted excluded; re-running the
  snapshot is idempotent (overwrites, does not duplicate rows); the Stat page reads the snapshot.
- **Acceptance:** the Stat board ranks players by the confirmed metric and refreshes idempotently.
  **Commit independently.**
- **Review depth:** MODERATE (idempotent overwrite + the metric-cost decision).

---

## Deferred items (document, do NOT build this epic)

- **Rolling-window event store** — IF Q1 chooses calendar buckets for v1, the `*_event` row tables and
  continuous re-aggregation needed for true trailing-window boards are deferred.
- **Per-league / per-tier boards** — IF Q10 chooses global-only for v1, league-banded global boards (and
  the `league` aggregate-key dimension) are deferred (the Gauntlet already covers league competition).
- **Effective-combat-power Stat metric** — if Q4 picks a cheap stored proxy (level / raw stats) for v1, the
  full active-legion + gear + trophy effective-power computation per player is deferred (expensive at scale).
- **Stamina / GuildStamina questing boards** and **per-raid-type damage boards** — out unless Q6/Q8 ask.
- **Rewards / prizes for leaderboard placement** — System 17 is prize-free; any "top raider gets X"
  economy belongs to the Gauntlet (System 16), not here.
- **Real-time push (SignalR)** — v1 is pull (`GET …/leaderboards`); no hubs are mapped.
- **Automatic refresh scheduler** — v1 Stat-board refresh is admin/CLI/interval-triggered; a cron/Quartz
  cadence engine is Phase 2+ (consistent with System 16's deferred scheduler).
- **Historical retention beyond the chosen window** — if Q3 keeps only current/last-N periods, a full
  archive of every past board is deferred.

---

## Constraints (every slice — binding, copied from the Legion/Gauntlet specs)

- Domain entities: private setters, no EF attributes; state via methods/factories.
- EF Fluent only, snake_case; every table `id`/`created_at`/`updated_at`/`is_deleted`; FKs indexed.
  Heed the **EF enum + store-default rule** (`HasSentinel`) for any enum column with a non-zero
  `HasDefaultValue` (the `RaidSize`/`PlayerRoles` bug).
- Config/providers bound from appsettings; validate at startup (bad week-start, unparseable timezone,
  unknown stat metric, impossible board/period combos all throw).
- Controllers thin; `PlayerId` from JWT `sub`; server-authoritative. Admin refresh endpoint `[AdminOnly]`
  with DB actor re-verify.
- The leaderboard write hooks must **never** corrupt or block the underlying energy-spend / raid-hit money
  path. The raid-hit contribution rides **inside** the existing advisory-lock transaction (atomic with the
  hit); the energy contribution follows the existing audit-write discipline. **No second damage
  computation — the leaderboard reuses the existing `damageFinal` / spent-amount.**
- Every state change writes to `audit_log` (admin Stat-refresh at minimum). The audit log is **not** a
  leaderboard data source.
- **Do NOT run `dotnet ef database update`.** Build **0 warnings**; **all tests green** before committing a
  slice. Update `docs/PROJECT_STATE.md` count + `docs/ROTA_Function_Reference.md` as you go. Cover DB-layer
  behavior (the increment/max upsert under concurrency) with **integration tests**, per the architecture
  caveat that mock-only unit tests miss store-default / persistence bugs.
- **No co-author trailer.** **One branch + one merge + one tag per slice; never bundle.** Do **not** push
  until the owner says so. Auditor reviews after a batch (DEEP-ish review on Slice 4 — the write-hook
  atomicity is the correctness point).
