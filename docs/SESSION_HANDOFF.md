# ROTA Session Handoff — 2026-06-08 (System 22 Masteries → PLAYTEST)

## TL;DR (resume here)
**System 22 Phase A (Masteries) is COMPLETE and merged to `main`**, the local DB is updated, and the
**Unity client masteries feature is built + merged to client `master`** and compiles clean. Everything is
green. The owner is now doing a **real + mock playtest**. This handoff is for the NEXT chat to **process
playtest feedback** (fix mock-fidelity/client bugs, tune magnitudes, optionally wire the deferred loot bits).

- **Backend repo:** `C:\Users\xolac\OneDrive\Documentos\Projects\ROTA` — branch `main`. Build: **0 errors,
  0 CS warnings** (4 pre-existing MSB3277 only). Tests: **787 unit + 88 integration green**.
- **Client repo:** `C:\Dev\ROTA.Client6` — branch `master` (masteries + SignalR-chat merged via fast-forward).
  Unity **6000.4.9f1** headless compile: **exit 0, zero error CS**.
- **DB:** `AddMasterySystem` + `AddMasteryRespecLedger` APPLIED to local docker Postgres. All migrations current.
- **Nothing pushed** (local merges only). API serves on **http://localhost:5035** (client live URL matches).

---

## HOW TO PLAYTEST

**Backend (real mode):**
```
cd C:\Users\xolac\OneDrive\Documentos\Projects\ROTA
docker-compose up -d            # if not already running (postgres + redis)
dotnet run --project src/ROTA.Api
```
Swagger at the launch URL; masteries: `GET /api/masteries`, `POST /api/masteries/pledge`,
`POST /api/admin/masteries/force` (AdminOnly).

**Client:** open `C:\Dev\ROTA.Client6` in Unity 6000.4.9f1 (now on `master`). On the **AppBootstrap**
GameObject, the **`useMock`** checkbox picks mock (✓) vs live (☐, → localhost:5035). Play → login → Home → **✦ Masteries**.
- **Mock:** dev-force is instant. Seeded mixed levels + Hoard pledge. `MAX ALL → L5` = jackpot (rating 56, "Ascendant").
- **Live:** log in as admin **`Xolaces`** for dev-force (it's AdminOnly; a fresh account 403s). Force shows a confirm dialog.

---

## WHAT'S DONE

### Backend — System 22 Phase A (7 slices + dev-force), all merged to `main`
Spec: `docs/specs/active/system-22-masteries-core.md`. Full per-slice detail in `docs/PROJECT_STATE.md`
(System 22 entries) + `CLAUDE.md` (System 22 build-status block) + `docs/ROTA_Function_Reference.md`.
- 4 Ancients (Wrath/Bulwark/Hoard/Discernment) level 1→5 via challenge checklists; always-on global + pledge
  (≈×2) modifiers through EXISTING combat/loot hooks (NO new combat path). `IMasteryService` (NOT ConditionalBonus).
- Wrath → `totalLegionBonus`; Bulwark → `FlatDamagePercent` (guild raids, hard-capped); Hoard → quest `Scale` +
  gold; Discernment → sigil-find + quest item rarity-upgrade (`UpgradesTo`). Formula-B rating + titles + leaderboard
  board. Lossless re-spec (`POST /api/masteries/pledge`): free-unlock → free-monthly → paid-weekly (Redis cap + idempotent gems).
- Dev-force: `POST /api/admin/masteries/force` [AdminOnly] (`PlayerMastery.ForceSetLevel`, `IMasteryService.ForceLevelsAsync`).
- Key files: `src/ROTA.Application/Services/MasteryService.cs`, `src/ROTA.Application/Services/RaidService.cs`
  (Wrath/Bulwark/Hoard hooks + activity counters), `src/ROTA.Application/Services/QuestService.cs` (loot hooks),
  `src/ROTA.Api/content/masteries.json` (magnitudes + challenge thresholds — the TUNE surface),
  `src/ROTA.Application/Configuration/MasteryConfig.cs` (scalar dials).

### Client — `C:\Dev\ROTA.Client6` (branch `master`, commit `672e47f`)
- DTO mirror in `Assets/ROTA.Client/Runtime/Api/Dtos.cs`; `IRotaApi.IsMock` + `GetMasteries`/`Pledge`/
  `ForceMasteryLevels` on `HttpRotaApi` + stateful `MockRotaApi` (faithfully replicates Formula-B rating/titles/magnitudes).
- `Assets/ROTA.Client/Runtime/Screens/MasteriesScreen.cs` (Home landing card → "Masteries" route in `AppBootstrap`):
  4 Ancient cards, rating + titles, pledge buttons, dev-force panel (per-Ancient L1–5, APPLY + MAX ALL) with a
  LIVE confirm dialog gate (`api.IsMock` decides).

---

## OPEN FOLLOW-UPS (candidate work for the next chat, post-playtest)
1. **Playtest feedback** — fix any mock-fidelity gaps or client display bugs the owner reports (the recurring
   pattern: mocks must be stateful so live fixes don't look broken in mock — see memory `mock-fidelity-playtest`).
2. **Deferred loot wiring** (backend): raid threshold-drop Hoard scaling + gear-drop / raid quality-upgrade
   (skipped to avoid per-participant reads on the kill path; `GearDefinition.UpgradesTo` field+validation already ship).
3. **TUNE** magnitudes + challenge thresholds in `content/masteries.json`; the breadth micro-bonus is OFF
   (`MasteryConfig.BreadthMicroBonusPercent = 0`).
4. **Reserved-but-unwired** activity counters: `EnergySpent`/`StaminaSpent`/`LevelGained` (no checklist uses them yet).
5. **Paid-respec crash-recovery gap** (strict weekly cap; PHASE-2 note in `MasteryService`).
6. **Dev-force open-to-any-authed-in-Development?** — currently AdminOnly; owner may want it relaxed for non-admin dev accounts.
7. **Phase B (The Rise)** + **Phase C (PoE-depth)** remain in backlog (`docs/specs/backlog/system-22-ancients-rise-and-masteries.md`).

## REPO / BRANCH STATE
- Backend `main`: all System 22 work merged (slices 1–7 + dev-force). Stale merged branches `feat/system22-masteries-s1..s7`
  + `-devforce` exist locally (prunable). Nothing pushed.
- Client `master`: masteries (`672e47f`) + SignalR-chat fast-forwarded in. Branches `feat/masteries-client`,
  `feat/signalr-chat` exist (prunable). Nothing pushed.
- Migrations applied locally; remote/prod would need `dotnet ef database update`.
