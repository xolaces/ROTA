# ROTA — v0.2.3: Discernment crit (raids) — SPEC

*Branch: `feat/v0.2.3-discernment-crit`. Adds a bounded crit system to raid hits.*
Obey `CLAUDE.md` and `docs/ARCHITECTURE.md`. STOP and report if blocked. Additive DTO change only;
no endpoint/field removal.

## Scope
Crit applies to **raid hits only** (`RaidService.HitRaidAsync` damage formula). Quests are node-based
with no per-hit damage roll — no crit there. Discernment quest-drop-quality is OUT (later).

## Locked numbers (tunable `CombatConfig` constants; the +10% chance cap is a HARD constraint)
- Crit chance = `BaseCritChance(0.05) + min(MaxCritChanceBonus(0.10), Discernment × CritChancePerDiscernment(0.0001))`
  → 5% at 0 discernment, +10% cap reached at 1,000 discernment, max 15%.
- Crit multiplier = `BaseCritMultiplier(1.5) + min(MaxCritDamageBonus(1.0), Discernment × CritDamagePerDiscernment(0.0002))`
  → 1.5× at 0 discernment, 2.5× cap reached at 5,000 discernment.
- "Discernment" = `player.Stats.DiscernmentInvestment` (gear discernment arrives with the gear system).

## Component A — CombatConfig
1. New `src/ROTA.Application/Configuration/CombatConfig.cs` with the six properties above (defaults as
   shown). Add a `"CombatConfig"` section to `src/ROTA.Api/appsettings.json` with those defaults.
2. Register in `Program.cs`: `builder.Services.Configure<CombatConfig>(builder.Configuration.GetSection("CombatConfig"));`
3. Commit: `feat(combat): add tunable CombatConfig (crit chance/damage curves)`.

## Component B — Crit profile on StatService
1. Add to `IStatService`: `CritProfile GetCritProfile(int discernment);` where `CritProfile` is a small
   record/struct `(double Chance, double Multiplier)` in `ROTA.Shared` (or Application).
2. `StatService` injects `IOptions<CombatConfig>` and implements the two clamped formulas above.
   `Chance` is clamped to `[0, BaseCritChance + MaxCritChanceBonus]`; `Multiplier` to
   `[BaseCritMultiplier, BaseCritMultiplier + MaxCritDamageBonus]`.
3. Tests (`StatServiceTests`): discernment 0 → (0.05, 1.5); 1000 → (0.15, …); 100000 → still (0.15, 2.5)
   (proves the hard caps); a mid value; never exceeds caps.
4. Commit: `feat(combat): discernment crit profile (chance/damage, capped)`.

## Component C — Apply crit in raid damage + expose on the hit response
1. `RaidService` — inside `AtomicApplyHitAsync`, right after the base damage is computed
   (`damageFinal = Math.Max(1, (long)(baseValue * hitSize * multiplier));`) and BEFORE
   `lockedRaid.TakeDamage(...)`:
   - `var crit = _stats.GetCritProfile(player.Stats!.DiscernmentInvestment);`
   - `bool isCrit = _random.NextDouble() < crit.Chance;`
   - `if (isCrit) damageFinal = Math.Max(1, (long)(damageFinal * crit.Multiplier));`
   - Capture `isCrit` and the applied multiplier in outer variables for the response.
2. DTO `RaidHitResponse` (`RaidDTOs.cs`): ADD `public bool IsCrit { get; set; }` and
   `public double CritMultiplier { get; set; }` (1.0 when not a crit). Populate at the response build
   site. The idempotency cache stores these (replays return the same).
3. Audit line: include crit in the existing RaidHit audit summary (e.g., append ` CRIT x{mult}` when crit).
4. Tests (`RaidServiceTests`): with a seeded `Random` forcing a crit, damage is multiplied by the
   profile multiplier and `IsCrit=true`; forcing no crit leaves base damage and `IsCrit=false`; a
   high-discernment player's crit damage uses the capped multiplier.
5. Commit: `feat(raids): apply discernment crit to hit damage + expose on response`.

## FINAL — verify & hand back (no push, no tag)
- `dotnet build` 0 warnings + `dotnet test` all green (baseline 207 + new tests).
- Report files changed per component, `git log --oneline`, and any deviation. The supervisor runs the
  `dotnet run` smoke test + traces the crit math.

## Tool rules (pre-staged)
You are already on branch `feat/v0.2.3-discernment-crit`; Docker is running. **Bash tool only**;
allowed: `dotnet build/test/ef`, `git add/commit`. No `git checkout`/`branch`/`rm`/`docker`/`dotnet
run`/PowerShell. Files via Write/Edit. Don't touch `.claude/`/transcripts/settings. STOP+report on any
block. Per component: build (0 warnings) → test green → one commit, trailer
`Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>`. No push, no tag.

## OUT OF SCOPE
Quest crit/drop-quality · gear (Legion/equipment is the next system) · reward-atomicity · per-raid
xpPerStamina · endpoint/field removal · push/tag.
