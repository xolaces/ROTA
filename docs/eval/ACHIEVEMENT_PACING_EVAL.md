# Achievement pacing — evaluation sheet

Checks the nine-rung clear ladders (`f59e481`) against the stated endgame. Two questions: does the
10 → 5,000 span have dead stretches, and is the 5,000 ceiling reachable.

**Short answers: no dead stretches — the reward rate is unusually flat — and no, 5,000 across the
roster is not reachable, which matches the stated intent.** One unintended consequence surfaced on
the hot path; it is Finding 4 and is filed as backlog R5b.

---

## The roster, and where AP comes from

| source | count | AP each | AP total |
|---|---|---|---|
| Authored achievements | 7 | — | 170 |
| Zone rerun ladders | 26 zones × 9 rungs | 2,800/ladder | 72,800 |
| Raid clear ladders | 25 raids × 9 rungs | 2,800/ladder | 70,000 |
| **Total (committed content)** | **466 definitions** | | **142,970** |

The working tree holds three more raids, which would make it 28 ladders, 493 definitions and
**151,370 AP**.

Ladder rungs are `10 / 25 / 50 / 100 / 250 / 500 / 1,000 / 2,500 / 5,000` awarding
`5 / 10 / 20 / 40 / 75 / 150 / 300 / 700 / 1,500` AP, summing to **2,800** per ladder.

**AP is a pure score.** `GetTotalPointsAsync` is read-only and nothing in the codebase spends,
converts or gates on it. So "where the ladder stops rewarding" is a question about pacing and
prestige, not about power — a player who ignores achievements entirely loses nothing mechanical.

---

## Finding 1 — there are no dead stretches

Reward rate per rung, computed as AP divided by the clears that rung actually costs:

| rung | threshold | clears in this stretch | AP | **AP per clear** | cumulative | % of ladder |
|---|---|---|---|---|---|---|
| 1 | 10 | 10 | 5 | 0.500 | 5 | 0.2% |
| 2 | 25 | 15 | 10 | 0.667 | 15 | 0.5% |
| 3 | 50 | 25 | 20 | 0.800 | 35 | 1.2% |
| 4 | 100 | 50 | 40 | 0.800 | 75 | 2.7% |
| 5 | 250 | 150 | 75 | 0.500 | 150 | 5.4% |
| 6 | 500 | 250 | 150 | 0.600 | 300 | 10.7% |
| 7 | 1,000 | 500 | 300 | 0.600 | 600 | 21.4% |
| 8 | 2,500 | 1,500 | 700 | **0.467** | 1,300 | 46.4% |
| 9 | 5,000 | 2,500 | 1,500 | 0.600 | 2,800 | 100.0% |

The rate stays inside **0.467 – 0.800 AP per clear across a 500× span of thresholds**. There is no
stretch where the ladder stops paying; a player grinding rung 9 earns at 0.600, the same rate as
rung 6 and better than rung 5.

The only soft spot is **rung 8**, at 0.467 — a 1,500-clear stretch, the worst rate on the ladder, sat
immediately before the longest one. Raising its award from 700 to 900 would flatten it to 0.600 and
match every other late rung. That is a tuning nicety, not a defect.

## Finding 2 — the ladder is heavily back-loaded, by construction

| | clears | AP | share of ladder |
|---|---|---|---|
| Rungs 1–6 (the ladder as it shipped before `f59e481`) | 500 | 300 | **10.7%** |
| Rungs 7–9 (the extension to 5,000) | 4,500 | 2,500 | **89.3%** |

Extending 500 → 5,000 did not add a third more content; it moved **89% of all achievement points**
behind a 9× longer grind. Anyone who was "finished" with a zone under the old ladder now holds a
tenth of it.

That is the intended shape for a chase ceiling. It is worth being deliberate about, because it means
the visible completion percentage for an established player dropped sharply the moment the ladders
were extended, and the AP total is now dominated by rungs almost nobody will reach.

## Finding 3 — 5,000 across the roster is not reachable, as intended

Maxing every ladder costs **125,000 raid clears** (25 raids × 5,000) plus **130,000 zone reruns**.
Against the stated ~50,000-raid endgame that is **2.5× the entire endgame in raid clears alone** —
and 2.8× with the three held raids included.

Assuming a steady clear rate, and stating the assumption because nothing in the codebase fixes one:

| clears per day | days to max ONE ladder | days to max all 25 raid ladders | years |
|---|---|---|---|
| 10 | 500 | 12,500 | 34.2 |
| 25 | 200 | 5,000 | 13.7 |
| 50 | 100 | 2,500 | 6.8 |
| 100 | 50 | 1,250 | 3.4 |
| 250 | 20 | 500 | 1.4 |

Even at a heavy 100 clears a day every day, the full roster is a three-and-a-half-year project. A
*single* raid's top rung is 50 days at that rate. This is exactly the stated intent — "very unlikely
to get 100%, but that is part of the game's intent" — and the numbers back it.

The practical reading: **rung 9 is a per-raid trophy, not a roster-wide goal.** A dedicated player
picks a handful of favourite raids and finishes those.

## Finding 4 — the hot path got three times heavier, and this is a real consequence

`EvaluateCompletionsAsync` is called on **every quest attempt** (`QuestService.cs:524`), plus on
login, equipment grants and profile reads. It calls
`AchievementProgressRepository.GetForPlayerAsync`, which is:

```sql
SELECT * FROM achievement_progress WHERE player_id = @p AND NOT is_deleted
```

Unfiltered by completion. The loop then does `if (row.IsCompleted) continue;` — so completed rows are
fetched over the wire and discarded in memory, every time.

The definition roster before the ladders was **163** (7 authored + 26 zones × 6 rungs). It is now
**466 committed / 493 working**. A veteran with progress on everything therefore returns roughly
**three times as many rows on every quest click** than before, and the set only ever grows, since
completed rows are never filtered out.

This matters because quest clicking is the core loop, and the potion design explicitly assumes
hundreds of clicks per pool drain — so this is the most frequently executed query in the game.

**The fix looks clean:** `EvaluateCompletionsAsync` only ever acts on incomplete rows, so it can ask
for incomplete rows. It cannot simply be added to `GetForPlayerAsync`, which the overview screen also
uses and which legitimately needs every row — it needs a separate method. Filed as **R5b**; not done
here because this tick's item was an eval sheet and the fix is a code change on a hot path.

Not measured: the actual latency. The row count is arithmetic; the cost is not, and asserting a
millisecond figure without measuring it would be guessing.

---

## What this sheet does not cover

- **Whether AP should do something.** It is currently a pure score. If it ever gates rewards, every
  number above changes meaning and Finding 2's back-loading becomes a balance question rather than a
  presentation one.
- **The zone rerun rate.** Zone reruns are driven by zone resets, whose cadence was not examined, so
  the 130,000-rerun figure has no time estimate beside it.
- **Client display.** 466 definitions is a large roster to render; backlog R4 covers whether the
  client assumes six rungs anywhere.
