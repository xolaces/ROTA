<p align="center">
  <img src="assets/branding/rota-icon-dark-1024.png" width="120" alt="ROTA mark">
</p>

<h1 align="center">ROTA — Rise of the Ancients</h1>

<p align="center">A server-authoritative fantasy RPG, built as a love letter to <em>Dawn of the Dragons</em>.</p>

---

This repo is the **game server**. It's an ASP.NET Core backend that owns every rule in the game —
combat resolution, loot rolls, progression, the economy, all of it. The Unity client (separate repo)
never decides anything; it sends *intent*, the server resolves it and sends back the result. Nothing
the player's machine claims is trusted. That constraint is the whole point.

It's a solo project, built over the back half of 2025 into 2026. Backend is feature-complete and in a
private beta; the Unity client is mid-build.

## Why this exists

I spent a stupid number of hours in Dawn of the Dragons — the social raids, the collection depth, the
long climb. ROTA is me rebuilding the parts I loved and quietly fixing the parts that punished people
who *wanted* to keep playing: the multi-hour real-time cooldowns, the dead-wall energy gates you
couldn't pay your way past. The grind stays. The friction stays — it's what makes the consumable
economy mean anything — but it's a throttle now, not a locked door. The full reasoning lives in
[docs/DESIGN_NORTHSTAR.md](docs/DESIGN_NORTHSTAR.md).

## Stack

- **ASP.NET Core 10 / C#** — clean architecture, five projects (`Api` · `Application` · `Domain` · `Infrastructure` · `Shared`)
- **PostgreSQL 16** via EF Core 9 / Npgsql — snake_case throughout, every table soft-deletable, every state change audited
- **Redis 7** — rate limits, login lockouts, idempotency caches, chat ring buffers
- **RS256 JWT** — 15-minute access tokens, rotating refresh tokens, max three live sessions per account
- **xUnit + Moq + FluentAssertions** — over a thousand tests gate every change
- **Unity 6** (UI Toolkit) — the client, in its own repo

## What's in it

A short tour of the systems that actually ship:

- **Auth & security** — registration behind a beta gate, BCrypt(12), session rotation, per-IP and
  per-player rate limiting, an append-only audit log nothing is allowed to UPDATE or DELETE.
- **Progression** — a level curve where XP scales with *resource spent*, not authored numbers; stat
  allocation with a server-enforced investment cap; an eleven-tier class ladder.
- **Combat** — a shared raid engine with four difficulties, server-seeded damage, contribution-tiered
  rewards, and a solo competitive ladder (the Gauntlet) layered on top of it without a second code path.
- **Questing** — a data-driven Chapter → Zone → Node map with node depletion and a zone-boss reset cycle.
- **Economy** — gems tracked as an append-only ledger (the balance is always a SUM, never a stored
  field), permanent gear ownership, a guild sigil economy with daily claims and a shop.
- **Social** — friends, private messages, blocks, world and guild chat over SignalR, in-game reporting.
- **Ops** — an admin CLI and REST surface, moderation tools, and an operator email backbone that
  persists everything before it tries to send.

## Running it locally

You'll need Docker and the .NET 10 SDK.

```bash
docker compose up -d                 # postgres + redis
dotnet run --project src/ROTA.Api    # serves http://localhost:5035
dotnet test                          # the whole suite
```

First boot seeds an admin account — set `Seed:AdminPassword` in user-secrets first. Registration is
gated by default, so mint yourself a key:

```bash
dotnet run --project src/ROTA.Api -- gen-beta-key
```

The admin CLI (seed-admin, promote/demote, beta keys, leaderboard refresh) and every `dotnet ef`
command are catalogued in [docs/OPERATIONS.md](docs/OPERATIONS.md). Putting it on a real server —
Ubuntu, Docker, Caddy with automatic TLS — is walked end to end in
[docs/BETA_DEPLOY.md](docs/BETA_DEPLOY.md).

## Layout

```
src/
  ROTA.Api              controllers, middleware, Program.cs
  ROTA.Application      all business logic + service interfaces
  ROTA.Domain           entities — private setters, no EF attributes, mutated only through methods
  ROTA.Infrastructure   EF configs, migrations, Redis
  ROTA.Shared           DTOs, enums, constants
tests/                  xUnit (unit + integration)
content/                JSON game data — quests, raids, items, loot tables, masteries
docs/                   architecture, design, ops, per-system specs
```

## A note on the rules

The architecture rules in [CLAUDE.md](CLAUDE.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) aren't
suggestions. Domain entities change only through methods. Controllers stay thin and delegate. EF mapping
is Fluent-API-only. The server is authoritative, always. They read as strict because a game server you
can't trust isn't worth building — the discipline is the feature.

## Status

Personal project, actively developed. Not open source — no license is granted yet, so all rights are
reserved for now. If something here is useful to you, ask.
