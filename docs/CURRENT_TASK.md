# ROTA — Current Task

*Updated 2026-05-29. Keep this short; it answers "what now?" for the next session.*

## Just completed (High)
**System 12 — Beta Access Control**, merged to `main`, tagged **v0.2.0**.
RBAC roles (Player/Moderator/Admin), single-use beta keys, idempotent `Xolaces`/`DEV_Xolaces`
admin seed, promote/demote via REST + bootstrap CLI. Four post-build oversight fixes applied
(CLI DI crash, EF enum-sentinel data corruption, beta-key burn on duplicate registration,
integration-test hermeticity). 193 tests green.

## Do first (housekeeping, High priority)
1. **Push `main` to a remote** — 25 commits are local-only; there is no backup.
2. **Add minimal CI** — a GitHub Action running `dotnet build` + `dotnet test`.
3. **Resolve the open decision:** web-host auto-migrate-on-startup vs. documented migrate-first.
   (Currently: NOT auto-migrated; a fresh DB crashes the startup seeder.)

## Recommended next feature (High — spec exists, depends on merged roles)
**System 13 — Moderation** — `docs/specs/SYSTEM_13_MODERATION.md`.
Mute, temp-ban ≤72h (reason required), append-only `punishment_log`, staff-report→admin priority.
Build via the proven workflow: spec → pre-stage branch+Docker → Sonnet builds Bash-only with a
fail-fast canary → larger model runs tests + traces security paths + smoke-tests endpoints →
per-component commits + tag.

## Cheap isolated win (optional)
Wire `IClassService` into `EnergyService` so regen derives from `ClassConfig` instead of the
stored per-row `RegenPerMinute`.

## Hard rule learned this session
**Build-green ≠ correct.** Always run the app and trace security-critical paths after any agent
build; mock-only unit tests masked a DB-layer data-corruption bug.
