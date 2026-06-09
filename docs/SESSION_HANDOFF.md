# ROTA Session Handoff — 2026-06-08 (Playtest Tickets 41–52)

## TL;DR (resume here)
A 12-ticket playtest batch (**T41–T52**) is **fully implemented, integration-reviewed, green, and committed**.
**Backend is committed + pushed** to `origin/main` (`001bec1`). **Client + ops are committed LOCALLY only**
(no git remotes configured): client `master` `1754e5e`, ops `master` `b2a8536`.

- **Backend** (`C:\Users\xolac\OneDrive\Documentos\Projects\ROTA`, branch `main`): **0 errors / 0 CS warnings**,
  **987 tests** (885 unit + 102 integration). ~116 changed/new files. 3 new migrations, **NOT applied**.
- **Unity client** (`C:\Dev\ROTA.Client6`, branch `master`): headless compile **exit 0, zero `error CS`**.
  ~26 changed/new files (incl. `SocialScreen.cs` deleted).
- **Ops dashboard** (`C:\Dev\rota-ops-dashboard`, React): `npm run build` clean. 6 changed files.
- An adversarial 4-agent integration review (contract parity · backend correctness · client integration ·
  requirements completeness) ran over the whole diff; **11/12 fully satisfied on first pass**, the punch-list
  fixes (below) were applied and re-verified.

## OWNER ACTION ITEMS (do these to light it up)
1. **Apply the 3 migrations.** `AddRaidVisibilityModel`, `AddEmailPriority`, `AddAchievementSystem` are created
   but unapplied. **NOTE:** `Program.cs` auto-migrates in **Development**, so your next `dotnet run --project
   src/ROTA.Api` will apply all three automatically. To gate them, review the migration files first (or run in a
   non-Development env). For prod: `dotnet ef database update`.
2. **Seed the Dev guild.** Add Nathan's identifier to `appsettings.json` → `Developer.Usernames` (or `PlayerIds`),
   or run `dotnet run --project src/ROTA.Api -- flag-dev <user|guid>`. Until then `EnsureDevGuildAsync` is a logged
   no-op (a guild needs a leader; the admin `Xolaces` is deliberately never locked into it). The Dev Coffee Shop
   (tag `DEV`) auto-seeds on the next start once a dev account is configured.
