# ROTA — v0.2.2 (pre-UI): class regen + raid size set + raid on-hit rewards — SPEC

*Branch: `feat/v0.2.2-class-regen-raid-rewards`. Stabilization + small mechanics, pre-UI.*
Obey `CLAUDE.md` and `docs/ARCHITECTURE.md`. If a step is ambiguous or blocked, STOP and report.
This batch DOES change a DTO (additive only) and an enum (with a migration). It does NOT remove or
rename any existing endpoint/field.

## Environment & tool rules (pre-staged)
- You are already on branch `feat/v0.2.2-class-regen-raid-rewards`; Docker (Postgres+Redis) is running.
- **Bash tool only.** Allowed shell commands: `dotnet build …`, `dotnet test …`, `dotnet ef …`,
  `git add …`, `git commit …`. Read-only `git status`/`git log`/`git diff` are fine. NOTHING else —
  no `git checkout`/`branch`/`rm`/`docker`/`dotnet run`/PowerShell.
- Files via Write/Edit. Do NOT touch `.claude/`, transcripts, or settings. If anything is blocked,
  STOP and report `BLOCKED` + the exact step.
- Per component: edit → `dotnet build` (0 warnings, 0 errors) → migrations if any → `dotnet test`
  green → ONE commit, last line exactly `Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>`.
- Do NOT push, do NOT tag. Baseline to keep green: **186 unit + 7 integration = 193**.

## Locked design decisions
- Raid on-hit XP = a SINGLE roll in [1.0, 4.0] per hit × stamina spent (NOT per-stamina rolls, NOT
  per-raid yet). Gold per hit = stamina spent × a per-raid `goldPerStamina` (default 1).
- Hit sizes stay **1 / 5 / 20** — no new sizes (the "5×20" rapid-dump is a future client button).
- Raid sizes = Personal/Small/Medium/Large/Titanic with participant caps **1/10/25/50/250**.
- Energy stays quest-only; stamina stays raid-only (already true — just verify).
- Discernment crit is OUT (v0.2.3). Quest drop-quality is OUT (later).

---

## Component A — Verify energy is quest-only (test, ~no code)
Add a unit test asserting the resource separation so it can't silently regress: `QuestService`
spends `ResourceType.Energy`; `RaidService` spends `ResourceType.Stamina`. A lightweight test
(e.g., in an existing service test) that confirms a quest attempt calls `SpendEnergyAsync` with
`ResourceType.Energy` and a raid hit calls it with `ResourceType.Stamina`. If suitable assertions
already exist, note that and skip. Commit: `test: lock energy=quest / stamina=raid resource split`.

## Component B — Energy/Stamina/GuildStamina regen from ClassConfig
Today `EnergyService.ComputeLiveValue` regenerates using the stored `PlayerResource.RegenPerMinute`
(points/minute). Switch the live-value source to the player's CLASS regen rates.
1. `ClassConfig.GetRegenRates(class)` / `IClassService.GetRegenRates(class)` return
   `RegenMinutesPerPoint` values (minutes to regen ONE point) for energy, stamina, guild stamina.
   Conversion: `pointsRegenerated = minutesPerPoint > 0 ? (int)(elapsedMinutes / minutesPerPoint) : 0`.
2. Inject `IPlayerRepository` and `IClassService` into `EnergyService`. In each public method
   (`GetCurrentEnergyAsync`, `SpendEnergyAsync`, `RefillEnergyAsync`), load the player's `Class`
   once, resolve the `RegenMinutesPerPoint` for the requested `ResourceType` via
   `IClassService.GetRegenRates(class)`, and pass it into `ComputeLiveValue`.
3. Change `ComputeLiveValue` to take the resource's `minutesPerPoint` (double) instead of reading
   `resource.RegenPerMinute`. Leave the `RegenPerMinute` column in place but unused for live
   computation (do NOT drop the column / no migration).
4. **Behavior change to flag in the commit + docs:** GuildStamina (previously `RegenPerMinute = 0`,
   i.e. no regen) will now regenerate at the class's GuildStamina rate. Confirm `ClassConfig` has a
   sane GuildStamina rate (it has `GuildStaminaRegenMinutes`, default 2.0).
5. Map `ResourceType` → which class rate: Energy→energy, Stamina→stamina, GuildStamina→guild.
6. Update `EnergyServiceTests` to mock `IPlayerRepository` (returns a player with a known `Class`)
   and `IClassService` (returns known `RegenMinutesPerPoint`), and assert live regen now follows the
   class rate, not the stored `RegenPerMinute`. Keep coverage for: no-regen (minutesPerPoint ≤ 0),
   cap at MaxValue, spend/refund.
7. Build + test green. Commit: `feat(energy): derive energy/stamina/guild regen from class config`.

## Component C — Raid size set (Personal/Small/Medium/Large/Titanic) + participant caps
1. `src/ROTA.Domain/Enums/RaidSize.cs`:
   ```csharp
   public enum RaidSize
   {
       Personal = 0,  // sigil solo raid — 1 participant (summoner only)
       Small    = 1,  // cap 10
       Medium   = 2,  // cap 25
       Large    = 3,  // cap 50  (was 2 — see migration)
       Titanic  = 4,  // cap 250
   }
   ```
