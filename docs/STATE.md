# ROTA — where the project actually is

*Single source for "what is the state of things". Updated 2026-09-07.*

> This file replaces three overlapping documents (`CURRENT_TASK.md`, `PROJECT_STATE.md`,
> `SESSION_HANDOFF.md`) that had drifted into a chain of stale pointers aimed at each other, all
> dated June, with the docs index recommending them as the first thing to read. **Build history lives
> in `/CLAUDE.md`; the work queue lives in `docs/AGENT_BACKLOG.md`; this file is only the snapshot.**

---

## Repo layout

| What | Where | State |
|---|---|---|
| Backend + content | `C:\Dev\ROTA` | branch `main`, **not pushed** |
| Unity client | `C:\Dev\ROTA.Client6` | branch `master`, separate repo |
| Ops dashboard | `C:\Dev\rota-ops-dashboard` | separate repo, private |

The `C:\Dev\ROTA-Qwen` worktree was merged into `main` and removed on 2026-09-07. Nothing lives in
OneDrive — earlier docs referenced a OneDrive path and that is no longer true.

## Build

- **0 errors, 0 warnings.** `dotnet build ROTA.slnx`
- **1,242 unit tests green.** `dotnet test tests/ROTA.UnitTests`
- 55 migrations, all applied locally. 136 endpoints across 30 controllers.
- Content: 7 chapters / 26 zones / 139 quest nodes · 176 items · 111 gear in 13 sets · 41 magics ·
  37 units · 10 legions · 37 raids · 23 recipes · 435 placeholder icons.

## Live server vs local

**The deployed server is roughly the June build — 145 commits and 5 migrations behind `main`.**

Deploying is therefore a schema change, not just a code push. Note that **every admin CLI command
calls `Database.Migrate()` before it runs**, including `beta-reset` and `gen-beta-key`, so the first
CLI command run against production applies all five migrations whether or not that was the intent.
Snapshot the database first.

## Before a public beta

1. **`content/legal/terms.md` is placeholder text** and registration requires accepting it. This is
   the one blocker that is not a command to run.
2. **Deploy, in the knowledge that it migrates.** See above.
3. **The ForwardedHeaders fix is on `main` but not on the server.** Until it deploys, per-IP rate
   limiting keys off the wrong address — the control that matters most for an open registration
   endpoint.

## Open owner decisions

Full list in `docs/AGENT_BACKLOG.md` under "Owner decisions". The two that gate the most:

- **0k — the sigil rerun rate** (`QuestConfig.SigilRerunDropChance`, currently 15%). Raids are gated
  on sigils and a chapter-6 sigil costs 34–70 days of banked energy. There are 37 raids. This one
  number decides whether a tester sees the game or its first two chapters.
- **0f — player market on or off for the beta.** Built, pen-tested 33/33, `MarketConfig.Enabled`
  ships `false`.

## Locked decisions — do not re-litigate

- **Gauntlet battalion power** is `(pATK + ΣbATK) × 4 + (pDEF + ΣbDEF) × 1` on effective stats.
  Discernment crit is passive and never shown in power. Slots are 6 generals + 20 troops. Gauntlet
  combat is a **full replace** of the normal damage path, not an addition to it.
- **Owner UI standards.** Pop-outs, not expansions. Rewards in one fixed replace-only slot. Action
  buttons never move. No grey buttons. Locked options are unselectable. **Obsidian Gilt is the skin
  and the Gauntlet page is the app-wide template.**
- **Mock fidelity.** The owner playtests in mock, so mocks stay stateful and the live path is
  verified separately.
- **Tag affinity is highest-only, never summed** — the same rule as Gauntlet trophies. Summing would
  let a legion with three small bonuses beat a true specialist at its own specialty.
- **Orange is the permanent rarity ceiling.** Nothing is ever added above it.

## Known gaps a tester will notice

- **Icons exist but nothing draws them.** 435 placeholder tiles are generated and every content entry
  has a path, but the only sprite load in the client is the class emblem.
- **Public chat is read-only.** World and raid chat receive but cannot send (needs a Unity SignalR
  client). Private messages work — they ride REST.
- **No achievement browse screen.** Points show on the profile and the endpoint is live.

## Read order for a new session

1. **This file.**
2. `/CLAUDE.md` — architecture and security non-negotiables, plus the per-system build history.
3. `docs/AGENT_BACKLOG.md` — the work queue and the open owner decisions.
4. `docs/OWNER_MANUAL.md` — how to change balance, content and config without touching C#.
5. `docs/ROTA_Function_Reference.md` — signatures, entity fields, endpoint map.

## Run commands

```bash
docker-compose up -d                      # postgres + redis
dotnet build ROTA.slnx                    # 0 errors, 0 warnings expected
dotnet test tests/ROTA.UnitTests          # 1,242 green
dotnet run --project src/ROTA.Api         # server on :5035
```

> **Stop the API before building.** A running instance holds `ROTA.Application.dll` and
> `ROTA.Infrastructure.dll`, so `dotnet build` fails with MSB3027 — and `dotnet test --no-build` then
> passes against the *stale* binaries, which reads as a clean run.
