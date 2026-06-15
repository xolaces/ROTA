# ROTA Session Handoff — v0.3.1 SHIPPED (2026-06-13)

> Fresh-chat boot doc. Everything below v0.3.1 is **committed + pushed**; there is no
> committed-but-unfinished work. Read this, then the "Read order" section, and pick up at "What's next".

## TL;DR
**v0.3.1** shipped on 2026-06-13: the **Obsidian Gilt UI swap**, **transparent class tier emblems**
+ header/profile class badges, **gem balance** on the profile, **zone-scoped quest difficulty + Pano
chase curve**, and the **Gauntlet battalion (System 24 D8)** — backend (entity/migration/endpoints) +
the full-replace combat fork. **972 unit + 111 integration green; 0 build errors.** Adversarial reviews
(USS/blast-radius, UI extras, and the combat fork) all clean.

## Repo state
- **Backend** `C:\Users\xolac\OneDrive\Documentos\Projects\ROTA` — branch `main`, remote
  `github.com/xolaces/ROTA.git`. **PUSHED** through `665f903` + annotated tag **`v0.3.1`**. Working tree
  clean except untracked `assets/classes/` (raw white-bg crest source; transparent usable copies live in
  the client at `Resources/UI/ClassIcons/`).
- **Client** `C:\Dev\ROTA.Client6` — branch `master`, committed through **`cc87b1f`**, **NO git remote**
  (commits are local). Portable bundle: **`C:\Dev\ROTA.Client6-v0.3.1.bundle`** (40 MB; `git clone`/`pull`
  from it, or `git remote add` + push when a remote exists). Unity client is NOT compiled here — headless
  scratch-compile (`%TEMP%\rota-client-check\check.csproj`) is clean.
- **⚠ MIGRATION NOT APPLIED.** Run before using the battalion live:
  `dotnet ef database update --project src/ROTA.Infrastructure --startup-project src/ROTA.Api`
  (adds `player_gauntlet_battalions` — migration `AddGauntletBattalion`). Docker postgres+redis must be up.

## What's next (owner-facing priorities)
> **Session 2026-06-13 (cont.):** items 1 + 2 below are **BUILT + adversarially-reviewed CLEAN,
> client compiles 0/0, UNCOMMITTED in `C:\Dev\ROTA.Client6` (pending owner mock playtest + commit).**
> Client-only (no backend change); the battalion live path still needs the `AddGauntletBattalion`
> migration applied. Resume at item 3.

1. ~~**Client: wire the battalion editor to the live endpoints.**~~ **DONE (uncommitted).** Mirrored
   `GauntletBattalionResponse`/`SetGauntletBattalionRequest` into client `Dtos`; added
   `GetGauntletBattalionAsync`/`SetGauntletBattalionAsync` to `IRotaApi`/`HttpRotaApi`/`MockRotaApi`
   (mock is stateful, mirrors the live caps/ownership/band/dup gates + the `(ATK×4+DEF)` power);
   `GauntletScreen` editor is now server-driven (per-tap PUT → authoritative response; **power is the
   server value, no client recompute**; PlayerPrefs path removed; failed save reverts + shows the
   error in-overlay).
2. ~~**Obsidian Gilt slice 2** — Quest/Raid detail surfaces adopt the rounded template.~~ **DONE
   (uncommitted).** New reusable `.is-selected` USS ring (replaces inline selection borders);
   RaidScreen list cards `.panel`→`.card` + boss/difficulty selection via `.is-selected`; QuestScreen
   navigator kicker + chip-ified attempt-rewards (replace-only contract kept); RaidCombatView **light
   touch** (owner-chosen) — ranks/log rounded to `.card`, Hit→`.btn-cta`, **Share + raid-chat converted
   to `OverlayPanel` pop-outs** (immersive arena/log preserved). QuestScreen was already on-template.
3. **Deferred test** — a positive battalion-damage integration test through `HitRaidAsync` (needs the
   `RaidServiceTests` bundle to expose the battalion mock; the 8 service tests cover the power formula).
