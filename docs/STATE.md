# ROTA — where the project actually is

*Single source for "what is the state of things". Updated 2026-11-09.*

> This file replaces three overlapping documents (`CURRENT_TASK.md`, `PROJECT_STATE.md`,
> `SESSION_HANDOFF.md`) that had drifted into a chain of stale pointers aimed at each other, all
> dated June, with the docs index recommending them as the first thing to read. **Build history lives
> in `/CLAUDE.md`; the work queue lives in `docs/AGENT_BACKLOG.md`; this file is only the snapshot.**

---

## Repo layout

| What | Where | State |
|---|---|---|
| Backend + content | `C:\Dev\ROTA` | branch `main`, level with `origin/main` |
| Unity client | `C:\Dev\ROTA.Client6` | live builds come from `feat/ui-glass-shell`, which sits on `feat/mock-playtest-accounts` (neither merged to `master`) |
| Web client | `C:\Dev\rota-web` | branch `master`, static Stitch screens (still Obsidian Gilt) |
| Ops dashboard | `C:\Dev\rota-ops-dashboard` | separate repo, private |
| DotD reference | `C:\Dev\dotd-re` | decompiled DotD client (AS3) and `GAP_MAP.md`; reference only |

**Unmerged local branch with finished work:** `docs/system-28-daily-assignments`, the specs for
Systems 28–32 (daily assignments, daily chest and check-in, comeback and account age, gifting, gear
loadouts) in `docs/specs/backlog/`. Not pushed.

The `C:\Dev\ROTA-Qwen` worktree was merged into `main` and removed on 2026-09-07. Nothing lives in
OneDrive — earlier docs referenced a OneDrive path and that is no longer true.

## Build

- **0 errors.** `dotnet build ROTA.slnx` (one known warning: CS8604 in `StarterKitRegistrationTests`)
- **1,370 unit tests green.** `dotnet test tests/ROTA.UnitTests`
- **229 integration tests green.** `dotnet test tests/ROTA.IntegrationTests` (needs Docker)
- 57 migrations, all applied locally and in production. 137 endpoints across 31 controllers.
- Content: 7 chapters / 26 zones / 139 quest nodes · 176 items · 111 gear in 13 sets · 41 magics ·
  37 units · 10 legions · 37 raids · 23 recipes · 435 placeholder icons.

## Live server vs local

**Production runs `main`.** Deployed 2026-11-09:

- **API:** `8605be0`.
- **Database:** has every migration through `AddRefreshTokenRevokeReason`. The snapshot taken just
  before is `/opt/rota/backups/rota-20261109-pre-8605be0.dump` on the droplet.
- **WebGL client:** built from `ROTA.Client6` `f0db5ae` (`feat/ui-glass-shell`). Every screen is in
  the glass style. `/opt/rota/web.prev` holds the previous build (`ded775d`, glass shell and raid
  combat only).
- **Email:** switched off on the droplet, so password reset answers 503 until it is configured.

A deploy that adds a migration is a schema change, not just a code push. Note that **every admin CLI
command calls `Database.Migrate()` before it runs**, including `beta-reset` and `gen-beta-key`, so the
first CLI command run against production applies any pending migration whether or not that was the
intent. Snapshot the database first.

**Build WebGL from the main `ROTA.Client6` checkout only.** A build from a second checkout (a git
worktree with a copied `Library`) packed `Main.unity` without its `AppBootstrap`, and the game showed a
black screen. When rolling back with `cp -a web.prev/. web/`, `touch` the restored files: `cp -a` keeps
their older timestamps, so browsers that cached the newer `.data` keep it, and the old wasm then
crashes with "memory access out of bounds".

## Before a public beta

1. **`content/legal/terms.md` is placeholder text** and registration requires accepting it. This is
   the one blocker that is not a command to run.
2. **Email is off on the droplet.** Until it sends, password reset answers 503. DigitalOcean blocks
   SMTP on 587, so use a relay's alternate port through `EMAIL_HOST` / `EMAIL_PORT`.

## Open owner decisions

Full list in `docs/AGENT_BACKLOG.md` under "Owner decisions". The two that gate the most:

- **0k — the sigil rerun rate** (`QuestConfig.SigilRerunDropChance`, currently 15%). Raids are gated
  on sigils and a chapter-6 sigil costs 34–70 days of banked energy. There are 37 raids. This one
  number decides whether a tester sees the game or its first two chapters.
