# System 19 — Raid Sharing (private-until-shared + join by UID)

*Status: SHIPPED 2026-06-04 — all three slices complete (backend core, sigil size, client UI).
§5 decision resolved. Traced against current raid code 2026-06-04.*

> **SUPERSEDED IN PART by System 23 / Ticket 50** (`../active/system-23-raid-visibility-lifecycle.md`).
> The boolean `ActiveRaid.IsPublic` + `Share()` described below were replaced by a `RaidVisibility`
> tier enum (Private/Public/GuildOnly/FriendsOnly) + a `RaidLifecycleState` enum
> (Active/Lootable/Looted) with a summoner-only `POST /api/raids/{id}/loot` (dismiss). `IsPublic`
> remains on the wire as a **derived** read-only field (`= Visibility == Public`) for back-compat.
> The "active raid (hittable by id) ≠ public raid (listed)" distinction first noted here is now
> made explicit in code.

## Goal (owner)
Summoned raids are **private until shared to public**. Two ways to join someone's raid:
1. **Direct raid UID** — paste a raid's id to join it.
2. **Public raid list** — browse shared raids.

A **Share** control lives **inside the raid screen**, with both:
- a **copy-paste area showing the raid UID**, and
- a **button to share the raid to the public list**.

## Current model (traced — what exists today)
- `ActiveRaid`: `Id` (GUID), `SummonedByPlayerId`, `Size` (Personal/Small/Medium/Large/Titanic,
  caps 1/10/25/50/250), HP, `Difficulty`, `ExpiresAt`, `IsDefeated`. **No visibility field.**
- **Players summon via Sigils** (`POST /api/items/{id}/use` → `ItemService` → `SummonRaidAsync`).
  Sigils default to **`Personal`** size; content can override via `SummonSize`
  (`ItemService.cs:80-83`). The `/api/raids/{id}/summon` endpoint is **AdminOnly**.
- `GET /api/raids` (`RaidService.GetActiveRaidsAsync:159`) filters
  `r.Size != Personal || r.SummonedByPlayerId == playerId` — i.e. **"private" == Personal size**,
  visible only to its summoner; everything else is public.
- Hit access gate (`RaidService:326`): **Personal raids → only the summoner may strike**
  (`AccessDenied` 403). Non-Personal raids are hittable by **anyone who has the id**, capped by Size.
- No `GET /api/raids/{id}` (single-raid) endpoint exists — the client opens combat from a list row.

**Key consequence:** the raid `Id` is already an unguessable GUID that grants hit-access to any
non-Personal raid. It *is* the invite token. So "join by UID" mostly needs a **get-by-id** endpoint
so the client can open the combat view from a pasted id.

## Design — decouple VISIBILITY from SIZE via `IsPublic`

### Domain
- `ActiveRaid.IsPublic` (bool, **default `false`** — summons start private).
- `ActiveRaid.Share()` domain method → sets `IsPublic = true`, bumps `UpdatedAt`.
- `Create(...)` gains `bool isPublic = false`.

### Access & listing semantics
| | In public list (`GET /api/raids`) | Joinable (hit) |
|---|---|---|
| Public non-Personal (`IsPublic=true`) | yes (everyone) | anyone with id, up to Size cap |
| **Private** non-Personal (`IsPublic=false`) | only the **summoner** sees it | **anyone with the id** (UID = invite) |
| Personal (any `IsPublic`) | only the summoner | **summoner only** (unchanged) |

- `GetActiveRaidsAsync` filter becomes:
  `(r.IsPublic && r.Size != Personal) || r.SummonedByPlayerId == playerId`
  — public shared raids for everyone, plus the caller's own (private) raids so they can re-open and
  share them.
- Hit access gate **unchanged** (Personal → summoner-only; non-Personal → id-bearer). A private
  non-Personal raid is already joinable by id; we simply stop *listing* it until shared.

### Endpoints
- **NEW** `GET /api/raids/{activeRaidId}` `[Authorize]` → `ActiveRaidResponse` for join-by-UID.
  Returns the raid regardless of `IsPublic` (the GUID is the token). `404` if not found / deleted /
  expired / defeated. (Personal raids owned by someone else → `404` to avoid leaking their existence.)
