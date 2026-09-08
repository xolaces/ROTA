# ROTA — Documentation Map

What every `.md` in this repo is for, and where to look first.

## Start here

| Doc | Purpose |
|---|---|
| [`/CLAUDE.md`](../CLAUDE.md) | **Canonical build instructions & rules** — stack, architecture/security non-negotiables, code labels, run/migration/CLI commands, and the per-system build history. Overrides default behaviour. |
| [`STATE.md`](STATE.md) | **Where the project actually is** — build/test numbers, the live-server gap, beta blockers, locked decisions. The single state snapshot. |
| [`AGENT_BACKLOG.md`](AGENT_BACKLOG.md) | **The work queue** — ranked Ready items, open owner decisions, Done with shas. Survives context loss; conversation memory does not. |
| [`OWNER_MANUAL.md`](OWNER_MANUAL.md) | **Hands-on control manual** — how to change balance, content and config yourself without touching C#. Raid HP, drop rates, adding raids and items, energy costs, XP curves. |

## Durable references (change rarely)

| Doc | Purpose |
|---|---|
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | The **rules** — layering, the EF/enum/store-default conventions, pipeline order, what is enforced and why. Short by design. |
| [`ai/ROTA_ARCHITECTURE.md`](ai/ROTA_ARCHITECTURE.md) | The **description** — how the five layers are actually built today, layer by layer. Longer, and complements the rules doc rather than repeating it. |
| [`ai/ROTA_INVARIANTS.md`](ai/ROTA_INVARIANTS.md) · [`ai/ROTA_SYSTEM_MAP.md`](ai/ROTA_SYSTEM_MAP.md) · [`ai/ROTA_GAME_MODEL.md`](ai/ROTA_GAME_MODEL.md) | Machine-oriented context set: invariants that must hold, the system map, and the game model. |
| [`DESIGN_NORTHSTAR.md`](DESIGN_NORTHSTAR.md) | Durable design vision; divergences recorded as amendments (no resets, capped scaling, Gauntlet as core spine). |
| [`OPERATIONS.md`](OPERATIONS.md) | Ops/tooling runbook — every `dotnet`/`ef` command, the admin CLI (including `beta-reset`), admin REST API, config flags, secrets, migrations, deployment order, beta onboarding. |
| [`DEPLOYMENT.md`](DEPLOYMENT.md) · [`BETA_DEPLOY.md`](BETA_DEPLOY.md) | Host-agnostic deploy artifacts, and the beta deployment runbook. |
| [`ROTA_Function_Reference.md`](ROTA_Function_Reference.md) | Full method signatures, entity fields, endpoint map, enums. Read instead of opening source when planning where to change. |
| [`ui/ROTA_GameDesign_UI_Reference.md`](ui/ROTA_GameDesign_UI_Reference.md) | DotD mechanics analysis, screen-by-screen UI blueprints, content-pipeline guide. |

## Specs

| Folder | Purpose |
|---|---|
| [`specs/README.md`](specs/README.md) | **Spec index** — every per-system build spec by status (`shipped/` · `active/` · `backlog/`), with the real System-number mapping (resolves the "System 13" collision). |

## Lore

[`Lore/`](Lore/) — 23 files. [`ROTA_Master_Canon.md`](Lore/ROTA_Master_Canon.md) is the top of the
chain and the only place the world's rules are written down; the faction dossiers, regional arcs and
raid banks hang off it. New content is checked against Master Canon XX, "Principles Carried Forward".

## Analysis (point-in-time, kept as evidence)

[`eval/`](eval/) and [`audit/`](audit/) hold balance evaluations, economy sweeps, security audits and
drop-curve analyses. These are dated and are **not** maintained — they are the evidence behind a
decision, useful when someone asks "why is this number what it is".

[`research/`](research/) holds genre comparisons. [`design/`](design/) holds per-system design
notes — player market, potion economy, tutorial opening, and
[`LONG_HORIZON_REWARDS.md`](design/LONG_HORIZON_REWARDS.md) (the 14-day login cycle, the Idol,
milestone design rights, and anniversary ROTA Coins).

## History

| Doc | Purpose |
|---|---|
| [`/changelog.md`](../changelog.md) | Per-version changelog. ⚠️ **Stale** — current through the v0.2.5 era. `CLAUDE.md`'s build-status section is the maintained history. |

---

*Maintenance note: prefer consolidating a superseded doc into its replacement over letting both live.
Three separate state documents once drifted into a chain of stale pointers aimed at each other, all
of them recommended here as the first thing to read — which is worse than having none. One state
doc, one queue, one build history.*
