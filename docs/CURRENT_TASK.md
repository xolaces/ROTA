# ROTA — Current Task

*Updated 2026-06-12. Short by design — answers "what now?" so a fresh session bootstraps cheaply.*

> **Canonical source: [SESSION_HANDOFF.md](SESSION_HANDOFF.md) (top section is always the freshest).**
> This file is a pointer + snapshot; when the two disagree, the handoff wins.

## Snapshot (2026-06-12)
- Quest/UI batches COMMITTED: backend **`ba3dc62`** (NOT pushed — classifier blocked direct-to-`main`;
  owner runs `git push origin main`), client **`bc290c5`**. 964 unit + 111 integration green.
- **UNCOMMITTED (client, on `bc290c5`; awaiting owner Unity playtest → commit):**
  1. **ProfileScreen rebuilt on the template** (Gauntlet-mirror: hero card + EQUIPPED slot-tile rail
     + 🎒Bag/🏅Awards; alloc/bag-detail/equip/achievements ALL OverlayPanel pop-outs; portrait wired
     to `Resources/UI/ClassIcons/<Class>` w/ glyph fallback). Scratch-compile 0 errors.
  2. **Obsidian Gilt shell slice 1** (the CHOSEN modern skin): `AppShell` root → ROW `[icon rail |
     header+content]`; `BottomNav` vertical rail + 👑 crest (pure-USS orientation, same 6 tabs);
     `Theme.uss` v2 (rail, slim header, refined bars, rounding pass). HeaderBar UNCHANGED. 3-lens
     adversarial review CLEAN (one "login breaks" finding = verified false positive).
  3. **Header/bars/rail polish (2026-06-13):** GOLD + GEMS plates (gem balance wired end-to-end —
     **also NEW uncommitted BACKEND**: `PlayerProfileResponse.Gems` ← gem ledger); bars recolored
     (energy green · stamina yellow · guild purple · HP red); rail reordered Home·Profile·Quest·
     Raids·Guild·Legion + ⚙ Options foot tile. Backend 964 unit green; client compiles.
- **The Gauntlet page is the app UI TEMPLATE**; **Obsidian Gilt** is the chosen skin (Gilded-Codex
  palette + left rail + rounder gold-glass). Owner rules: memory `owner-ui-standards`.
- Live server may be on :5035 with the committed backend; stop before rebuilding.

## What now (priority order)
0. **Backend:** commit the new gem-wiring (PlayerDTOs/PlayerService/PlayerServiceTests) + `git push
   origin main` (ba3dc62 + the new commit).
1. **Owner Unity playtest** the profile rework + Obsidian Gilt shell → commit the client work.
2. **Obsidian Gilt slice 2** — Quest/Raid/Gauntlet detail surfaces adopt the rounded template.
3. **Class-specific icon FILES** (owner has them, NOT ready — drop into `Resources/UI/ClassIcons/
   <ClassName>.png`, zero code) — portrait + rail already wired.
4. **T5 battalion backend slice** (spec system-24 §0b D8 — formula LOCKED, client already matches).
5. **Lore → items** (queued): fill the art/lore slots reserved across the new screens.
