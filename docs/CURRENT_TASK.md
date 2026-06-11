# ROTA — Current Task

*Updated 2026-06-11. Short by design — answers "what now?" so a fresh session bootstraps cheaply.*

> **Canonical source: [SESSION_HANDOFF.md](SESSION_HANDOFF.md) (top section is always the freshest).**
> This file is a pointer + snapshot; when the two disagree, the handoff wins.

## Snapshot (2026-06-11)
- **All committed + pushed.** The 3.5 waves (Wave 2 T65–T70 · UX T72–T75 · T76 Gauntlet foundation
  + S2 page slice) are backend commit `4bd9850`; the **lore Master Canon** merge is `39d21d4`; both
  **pushed to `origin/main`**. Client mirror is `d7f9609` (local — no remote). 956 unit + 111
  integration green; client compiles clean. Migrations applied to dev DB.
- **Live playtest done → 5 new tickets** in
  **[PLAYTEST_TICKETS_2026-06-11.md](PLAYTEST_TICKETS_2026-06-11.md)** — the active backlog.
- **Xolaces dev-flag: KEPT** (owner-confirmed 2026-06-11) — Developer flag + Dev guild membership
  are intentional; CLAUDE.md T43 note updated.
- **Live server** may still be up on `http://localhost:5035` with an Active Neck event seeded.

## What now (priority order)
1. **Playtest tickets** → [PLAYTEST_TICKETS_2026-06-11.md](PLAYTEST_TICKETS_2026-06-11.md),
   recommended order **T1 → T4 → T3 → T5 → T2**: T1 difficulty-gate (client selectability; server
   OK), T4 energy/stamina delta + HUD↔profile SSOT (client; backend OK), T3 raid-loot enforcement,
   T5 Gauntlet overhaul epic (extends system-24), T2 raid-summon remodel (Fable UI pass).
2. **Lore → game asset items** (queued; playtest-before-lore): wire `docs/Lore/ROTA_Master_Canon.md`
   into `content/items.json` + `gear.json` + Home lore; overlaps T2/T5 art/lore slots.
3. **Owner housekeeping:** replace placeholder legal text; first `tools/build-client.ps1` run; add a
   git remote for the client to push its work.
4. **Then:** T77 content wave · T76 remaining (rank-gear/banner) · pre-beta hardening.