2. EF: `ActiveRaidConfiguration` — `Size` store default is `RaidSize.Large` (now =3); update
   `.HasDefaultValue(RaidSize.Large).HasSentinel(RaidSize.Large)` (both now resolve to 3).
3. Migration `ExpandRaidSizeSet`: in `Up`, **first** remap existing rows with raw SQL
   `UPDATE active_raids SET size = 3 WHERE size = 2;` (old Large=2 → new Large=3), THEN let the
   generated `AlterColumn` set the new default (3). In `Down`, reverse: default→2 and
   `UPDATE active_raids SET size = 2 WHERE size = 3;`. Create AND apply it.
   (Personal=0 and Small=1 are unchanged; no other values exist.)
4. Participant caps: add a static readonly map in `RaidService`:
   `Personal=1, Small=10, Medium=25, Large=50, Titanic=250` (mirror the existing `HpMultipliers`
   style). Add `RaidHitFailureCode.RaidFull` (next free value) to the DTO enum.
5. Cap enforcement in `HitRaidAsync`, PRE-spend (right after the Personal access gate, step 3a, so
   no stamina is spent on rejection): if `raid.Size != RaidSize.Personal`, look up whether the player
   is already a participant (`_participants.FindByRaidAndPlayerAsync`); if NOT already a participant
   AND `raid.ParticipantCount >= cap[raid.Size]`, return `HitFail(RaidHitFailureCode.RaidFull, "This
   raid is at its participant cap.")`. (A small over-cap race is acceptable — not security-critical.
   Personal cap=1 is already enforced by the access gate.)
6. `RaidController`: map `RaidHitFailureCode.RaidFull` → `409 Conflict` (or `422`; match the existing
   style for capacity-type failures).
7. Tests: extend `RaidSizePersistenceTests` to round-trip ALL FIVE sizes (Personal/Small/Medium/
   Large/Titanic) — this guards the migration + sentinel. Add a `RaidService` unit test: a non-
   participant hitting a raid already at its cap gets `RaidFull` with NO stamina spent; a player
   already participating is not blocked. Update any existing `RaidSize.Large` test expectations
   (the name is unchanged; only the int value moved 2→3, which tests shouldn't hard-code).
8. Build + apply migration + test green. Commit: `feat(raids): Personal/Small/Medium/Large/Titanic size set + participant caps`.

## Component D — Raid on-hit XP + gold + raid-log fields
1. DTO `RaidHitResponse` (`src/ROTA.Shared/DTOs/RaidDTOs.cs`): ADD `public int XpGained { get; set; }`
   and `public long GoldGained { get; set; }` (additive — do not touch existing fields; `DamageDealt`
   already represents health lost per hit).
2. `RaidDefinition` model + `raids.json`: add `goldPerStamina` (long/int, default 1) per raid.
3. In `HitRaidAsync`, INSIDE the `AtomicApplyHitAsync` block (same transaction as the damage/
   participant write), AFTER the participant upsert and BEFORE the kill-reward block:
   - XP: `double xpRoll = 1.0 + _random.NextDouble() * 3.0;`  (uniform [1,4])
     `int xpGained = Math.Max(1, (int)Math.Round(staminaCost * xpRoll));`
   - Gold: `long goldGained = (long)staminaCost * definition.GoldPerStamina;`
   - Apply to the already-loaded `player`: `var hitLevelUps = player.AddExperience(xpGained, lvl =>
     _stats.XpToNextLevel(lvl)); player.AddGold(goldGained); await _players.UpdateAsync(player, ct);`
   - Handle level-ups EXACTLY as the kill path does (mirror `DistributeKillRewardsAsync`: call
     `_stats.GrantLevelUpPointsAsync(playerId, level, ct)` for each returned level). On a killing hit,
     this on-hit grant runs first, then kill rewards stack on top — both are intended.
   - Capture `xpGained`/`goldGained` in outer variables so they can populate the response.
4. Populate `response.XpGained` and `response.GoldGained` at the response-build site (~line 336). The
   idempotency cache stores this response, so replays return the same per-hit values.
5. Tests (`RaidServiceTests`): a hit grants XP in `[staminaCost, staminaCost*4]` and gold =
   `staminaCost * goldPerStamina`; the response carries both; XP that crosses a level threshold fires
   `GrantLevelUpPointsAsync`. Use a seeded/deterministic `Random` (the service already accepts an
   injected `Random`) to make XP assertions stable.
6. Build + test green. Commit: `feat(raids): on-hit XP (1-4/stam roll) + gold + raid-log fields`.

## FINAL — verify & hand back (no push, no tag)
- `dotnet build` (0 warnings) + `dotnet test` (all green) — paste the summary.
- Report: every file changed per component, the new migration name, `git log --oneline`, and confirm
  both that the migration applied and that the GuildStamina-now-regenerates behavior change is
  intentional and documented. The supervisor runs the `dotnet run` smoke test + traces regen math
  and the raid-size migration.

## OUT OF SCOPE (do not attempt)
- Discernment crit (v0.2.3) · quest drop-quality (later) · Unity/SDK (v0.3.0) · moderation
  (back-burnered) · per-raid xpPerStamina · reward-atomicity rework of the stamina spend ·
  any endpoint/field removal/rename · pushing or tagging.