- **NEW** `POST /api/raids/{activeRaidId}/share` `[Authorize]` → summoner-only. Sets `IsPublic=true`,
  writes `audit_log`, returns updated `ActiveRaidResponse`.
  - `403` if caller ≠ summoner; `404` if not found/expired; `409`/`422` if `Size == Personal`
    (a solo raid can't be shared) — surfaced clearly to the client.
- `GET /api/raids` unchanged signature; filter updated (above).

### DTOs (`ROTA.Shared/DTOs/RaidDTOs.cs`)
- `ActiveRaidResponse` gains **`bool IsPublic`** (client shows shared-state + the Share button) and
  already carries `ActiveRaidId` (the UID to copy) and `SummonedByUsername`.
- `ShareRaidResult { bool Success; ShareRaidFailureCode FailureCode; string? FailureReason;
  ActiveRaidResponse? Raid; }` + `enum ShareRaidFailureCode { None, NotFound, NotSummoner,
  CannotSharePersonal }`.
- After these change, run `/audit-dtos` and mirror into the client `Dtos.cs`.

### Client (Unity — raid screen)
- **Share panel inside the raid view** (combat view or the raid menu when you own the active raid):
  - a read-only/selectable field showing the **raid UID** (`ActiveRaidId`) with a **Copy** button
    (UI Toolkit: `TextField` set to the id + a copy that writes `GUIUtility.systemCopyBuffer`),
  - a **"Share to public"** button → `POST /api/raids/{id}/share`; on success flip to "Shared ✓" and
    surface `CannotSharePersonal`/not-summoner errors.
  - Only shown when `SummonedByUsername == me` (or a `YouAreSummoner` flag) and `!IsPublic`.
- **Join by UID:** a paste field + "Join" on the Public/Summon tab → `GET /api/raids/{id}` → open
  `RaidCombatView` with the returned raid. Handle `404` (bad/expired id) with a clear message.
- New client API methods: `GetRaidByIdAsync(Guid)`, `ShareRaidAsync(Guid)`.

## §5 DECISION (resolved 2026-06-04, owner)
**Sigil size is per-sigil, content-driven** — predetermined by the raid each sigil summons; sizes
grow as content is added. **The first quest boss's sigil summons a `Small` raid.** So Slice 2 sets
the existing early-game sigils to `Small` in `content/items.json` (`SummonSize`); future raids/sigils
get their own sizes. `Personal` is no longer the sigil default — summoned raids are multiplayer
(non-Personal), private until shared, using the multiplayer `BaseHp` pool. The backend core (Slice 1)
is identical regardless; only the sigil content/default changes.

**Economy intent (owner, durable — see [[raid-summon-economy]]):** sigils are the *consumable* that
gates summoning — **no cooldowns**. The long-term gate is **energy → quests → quest-boss sigil drops
→ summon raid**, keeping energy meaningful into mid/late game (in DotD energy became pointless
late-game besides bragging/achievements).

## Slices
1. **Backend core** ✅ (`043f172`, merged `7981d2a`): `IsPublic` + `Share()` + migration `AddRaidVisibility`;
   list-filter update; `GET /api/raids/{id}`; `POST /api/raids/{id}/share`; `ActiveRaidResponse.IsPublic`;
   `SummonRaidAsync` passes `isPublic:false`. Unit + integration tests (share by non-summoner 403,
   private not listed to others / listed to summoner, get-by-id any visibility, share-Personal rejected).
2. **Sigil size** ✅ (`0961b43`): early-game sigils set to `Small` in `content/items.json` (`SummonSize`).
3. **Client** ✅ (`bd50794`, ROTA.Client6 `master`): Share panel inside `RaidCombatView`
   (summoner-only, non-Personal) — selectable UID field + Copy (`GUIUtility.systemCopyBuffer`) +
   "Share to public list" → `ShareRaidAsync`, flips to "Shared ✓", surfaces NotSummoner /
   CannotSharePersonal. Join-by-UID card atop the Public tab → `GetRaidByIdAsync` → open combat
   (handles invalid/404). PRIVATE badge on own unshared cards. DTOs mirrored (`IsPublic`,
   `ShareRaidResult`, `ShareRaidFailureCode`); `GetRaidByIdAsync`/`ShareRaidAsync` on `IRotaApi`
   (Http maps status→result; Mock store flips `IsPublic`). Headless-compiled: 0 `error CS`.

## Non-goals (defer)
Invite revocation, per-player allowlists, re-privatising a shared raid, share links/deep-linking.
