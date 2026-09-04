# Partial index on `achievement_progress` — measured

Backlog R5c asked whether a partial index is worth adding after the sweep query was narrowed
(`409a3f8`). It was filed as "prove it helps", not "do it", because an index that speeds a read
always slows the writes that maintain it.

**Answer: yes, by roughly 14 to 1.** Reads go from 1.662 ms to 0.096 ms; writes cost 10.9 µs more per
incremented row. On a quest attempt that is ~1.46 ms saved against ~0.11 ms spent.

The recommendation is to add it. The migration is not written here — that is R5d, and the owner
applies migrations.

---

## How this was measured

An isolated `rota_indexbench` database on the local Postgres 16, dropped afterwards. Table DDL mirrors
production: the same columns and the same single unique index on `(player_id, achievement_id)`.

**932,000 rows** — 2,000 players × the 466-definition committed roster. Completion is skewed the way
real play skews: rungs 1–440 finished, the rest in progress, giving 880,000 completed and 52,000
incomplete. `ANALYZE` run before every measurement.

The query is the one `GetIncompleteForPlayerAsync` issues:

```sql
SELECT * FROM achievement_progress
WHERE player_id = $1 AND NOT is_deleted AND NOT is_completed;
```

The candidate index:

```sql
CREATE INDEX ix_ap_player_incomplete ON achievement_progress (player_id)
    WHERE NOT is_completed AND NOT is_deleted;
```

**Caveat, stated plainly:** these are single runs on one developer machine in a container sharing a
host. The magnitudes are indicative, not precise. The direction and the rough ratio are what the
recommendation rests on, and both are far too large to be noise.

---

## Read: 17× faster

`EXPLAIN (ANALYZE, BUFFERS)` on the same player, before and after.

| | before | after |
|---|---|---|
| Rows returned by the index scan | 466 | 26 |
| **Rows removed by filter** | **440** | **0** |
| Heap blocks read | 466 | 26 |
| Buffers touched | 472 | 28 |
| **Execution time** | **1.662 ms** | **0.096 ms** |

Without it, Postgres scans the composite index for all 466 of the player's rows, fetches all 466 heap
blocks, and then discards 440 of them. The partial index contains only incomplete rows, so the 440
never leave disk.

Note this is a *separate* win from `409a3f8`. That change stopped the 440 rows crossing the wire to
the application; the database was still reading them. This stops the read itself.

**The index is 432 kB**, against 56 MB for the existing `(player_id, achievement_id)` index and 36 MB
for the primary key. It holds 52,000 of 932,000 rows, so it costs about 0.7% of the index storage
already in use. Storage is not a consideration here.

## Write: real, and much smaller than the read saving

Each arm rebuilt the table from scratch and ran an identical 10,000-row workload, because a first
attempt compared two arms against *different* table states and produced a nonsense result — the
common-case update appeared to get faster with an extra index on it.

| workload (10,000 rows) | without | with | delta |
|---|---|---|---|
| Increment `progress_value` on incomplete rows — the common case | 181 ms | 290 ms | **+60%** |
| Flip `is_completed` false → true — the row leaves the index | 174 ms | 188 ms | +8% |

The increment cost is the one that matters: **+10.9 µs per row**. It is not free, and +60% is a real
relative cost — it looks alarming until put beside what the read saves.

Completion transitions are nearly free because they are rare per row: a row leaves the partial index
exactly once, ever.

## The trade, per quest attempt

A quest attempt does one sweep read and fans progress increments across the definitions on its metric
— one for a simple counter, up to nine for a zone-rerun ladder.

    read saved     1.662 - 0.096                = 1.566 ms
    write cost     10.9 us x 10 increments      = 0.109 ms
    net                                          ~1.46 ms saved per attempt

Roughly **14 units saved for every 1 spent**, and the ratio only improves as accounts age, because the
read saving grows with completed rows while the write cost does not.

---

## What this does not settle

- **Whether the 466-row baseline is right long term.** The roster grows with content. The read cost
  without the index scales with total definitions; with it, only with what a player has unfinished.
  That is the more important structural property and it is the real argument for the index.
- **Behaviour under write-heavy concurrency.** These are single-threaded batches. A busy server with
  many players incrementing at once was not simulated.
- **Whether `is_deleted` belongs in the predicate.** Checked: it does not need to, but it should stay.
  `AchievementProgress.IsDeleted` has a private setter, is set to `false` in `Create`, and nothing in
  the codebase ever sets it true — the column is permanently false on this table. Keeping it in the
  predicate costs nothing and keeps the index an exact match for the three queries that filter on it,
  which is the safer shape if soft-delete is ever switched on.
