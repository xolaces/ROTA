# ROTA — Spec Index

Per-system build specs, organized by **status**. A spec is the locked design + sliced task
queue a build agent works against; it stays as the historical decision record after shipping.

- **`shipped/`** — built, merged, and tagged. Kept as the "why we did it this way" record.
- **`active/`** — decision-complete and currently buildable (or mid-build).
- **`backlog/`** — drafted but deliberately deferred; not being built yet.

> ⚠️ **"System 13" naming collision (historical).** The number 13 was reused three times before
> the kebab-case `system-NN-*` convention settled. Filenames are preserved as-written (so the
> changelog/journal still line up); use the table below for the real mapping. Don't renumber.

## Shipped (`shipped/`)

| File | System / Version | Purpose |
|---|---|---|
| `SYSTEM_12_BETA_ACCESS_CONTROL.md` | System 12 | Beta keys, `PlayerRoles` (Player/Mod/Admin), JWT role claims, admin tooling/CLI, seed-admin. |
| `BACKEND_HARDENING_PRE_UNITY.md` | Hardening pass | Pre-Unity stabilization — bug/robustness fixes, **no API-contract changes**. |
| `V0_2_2_PRE_UI.md` | v0.2.2 | Class-based regen, raid-size set (1/5/20), raid on-hit rewards. |
| `V0_2_3_DISCERNMENT_CRIT.md` | v0.2.3 | Discernment → raid critical-damage bonus. |
| `system13_character_gear.md` | "System 13" / **v0.2.4** | 8-slot equipment, effective ATK/DEF (base+gear), mount-slot proc. |
| `system-13-stacking-bonuses.md` | "System 13" / **v0.2.5** | Conditional/stacking gear bonuses (`ConditionalBonusEvaluator`), reward atomicity. |
| `system-14-raid-magic.md` | System 14 / v0.2.6 | Raid magic (Wrath/Blessing precursor); per-event consumable auras + cap. |
| `system-15-legion.md` | System 15 / v0.2.7 | Units + legions, commander slot, legion power as a separate damage term. |
| `system-17-leaderboards.md` | System 17 / **v0.2.8** | Global leaderboards — 6 boards, aggregate table, eligibility-in-SQL, write hooks, stat snapshot. (5 slices, complete.) |
| `system-19-raid-sharing.md` | System 19 | Raids private until shared — `ActiveRaid.IsPublic` + `Share()`, `GET /api/raids/{id}` (join-by-UID), `POST /api/raids/{id}/share` (summoner-only), list = public + own; sigils summon `Small`. Client share panel + join-by-UID. (3 slices, complete.) |

## Active (`active/`)

| File | System | Status | Purpose |
|---|---|---|---|
| `system-16-gauntlet.md` | System 16 | **Decision-complete — ready to build (6 slices)** | Competitive Gauntlet event: leagues, Strikes ledger, Wrath/Blessing off-cap auras, trophies, two-currency token shop. Slice 4 = deep combat/money. |

## Backlog (`backlog/`)

| File | System | Status | Purpose |
|---|---|---|---|
| `SYSTEM_13_MODERATION.md` | Moderation (drafted under "13") | **Deferred** | Moderator powers — world-chat mute, temp bans ≤3 days, reason-required, punishment logging. Not built (no chat/players yet). |

## Naming convention going forward

New specs: **`system-NN-short-name.md`** (kebab-case, next free integer N), drop into `active/`
while building, `git mv` to `shipped/` once tagged. The numeric collision above is frozen history.
