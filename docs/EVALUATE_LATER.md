# Evaluate later

Things built or deferred on purpose, with an open question attached. Not a backlog of work to do —
a list of calls to revisit once there is evidence to make them on. Each entry says what would settle it.

Delete an entry when it is decided, and record the decision in `DESIGN_DECISIONS.md` if it matters.

---

## Gauntlet stage jump — keep it? make it work live?

**Added:** 2026-08-26, at the owner's request, mock only.
**Where:** `GauntletDevTab` → "JUMP TO STAGE", backed by `MockRotaApi.SetMockGauntletStage`.

Drops a player straight onto a ladder stage so late-ladder behaviour is testable without clearing
179 stages to reach stage 180. Score and highest-cleared move with the jump, or the leaderboard would
contradict the position.

**Deliberately mock only.** The live dev surface is `grant` / `grant-item` / `refill`. A live version
would have to write `gauntlet_entries`, then re-run the rank snapshot so the standing agrees — and a
dev tool that writes competitive ladder state is a different risk class from one that grants gold.

**Open questions:**
1. Is this useful enough to keep at all once the Gauntlet is stable, or was it scaffolding?
2. If it stays, does it need to work against live? That means a new AdminOnly endpoint and an audit
   row per jump, because a ladder position that moved without being climbed must be explicable.
3. If it works live, does jumping DISQUALIFY the account from that run's prizes? A dev account sitting
   at stage 250 without climbing distorts every league it is in.

**What would settle it:** running one full Gauntlet season with the mock jump and seeing whether the
live gap actually got in the way.

---

## The 5000+ league still has no lore name

**Raised:** D-016. Still open.

The top league is "Ancient", which collides with the Ancient class at level 10,000. Config and
content-string change only; the owner picks the name.

---

## League band expansion

**Raised:** D-016. Trigger, not a date.

Open-ended 5000+ is fine while the population above 5,000 is thin. Higher bands (10000–24999,
25000+) get added when population justifies it — `GauntletConfig.LeagueBounds`, config only.

**What would settle it:** a league leaderboard where the top and bottom of the 5000+ band are no
longer comparable.
