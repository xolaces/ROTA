# Pending live-data actions

One-off actions against **live player data** that must happen when the current work is pushed to the
production server. These are not migrations — a migration changes shape, these change player state —
so nothing applies them automatically and nothing will remind you. That is what this file is for.

Delete an entry once it has been done, and say when.

---

## 1. Reset skill points for every live player — after the XP-curve fix

**Status:** OPEN. Blocked on the owner deciding the reset semantics (below).
**Raised:** 2026-08-25, by the owner, alongside the auto-levelling report.
**Depends on:** the `XpLinearPerLevel` fix shipping (`LevelingConfig`, 2026-08-25).

### Why

Levelling was self-sustaining. A player's stamina pool grows **linearly** with level — the LSI cap
bounds `Energy + Stamina × 2` to `7.45 × level`, so an all-stamina build reaches about `3.725 × level`
— while XP-to-next-level grew **sublinearly** at `30 × level^0.8`. Linear always overtakes sublinear.
`MilestoneFloors` patched it in steps, so pacing sawtoothed: each milestone bought headroom, the pool
caught up, and the player auto-levelled until the next milestone.

At the worst point — **level 4,999**, the last level before the 5,000 milestone lands — a single full
stamina dump was worth **1.60 levels**. Each of those levels granted the skill points that made the
next dump bigger.

So any live account that spent time in an auto-levelling band holds skill points it should never have
earned. The curve fix stops it continuing; it does not undo what was already banked.

### DECIDED: refund and re-grant

**Owner ruling, 2026-08-26: option 1 — refund and re-grant.** Zero every investment, return skill
points equal to what the account's level legitimately grants, and let players re-spend from scratch.
Levels are PRESERVED; only the build resets.

The owner's reasoning, recorded because it governs how this is communicated: *"this isn't a version
update"*. It is a beta correction, so players are not owed the continuity a live patch would owe them,
and the two options either side were rejected for good reasons:

- Rolling levels back (option 2) is the most technically correct and the most punishing. It takes away
  progress people watched themselves earn, to fix a bug they did not cause.
- Zeroing only unspent SP (option 3) leaves the over-earned power banked in stats, which is most of it
   — it would have looked like a fix without being one.

Refund-and-regrant lands between: nobody loses a level, and nobody keeps a build the corrected curve
would never have funded.

### Implementation notes

- Preserve `players.level` and `experience`. Rewrite only `player_stats`.
- Zero all six `*_investment` columns, then set `skill_points` to the legitimate total for that level.
  Read the per-level grant from `StatService`'s level-up path rather than assuming a flat rate.
- **Re-check LSI afterwards.** Investments are what LSI is computed from, so a zeroed build is
  trivially legal — but the player's Energy/Stamina POOLS derive from those investments too, and both
  must come back down with them or the pools outlive the investment that paid for them.
- Health/Energy/Stamina live values should be clamped to the new maxima, not left above them.

### Notes for whoever writes the script

- `player_stats` holds `skill_points` (unspent) and the six `*_investment` columns.
- Skill points per level come from `StatService` level-up handling — read it rather than assuming a
  flat rate.
- `audit_log` is append-only as of the 2026-08-25 migration, so the reset **must** append its own
  audit rows rather than editing anything. Do not disable the trigger for this.
- One audit row per player, naming the before/after skill-point totals. A blanket "reset everyone" row
  is not a dispute trail.
- Take a database backup first. This is not reversible from inside the app.

---

## 2. Verify the two pending schema migrations landed

**Status:** OPEN.

`20260825121651_AddBannedUntil` and `20260825122918_EnforceAuditLogAppendOnly` are on `main` and have
not been applied to production. `AddBannedUntil` is **required before deploying** that release — every
player read fails on a missing column without it.

```
psql "$PROD_CONNECTION" -f scripts/verify-prod-schema.sql
```

The same script also settles the older open question of whether the June `int → bigint` widenings were
ever applied. If they were not, production is carrying a silent overflow risk on core player stats.
