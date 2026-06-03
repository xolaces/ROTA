# ROTA — Documentation Map

What every `.md` in this repo is for, and where to look first.

## Start of session (read these to bootstrap)

| Doc | Purpose |
|---|---|
| [`/CLAUDE.md`](../CLAUDE.md) | **Canonical build instructions & rules** — stack, architecture/security non-negotiables, code labels, run/migration/CLI commands, build status. Overrides default behavior. |
| [`CURRENT_TASK.md`](CURRENT_TASK.md) | **"What now?"** — the cheap session bootstrap: just-completed work, what's next in priority order, deferred items. Kept short and current. |
| [`PROJECT_STATE.md`](PROJECT_STATE.md) | Current build/test snapshot (counts, warnings, what each system shipped). |
| [`ROTA_Function_Reference.md`](ROTA_Function_Reference.md) | Full method signatures, entity fields, endpoint map, enums, PHASE-2 backlog. Read instead of opening source when planning where to change. |

## Durable references (change rarely)

| Doc | Purpose |
|---|---|
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | Layering, patterns, the EF/enum/store-default rules, persistence conventions. |
| [`DESIGN_NORTHSTAR.md`](DESIGN_NORTHSTAR.md) | Durable design vision; divergences recorded as amendments (no resets, capped scaling, Gauntlet as core spine). |
| [`OPERATIONS.md`](OPERATIONS.md) | Ops/tooling runbook — every `dotnet`/`ef` command, the admin CLI, admin REST API, config flags, secrets, migrations, deployment order, beta onboarding. |
| [`ui/ROTA_GameDesign_UI_Reference.md`](ui/ROTA_GameDesign_UI_Reference.md) | DotD mechanics analysis, screen-by-screen UI blueprints, Unity implementation prompt, content-pipeline guide. |

## History / changelog (append-only, look back)

| Doc | Purpose |
|---|---|
| [`/changelog.md`](../changelog.md) | Per-version changelog. ⚠️ **Stale** — current through System 12 / ~v0.2.5 era; missing v0.2.6 (magic), v0.2.7 (legion), v0.2.8 (leaderboards). Needs a catch-up pass. |
| [`DEV_JOURNAL.md`](DEV_JOURNAL.md) | Chronological development journal — narrative history of sessions/decisions. |

## Specs

| Folder | Purpose |
|---|---|
| [`specs/README.md`](specs/README.md) | **Spec index** — every per-system build spec by status (`shipped/` · `active/` · `backlog/`), with the real System-number mapping (resolves the "System 13" collision). |

---

*Maintenance note: when a doc is superseded, prefer `git mv` into the right place + an index update
over deleting — these files are the project's decision record. Keep this map and `CURRENT_TASK.md`
honest between work batches.*
