# ROTA — Current Task

*Updated 2026-05-29. Keep this short; it answers "what now?" for the next session.*

## Just completed (High)
- **System 12 — Beta Access Control** (v0.2.0): RBAC roles, single-use beta keys, admin seed
  (`Xolaces`/`DEV_Xolaces`), promote/demote (REST + CLI). 4 oversight fixes.
- **Backend hardening (v0.2.1):** CI workflow, Dev-only startup auto-migrate, `Player.Create`
  de-duplication, obsolete test stub removed. 193 tests green, no API changes.

## Do first (housekeeping)
1. **Push `main` to origin** — ~27 commits are local-only; still no backup. (Requires your go-ahead.)

## Verified facts to act on (from the Step-0 trace)
- **Content is a minimal playable slice** (2 chapters, 5 quest nodes, 2 raids). Expanding content
  is what moves toward an actual beta; the loop itself works.
- **Rewards are not atomic.** Quest: energy committed before rewards (separate). Raid: kill-rewards
  are inside the advisory-lock tx, but stamina spend is outside it. A transactional fix is scoped
  but NOT yet built (deliberately deferred from the hardening pass).

## Recommended next options (pick one)
- **A — Pre-client correctness:** wrap quest/raid resource-spend + reward grant in one transaction
  (the non-atomic gap above) and wire `EnergyService`→`ClassConfig`. Small, isolated, no API change.
- **B — Next feature:** System 13 — Moderation (`docs/specs/SYSTEM_13_MODERATION.md`), depends on
  the merged role system.
- **C — Begin the Unity client** against the now-stable API (the stated near-term goal).

Build workflow that works: spec in `docs/specs/` → pre-stage branch+Docker → Sonnet agent Bash-only
with hard guardrails → supervisor runs the app + traces security paths + smoke-tests → per-component
commits → review → merge + tag.

## Hard rule (learned)
**Build-green ≠ correct.** Always run the app and trace behavior-touching changes after any agent
build; mock-only unit tests masked a DB-layer data-corruption bug this session.