- **0f — player market on or off for the beta.** Built, pen-tested 33/33, `MarketConfig.Enabled`
  ships `false`.

## UI direction (approved 2026-11-09)

The approved layout is a design canvas:
[ROTA UI Rework](https://claude.ai/artifact/79Uzm9i9mi1S7afZDvapku) (private to the owner). It has 10
screens: Home, Quest, Raids, Raid combat, Leaderboards, Profile, Legion, Gauntlet, Guild, Bazaar.

**In the Unity client** (`feat/ui-glass-shell`, live 2026-11-09 at `f0db5ae`):

- **Shared shell and raid combat** are laid out like their canvas boards: top bar, collapsible rail,
  framed stage, chat dock.
- **Every other screen has the canvas colours and controls, in its old layout.** That means:
  - glass panels and cards;
  - gold-tint buttons, with solid gold for a screen's one main action;
  - underlined tabs and sentence-case headings.
- **Where it lives:**
  - The shell and raid combat are in `Resources/UI/Glass.uss`, loaded after `Theme.uss`.
  - The other screens get the look from `Theme.uss` itself, which was recoloured in place.
- **The logged-out login card keeps its Obsidian Gilt look.** A restore block in `Glass.uss`, scoped
  to `.app-root--bare`, puts back the classes it shares with the game. A class added to the login
  later needs an entry there too.
- **Two compromises for the 960px WebGL canvas:**
  - Glass is translucency without blur, because UI Toolkit has no backdrop filter.
  - Magic sits under the raid's top bar, not in its bottom bar.

**Shell, the same on every screen:**

- **Top bar:** name, class, level, the Energy / Stamina / Health / Honor meters with their regen
  timers, gold and gems.
- **Left nav rail:** collapses to a monogram strip, and the choice carries across screens.
- **Stage:** fills the middle.
- **Chat:** on the right, with World / Guild / Raid tabs.

**The stage:**

- It is one full-bleed art slot.
- Every panel floats over it as glass. All panels share one transparency (about 65%), a 10px blur and
  a ~10px radius, and sit 12px in from the stage edge.
- Panels flow and wrap; they are never absolutely positioned columns sharing a fixed width (that
  overlapped on Profile).
- **Raid combat:** the name and health bar run across the top. Stamina, the hit buttons, magic and
  "you" run along the bottom. Damage, Loot and Log float over the art.

**Type and colour:**

- Type is Barlow everywhere: one crisp, slightly bold sans.
- The palette is "Iron & Crimson": graphite neutrals with no blue or brown tint, crimson for health,
  and muted gold for highlights only.

**Buttons take the colour of what they spend:**

| Spends | Colour | Where |
|---|---|---|
| Stamina | orange | normal raids |
| Energy | gold | quests and world raids |
| Honor | blue | guild raids; the backend enum is `GuildStamina` |
| Strikes | rose | Gauntlet |
| Gems | coral | gem purchases |

World raids get a small Energy / Stamina / Honor selector beside the hit buttons, and the buttons
recolour to match. An unaffordable action reads "N short" instead of going grey.

**Rejected on the way, so don't bring these back:**

- Obsidian Gilt's blue-grey and gold
- Cinzel and Inter
- letter-spaced caps labels over big numbers
- dashboard stat tiles and pill chips
- the parchment / umber palette
- decorative fonts

## Locked decisions — do not re-litigate

- **Gauntlet battalion power** is `(pATK + ΣbATK) × 4 + (pDEF + ΣbDEF) × 1` on effective stats.
  Discernment crit is passive and never shown in power. Slots are 6 generals + 20 troops. Gauntlet
  combat is a **full replace** of the normal damage path, not an addition to it.
- **Owner UI standards.** Pop-outs, not expansions. Rewards in one fixed replace-only slot. Action
  buttons never move. No grey buttons. Locked options are unselectable. **The skin is the glass
  over stage layout above (Barlow, Iron & Crimson, buttons coloured by what they spend), and it
  replaces Obsidian Gilt. The shared shell (top bar, nav rail, stage, chat) is the app-wide
  template**, not the Gauntlet page.
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
- **Screens other than raid combat keep their old layouts.** Their colours and controls match the
  canvas, but they are still stacked cards. They do not yet use the canvas boards' stage art with
  panels floating over it. `rota-web` is still Obsidian Gilt throughout.

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
