# Client ↔ Backend DTO Drift Audit & Fix Plan (2026-06-05)

**Context:** backend = `AddControllers()` System.Text.Json defaults (camelCase property
names, **enums serialized as integers**). Client = Newtonsoft.Json (silently coerces
number→string as a *digit* string; **throws** on number→List / object→scalar). Backend is
the source of truth — client `Dtos.cs` must mirror it field-for-field.

**Client file:** `C:\Dev\ROTA.Client6\Assets\ROTA.Client\Runtime\Api\Dtos.cs`
(EDITOR-GATED — every edit triggers a Unity domain reload; headless-compile 0 `error CS`
after, and do it only with the Editor closed.)

Verified against backend `src/ROTA.Shared/DTOs/*` on 2026-06-05. `[V]` = personally
re-confirmed against backend source this session.

---

## CRASH — fix first (throw at runtime on valid data paths)

1. **UseItemResponse.SummonedRaidId (`Guid?`)** → backend **`RaidSummoned` (`SummonRaidResponse?`, an object)**.
   Newtonsoft throws the moment a sigil use successfully summons a raid (object→scalar).
   Rename client field to `RaidSummoned`, type `SummonRaidResponse?`. `[V ItemDTOs.cs:24]`
2. **LegionDetailResponse.ComputedPower (`long`)** → backend **`LegionPowerResult` object**
   `{ double RawPower; double LegionBonusFraction; double UnitSum; }`. Throws on *any* legion
   detail load. Add the `LegionPowerResult` class to the client; retype the field. `[V LegionDTOs.cs:70,94]`
3. **LegionDetailResponse.GeneralSlots + TroopSlots (two `List<LegionSlotResponse>`)** → backend
   single **`Slots: List<SlotAssignmentResponse>`**. Both client lists stay empty (slot UI blank,
   no throw because the JSON key `slots` matches neither). Replace both with `Slots` and add
   `SlotAssignmentResponse { string Family; int SlotIndex; string ConstraintType; string? ConstraintValue; string? UnitDefinitionId; string? UnitName; }`. `[V LegionDTOs.cs:77-95]`

## WRONG — silent bad data

4. **Enum FailureCode typed `string`** on client → backend sends the enum as an integer, so the
   client gets `"0"`/`"3"` (digit string), never matching named comparisons. Fix on:
   `QuestResultResponse`, `UseItemResponse`, `BuyMagicResponse`, `BuyUnitResponse`, `BuyLegionResponse`.
   Prefer declaring a matching client enum (same names/values) and typing the field as that enum
   (Newtonsoft maps number→enum by underlying int) — keeps readable comparisons.
5. **UseItemResponse.SkillPointsGranted** → backend **`StatPointsGranted`** (rename — stat-bag
   grants currently always read 0). Also add backend's `ItemDefinitionId`, `QuantityConsumed`,
   `RemainingQuantity`, `FailureReason`. `[V ItemDTOs.cs:15-25]`
6. **InventoryItemResponse.IconPath** → backend **`ArtKey`** (rename — icon always null). Drop
   client-only `StatPointsOnUse` / `SummonRaidId` / `SummonDifficulty` (backend never sends; those
   are *definition* data, need a content endpoint if wanted). `[V ItemDTOs.cs:3-13]`
7. **PlayerProfileResponse DisplayName + Class** — **FIXED BACKEND-SIDE this batch** (commit
   `512bcce`): backend now sends both; the client's existing (previously-null) fields populate with
   **no client change**. Optionally add `EffectiveAttack`/`EffectiveDefense` to the client (backend sends them). `[V PlayerDTOs.cs:3-18]`
8. **AllocateStatResponse** — remove client `NewBaseAttack`/`NewBaseDefense`/`NewBaseMaxHealth`
   (absent on backend); add `StatType`, `AmountAllocated`, `NewMaxEnergy`, `NewMaxStamina`,
   `NewMaxGuildStamina`, `CurrentLsi`.
9. **Legion ownership DTOs** `[V LegionDTOs.cs]`: `OwnedUnitResponse` — drop `GemPrice`, add
   `Attribute`/`HasAbility`/`LegionBonus`. `OwnedLegionResponse` — drop `GemPrice`, add `PowerBonus`.
   `OwnedMagicResponse` — drop `GemPrice` (not on backend's owned DTO). `CommanderGearResponse` —
   `Name`→`GearName`, add `GearDescription`/`Note`.
10. **UpdateUsernameResponse** — add to client `{ string Username; DateTimeOffset UpdatedAt; }`.

## Perfect matches (checked, no change)
Auth (Register/Login/Refresh/AuthResponse), ResourceValueResponse, Update(Display)Name req/resp,
ClassInfoResponse↔ClassRegenRates, Quest availability/attempt, **all Raid DTOs** (ActiveRaid,
RaidHit req/resp, RaidRewards, CompletedRaid, RaidParticipantRankDto — prior Id/Key crashes already
fixed), ItemGrantDTO, EquipRequest, OwnedGearResponse, **all Leaderboard DTOs**.

---

## Leaderboards — separate client bug (NOT DTO drift; data/eligibility fixed this batch)

- **CLIENT BUG (Editor-gated):** `LeaderboardScreen.SelectBoard` passes `board.CurrentPeriodKey`
  (a *weekly* key, e.g. `week:2026-W23`) for **every** period tab. Clicking a **Monthly** tab sends
  that weekly key with `period=Monthly` → server `ValidatePeriodKeyFormat` rejects it → **400**.
  Fix: pass `null` as `periodKey` (the server resolves the correct current key for the selected
  period). `LeaderboardScreen.cs:156`.
- **Backend/data — FIXED this batch (commit `c07969e` + snapshot):** root cause of "empty boards"
  was eligibility, not a code bug — the only player is the admin `Owner` at L3, filtered by
  `MinLevel=20` **and** `ExcludeAdmins=true`. Dev override (`appsettings.Development.json`:
  `MinLevel=1`, `ExcludeAdmins=false`) + `leaderboard-refresh-stat` now make all 6 boards show the
  owner's data. **Production keeps the locked L20 + exclude-admins rules.** Real boards populate once
  there are eligible (≥L20, non-admin) players.