3. **Push client + ops (no remotes yet).** Backend already pushed. The Unity client (`C:\Dev\ROTA.Client6`,
   `1754e5e`) and ops dashboard (`C:\Dev\rota-ops-dashboard`, `b2a8536`) are committed but have **no git remote** —
   create the GitHub repos + `git remote add origin <url>` + `git push -u origin master` to back them up. The stray
   `C:\Dev\ROTA.Client6\Assets\_Recovery\` (+ `.meta`) artifact was left untracked (excluded from the commit) — delete it.

## WHAT SHIPPED (per ticket)
- **T41** (client) — raid hit button no longer sticks (in-flight guard + `finally` re-gate, replacing the
  row-disable); **0.70s auto-hit toggle** repeating the last Hit ×N, auto-stops on insufficient stamina / raid end.
- **T42** (backend+client) — rate limit is config-driven (`RateLimitConfig`), per-player ceiling **60→180/min**
  (covers 0.70s auto-hit + fast questing); client now surfaces the server's clean 429 message on every endpoint
  (centralized in `HttpRotaApi.SendAsync`).
- **T43** (backend) — **Dev Coffee Shop** (tag `DEV`): `PlayerRoles.Developer` flag + `DeveloperConfig` allowlist,
  idempotent `SeedData.EnsureDevGuildAsync`, invisible in `BrowseAsync`/`GetGuildAsync` to non-devs, two-sided
  guild gates (devs locked to it; non-devs can't join it), `flag-dev`/`unflag-dev` CLI. No migration (flag is an
  int bit). Allowlist EMPTY pending Nathan.
- **T44/45** (backend+client) — data-driven **6 chapters / 25 zones / 136 nodes + per-zone bosses** (placeholder
  lore names) in `quests.json`; the dormant **zone-indexed XP formula** is now live (early-low → late-high,
  `ChapterXpScalars` 1.0→11.0, boss ×2.0); ordered clear node→zone→chapter with `ZoneBossLocked` (409) gate;
  **reset narrowed from chapter-scope to ZONE-scope** (`ResetZoneAsync`, `ChapterReset`→`ZoneReset`) — revises T26.
  No migration (zones are JSON-only).
- **T46** (backend+client) — **Achievement Points**: data-driven `achievements.json` (5 categories: raid /
  quest node+boss / equipment owned / days played / collector), AP **summed from an append-only award ledger**,
  per-event idempotency via `AchievementProgressEvent` (unique `(player,achievement,reference)`), hooks at
  raid-kill / quest-clear / gear-grant / login(days-played); **AP total on the profile** + `GET /api/achievements`.
  Migration `AddAchievementSystem`. (Browse screen deferred; raid/gear Collector + quality-upgrade hooks deferred.)
- **T47/51** (client) — Social collapsed into a tabbed **WorldChatPanel** (Chat·Friends·Messages·Blocks); nav slot
  removed; **username context menu** (Add/Remove Friend·Block·Report) with a viewer-gated admin sub-tab
  (Mute/Ban/Unmute). `SocialScreen.cs` deleted (`ApplyRoleColor` relocated to `WorldChatPanel`, 3 call sites fixed).
  Chat now carries `SenderUsername` (verified claim) so mod/social actions target a stable handle.
  **Kick deferred** (no backend kick endpoint yet).
- **T48** (client) — pledge button uses themed `btn-primary` (was unreadable cream-on-light).
- **T49** (client) — unified **DevToolsScreen** with a drop-in `IDevTab` interface (Masteries dev-force migrated,
  **Gauntlet Summoning** tab, inert **Ancient** placeholder); opens via a dev-gated 🛠 button + F8. Scattered dev
  controls removed from MasteriesScreen/RaidScreen.
- **T50** (backend+client) — **System 23**: `RaidVisibility{Private,Public,GuildOnly,FriendsOnly}` +
  `RaidLifecycleState{Active,Lootable,Looted}`. Raids summon **Private** (unindexed); Share-to-Public/Guild/Friends;
  completed→**Lootable**, summoner Loot → **Looted** + removed from all indexes. "active ≠ public" clarified in
  code + `docs/specs/active/system-23-raid-visibility-lifecycle.md`. **Loot = dismiss/remove** (rewards already
  granted on the killing hit). Migration `AddRaidVisibilityModel`. `IsPublic` kept derived for back-compat.
- **T52** (backend+client+ops) — central `content/subjects.json` (startup-validated): bug-subject + report-subject
  dropdowns (off-list → 400), feedback forced to "Player Feedback"; `EmailPriority{Low,Normal,High}` on
  `outbound_emails` (reports High / feedback Low / bug Normal); ops dashboard groups by subject + priority-first
  sort + priority pills. Migration `AddEmailPriority`.

## DEFERRED FOLLOW-UPS (tracked, not done)
- **Global Kick endpoint** (T51) — only ban/mute/unmute exist; client Kick button intentionally omitted. A kick =
  new `[ModeratorOrAdmin]` endpoint + SignalR force-disconnect.
- **T46 raid/gear Collector hooks + Discernment quality-upgrade wiring** — Collector counts only the quest
  item-grant path today; raid threshold-drop / gear quality-upgrade are deferred (per-participant kill-loop reads).
- **Ops dashboard live-mode field mismatch (PRE-EXISTING)** — `EmailEvent.emailType` vs wire `type`, and
  `sendStatus`/`reviewStatus` lowercase vs backend PascalCase enum names. Masked because the dashboard defaults to
  demo mode; needs a remap adapter in `client.ts` before running against the live API. (The batch's `priority`
  field is correct.)

## HOW TO VERIFY
- Backend: `dotnet build` (0/0) ; `dotnet test` (987 green).
- Client: open `C:\Dev\ROTA.Client6` in Unity 6000.4.9f1; headless compile path writes `C:\Dev\rota6-compile.log`
  (grep `error CS` → 0). Owner playtests in **mock** (`useMock` on `AppBootstrap`) — all mocks are stateful.
- Ops: `npm run build` in `C:\Dev\rota-ops-dashboard` (demo mode default shows priority pills + subject grouping).
