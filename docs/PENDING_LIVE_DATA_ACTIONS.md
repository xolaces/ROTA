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

### What has to be decided first

"Reset SP" has at least three meanings and they are not equivalent:

1. **Refund and re-grant** — zero every investment, return skill points equal to what the account's
   level legitimately grants, let players re-spend. Preserves level, resets the build.
2. **Roll levels back too** — recompute what the account's level *should* be under the fixed curve and
   set both level and SP from that. Most correct, most punishing, hardest to explain.
3. **Zero unspent SP only** — leave invested stats alone, clear the unspent balance. Cheapest, and
   leaves most of the over-earned power in place.

The beta population is small (the owner plus a few friends), so any of the three is operationally
easy. This is a fairness and player-communication call, not a technical one.

### Notes for whoever writes the script

- `player_stats` holds `skill_points` (unspent) and the six `*_investment` columns.
- Skill points per level come from `StatService` level-up handling — read it rather than assuming a
  flat rate.
- `audit_log` is append-only as of the 2026-08-25 migration, so the reset **must** append its own
  audit rows rather than editing anything. Do not disable the trigger for this.
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