4. **Known follow-ups** — live data for rail notification dots (lootable-raid + guild-unread counts);
   a daily-reward claim backend behind the 🎁 header button; the lore→items content phase (art/lore
   slots are reserved across the new screens); **Obsidian Gilt slice 3** could card-ify the remaining
   surfaces (Raid `.menu-card` landing, the immersive combat header/arena) if the owner wants more.

## What's IN v0.3.1 (by area)
- **Gauntlet battalion (System 24 D8) — backend `665f903`.** Entity `PlayerGauntletBattalion`
  (generals/troops JSON, caps 6+20) + EF config + migration + repo + `GauntletBattalionService`
  (read/assign + power = **`(pATK+ΣbATK)×4 + (pDEF+ΣbDEF)×1`** on EFFECTIVE stats; validates ownership /
  UnitType band / no-duplicates) + DTOs + validator + `GauntletController` GET/PUT + DI. **Combat fork**
  in `RaidService.HitRaidAsync`: Gauntlet strike damage = **battalion power × RNG × crit** (full-replace —
  char base/legion/trophy/off-cap auras[block removed]/PowerScaling/all procs/magic-crit/flat-damage gated
  off; crit kept; strike FLAT 1 ticket). Non-Gauntlet + guild hits byte-for-byte unchanged (review clean).
- **Backend `ba3dc62`** — zone-scoped quest difficulty unlock · sigils only from the zone-final boss ·
  Pano chase curve (`0.5% + 4.5%·d/(d+50k)`, cap 5%). **`13497bf`** — gem balance on
  `PlayerProfileResponse` (ledger SUM via `IGemService`) for the header GOLD+GEMS plates.
- **Client (local, through `cc87b1f`)** — Obsidian Gilt shell (left icon rail, slim gold-glass header,
  rounding pass), Profile rebuilt on the Gauntlet template (hero card + equipped rail + pop-out nav),
  header GOLD+GEMS plates, bars recolored (energy green/stamina yellow/guild purple/HP red), rail reorder
  Home·Profile·Quest·Raids·Guild·Legion + ⚙ Options foot tile, the 5 extras (crest level badge,
  active-tile accent, rail notif dots, XP sliver, 🎁 daily-reward stub), and the **8 class tier emblems**
  (white backgrounds flood-filled to transparent; shown by tier bracket via `ClassEmblem` in the profile
  portrait + header badge). Class→emblem map (by LEVEL bracket): Conscript<500 · Legendary≥500 ·
  Ascendant≥1000 · Luminary≥2000 · Immortal≥5000 · Archon≥7500 (also Ancient≥10000) · ElderAncient≥15000 ·
  Eternal≥25000.

## Locked decisions / standards (don't re-litigate)
- **Battalion power formula** is LOCKED: `(pATK+ΣbATK)×4 + (pDEF+ΣbDEF)×1`; Discernment crit is passive,
  never shown in power; slots 6 generals + 20 troops. Gauntlet combat = **full replace** (mirror the mock).
- **Owner UI standards** (memory `owner-ui-standards`): pop-outs not expansions; rewards in ONE fixed
  replace-only slot; action buttons never move; no grey buttons; locked options unselectable;
  **Obsidian Gilt is the chosen skin; the Gauntlet page is the app-wide template.**
- **Mock fidelity** (memory `mock-fidelity-playtest`): owner playtests in mock; keep mocks stateful and
  verify the live path separately.

## Read order for a new chat
1. **This file.**
2. `CLAUDE.md` — "Current build status" (per-system history) + architecture/security rules.
3. Memory: `tickets-playtest-061126` (the full 2026-06-11→13 record), `owner-ui-standards`,
   `mock-fidelity-playtest`, `t76-gauntlet-foundation`.
4. `docs/specs/active/system-24-gauntlet-event-experience.md` §0b (battalion D8 + Gauntlet event).
5. `docs/ROTA_Function_Reference.md` — interfaces/controllers/entities signatures.

## Run commands
- `docker-compose up -d` · `dotnet build` · `dotnet test tests/ROTA.UnitTests/ROTA.UnitTests.csproj`
- `dotnet run --project src/ROTA.Api` (server :5035) · migrations: `dotnet ef database update
  --project src/ROTA.Infrastructure --startup-project src/ROTA.Api`
- Client headless compile: `dotnet build %TEMP%\rota-client-check\check.csproj`
