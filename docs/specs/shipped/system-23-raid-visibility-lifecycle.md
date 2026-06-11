# System 23 — Raid Visibility & Indexing model (Ticket 50)

*Status: BACKEND COMPLETE 2026-06-08 (migration created, NOT applied). Client mirror is a separate
later step. Supersedes the `ActiveRaid.IsPublic` boolean from System 19.*

## North-star clarification — "active" ≠ "public"

Two orthogonal concepts that were previously conflated under one boolean:

- **Active raid** = alive (not defeated/expired/deleted) and **hittable by its GUID**. The raid id is
  the invite token; a raid is joinable by id regardless of which list it appears in. This is the
  `GetRaidByIdAsync` / `HitRaidAsync` path.
- **Listed raid** = **indexed** in the public / guild / friends list. This is `GetActiveRaidsAsync`.
  A Private/GuildOnly/FriendsOnly raid is still joinable by id — it just doesn't show in someone
  else's list.

The code comments throughout `RaidService`, `ActiveRaid`, and the two new enums emphasize this split.

## What shipped

### Visibility tiers (replaces `IsPublic`)
`RaidVisibility { Private=0, Public=1, GuildOnly=2, FriendsOnly=3 }` (Domain enum).
- `ActiveRaid.Visibility` (private set) replaces `bool IsPublic`. New summons start **Private**.
- `ActiveRaid.ShareTo(RaidVisibility)` sets the tier. `Share()` is kept as a back-compat overload
  that defaults to **Public** (so the currently-shipped client + existing callers/tests still work).
- `ShareRaidAsync(callerId, raidId, visibility = Public)`: summoner-only; rejects Personal
  (`CannotSharePersonal`); for `GuildOnly` validates the summoner is in a guild
  (`NotInGuild`); coerces a `Private` target to `Public` (no un-share path — non-goal); audits the tier.
- `GetActiveRaidsAsync` list predicate (caller's `guildId` + accepted-friend set resolved **once**
  before the in-memory filter):
  `LifecycleState == Active && (own raid || (Size != Personal && (Public ||
   (GuildOnly && callerGuildId != null && SummonedByPlayer.GuildId == callerGuildId) ||
   (FriendsOnly && acceptedFriendIds.Contains(SummonedByPlayerId)))))`.
  GuildOnly compares the **Include-loaded** `SummonedByPlayer.GuildId` in-memory (zero extra queries).

### Lifecycle (completed → lootable → looted)
`RaidLifecycleState { Active=0, Lootable=1, Looted=2 }` (Domain enum).
- **CRITICAL FINDING (verified in code):** raid rewards are **fully granted on the killing hit** inside
  `DistributeKillRewardsAsync` — there is **NO unclaimed-reward state**. Therefore **"loot" is a
  DISMISS / REMOVE-FROM-ALL-INDEXES action, not a reward claim.**
- `MarkDefeated()` now also sets `LifecycleState = Lootable`.
- `Loot()` guards `LifecycleState == Lootable`, sets `Looted`, bumps `UpdatedAt`. Does **NOT** soft-delete
  (`IsDeleted` stays false) so the `raid_participants` FK + completed-raid history stay intact.
- `LootRaidAsync(callerId, raidId)`: summoner-only dismiss. `NotFound` (missing/deleted/already-Looted),
  `NotSummoner`, `NotLootable` (still Active). Audits `RaidLooted`.
- `GetRaidByIdAsync`: the **summoner** may resolve their own **Lootable** raid (for the loot/dismiss
  screen); Looted resolves for no one; a non-summoner sees any defeated raid as gone (null).
- Lootable/Looted raids **never list** (the `LifecycleState == Active` gate).

## API surface
- `POST /api/raids/{id}/share` — body `ShareRaidRequest { Visibility = "Public" }` is **OPTIONAL**
  (no body / omitted → Public, back-compat). 200/400(bad tier)/403/404/409(Personal **or** NotInGuild).
- `POST /api/raids/{id}/loot` — summoner-only dismiss. 200/403/404/409(NotLootable). **[Authorize].**

## DTO contract
- `ActiveRaidResponse += string Visibility, string LifecycleState`; **`bool IsPublic` kept** as a
  derived read-only convenience (`= Visibility == Public`) so the currently-shipped client keeps working.
- `ShareRaidFailureCode += NotInGuild = 4`. New `ShareRaidRequest { string Visibility = "Public" }`.
- New `LootRaidResult { Success, FailureCode, FailureReason, Raid }` +
  `LootRaidFailureCode { None=0, NotFound=1, NotSummoner=2, NotLootable=3 }`.

## Persistence
- `ActiveRaidConfiguration`: `visibility` (int, default 0) + `lifecycle_state` (int, default 0);
  filtered index `ix_active_raids_visibility_lifecycle` on `(visibility, lifecycle_state)`
  `WHERE is_defeated = false AND is_deleted = false`.
- Migration **`AddRaidVisibilityModel`** (created, **NOT applied** — owner runs `dotnet ef database update`):
  add `visibility` (default Private) → backfill `is_public=true → visibility=1` → drop `is_public`;
  add `lifecycle_state` (default Active) → backfill `is_defeated=true AND is_deleted=false →
  lifecycle_state=2` (Looted, so legacy defeated raids don't show stale "loot me" prompts); create index.

## Decisions (resolved — applied as-is)
- **A.** `Loot()` is Lootable→Looted + remove-from-indexes; does **not** soft-delete (FK/history intact).
- **B.** Migration backfill: existing `is_defeated=true` raids → `Looted`.
- **C.** GuildOnly compares `SummonedByPlayer.GuildId == callerGuildId` in-memory (nav already Included).
- **D.** `IsPublic` kept on the wire as a derived convenience; `Share()` overload + no-body share endpoint
  default to Public, so the shipped client is unaffected.
- **E.** `ShareTo` may move between GuildOnly/FriendsOnly/Public; no un-share-to-Private path (non-goal).

## Accepted-friends lookup
`IFriendshipRepository.ListForPlayerAsync(playerId, FriendshipStatus.Accepted, ct)`, mapped through
`Friendship.OtherSide(playerId)` into a `HashSet<Guid>`.

## Tests
Unit (RaidServiceTests): summon → Private + Active; list tiers (Public/GuildOnly±guild/FriendsOnly±friend);
Lootable/Looted never listed; ShareRaid Public/GuildOnly/FriendsOnly + non-summoner + Personal + NotInGuild;
LootRaid summoner-on-Lootable / non-summoner / still-Active / already-Looted / missing; GetRaidById
summoner-resolves-Lootable / Looted→null / other's-Lootable→null; regression: killing hit grants rewards
**and** sets Lootable. Integration (RaidSizePersistenceTests): visibility default + ShareTo round-trip;
defeat→Lootable→Loot→Looted round-trip with `IsDeleted` staying false. **830 unit + 94 integration green.**

## Follow-ups
- Client mirror (ROTA.Client6): visibility picker on the share panel; loot/dismiss button on a defeated
  raid; surface GuildOnly/FriendsOnly badges. (Separate step — backend is authoritative + tested.)
