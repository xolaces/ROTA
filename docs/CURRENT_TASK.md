# ROTA — Current Task

*Updated 2026-05-31 (System 15 Legion epic queued). Short by design — answers "what now?" so a fresh session bootstraps cheaply.*

## Just completed
- **v0.2.0–v0.2.4** (beta access → character gear) · **v0.2.5** conditional/stacking bonuses ·
  **v0.2.5.1** hardening · **v0.2.6** System 14 Raid Magic (6 slices, s1–s6).
  All merged, tagged, pushed to origin.
- Current: 266 unit + 8 integration = 274 tests green, 0 warnings. `main` @ v0.2.6-s6 synced with origin.

## v0.2.6 summary (System 14 — Raid Magic, just shipped)
Shared raid-scoped proc magics (reuse the v0.2.5 `ConditionalBonusEvaluator`). Per-size slots 1/2/3/4/5;
**World raids Admin-only** (anti-grief); one-per-player (race closed under advisory lock); permanent
ownership. DamageProc + utility (crit/gold/xp) effects fold into `HitRaidAsync` with an aggregate cap.
Economy: loot drops + gem shop. **Audited — one money bug pending fix (see precursor below).**

## PENDING precursor: v0.2.6.1 magic fixes (small — do first)
Close the System 14 audit's money bug before Legion: `MagicService.BuyMagicAsync` (1) charges gems for a
magic you already own (no ownership pre-check → currency for nothing), and (2) passes `referenceId=null`
so a retry double-charges. Fix: add `BuyMagicFailureCode.AlreadyOwned` + ownership pre-check (reject w/o
charging) + idempotent `referenceId="magicbuy:{playerId}:{magicId}"`; map AlreadyOwned→409. Also minor:
`MagicProcs` (raw) vs `MagicProcBonus` (capped) display. Branch `v0.2.6.1-magic-fixes`. Tests incl.
buy-twice-charges-once.

## NEXT (large epic): System 15 — Legion
Full spec + sliced task queue: **`docs/specs/system-15-legion.md`**. Build the 6 slices IN ORDER —
one branch + build/test green + merge per slice, committed independently (never bundled). Auditor reviews
after a batch. Decisions LOCKED with owner (grounded in DotD wiki, saved in `docs/research/dotd-wiki`):
- **Legion power = a SEPARATE additive damage term** (DotD coefficients: General 2.0×ATK+0.4×DEF, Troop
  1.44×/0.36×); crit multiplies the combined total. Unit/legion abilities reuse the proc pipeline.
- **Primary but tuned below Dawn's ~90%** via `LegionConfig.PowerScaling`; mount procs stay significant
  (they scale off the combined base). Generals+Troops at launch (Armaments config-ready for later).
  Race/Role/Attribute slot-typing engine now; starter legions mostly-open + a few specialists.
- Slices: (1) content → (2) ownership → (3) assembly+power → (4) combat integration **[DEEP]** →
  (5) commander slot → (6) economy. Data model, damage formula, per-slice acceptance criteria in the spec.

## Then (in order)
- **Gauntlet epic** (competitive leaderboard event; tightly coupled to Legion). 3 level-leagues, top-500
  prizes, currency = Gauntlet Tokens → shop. LOCKED content from DotD: **SMITE → "Wrath of the Ancients"**
  (rank 1: 24%→500% dmg) and **Blessing of Mathala → "Blessing of the Ancients"** (ranks 2–10: 13%→850%),
  near-identical mechanics, acquired by Gauntlet rank; Gauntlet Trophies (rank 1/10/500) passively boost
  ALL legion power +25/10/5%. Spec when we reach it.
- **v0.3.0 — C# API client SDK** (HTTP + DTOs) = the Unity client's layer. Owner wires Unity scenes.

## Deferred / back-burnered
Discernment quest-drop-quality · moderation (no chat/mods/players) · per-raid xpPerStamina ·
gear set bonuses · N+1 in `DistributeKillRewardsAsync` (per-participant queries inside the lock on
kill — invisible at current raid sizes, batch only if raids get crowded) · content depth
(2 raids / 5 quests is thin — needs authoring alongside systems).

## Balance values owner has signed off / flagged (tunable in appsettings)
- Regen: class-based, Conscript 5 min/point energy+stamina (accepted — level-up RefillResource +
  future consumables offset it). · Crit: 5%→15% chance (+10% hard cap @1000 disc), 1.5×→2.5× dmg
  (@5000 disc). `CombatConfig` / `ClassConfig`.

## Hard rule (learned)
Build-green ≠ correct. After any agent build: run the app, trace behavior-touching changes (combat/
migrations/concurrency/money), smoke-test the CLI. Light-check only low-risk batches (docs/CI/additive).
