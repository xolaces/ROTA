# ROTA — Current Task

*Updated 2026-06-12. Short by design — answers "what now?" so a fresh session bootstraps cheaply.*

> **Canonical source: [SESSION_HANDOFF.md](SESSION_HANDOFF.md) (top section is always the freshest).**
> This file is a pointer + snapshot; when the two disagree, the handoff wins.

## Snapshot (2026-06-12)
- Playtest tickets T1/T4/T3 fixed + COMMITTED (backend `ecf9277` pushed · client `6dbb9c5` local).
- **THREE further batches built + verified but UNCOMMITTED** (964 unit + 111 integration green;
  client compile clean): the UI-template restructure (Gauntlet v2 + pop-outs + battalion editor +
  summon remodel), the quest-rules batch (zone-scoped difficulty unlock · zone-final-boss sigils ·
  Pano chase curve · victory/attempt pop-outs · mock fidelity), and the battalion-power formula
  (6 generals + 20 troops, stat-inherent power). File list: handoff "UNCOMMITTED WORK".
- **The Gauntlet page is the app UI TEMPLATE** (Theme.uss `.card/.kicker/.chip/.btn-cta/.art-slot/
  .slot-tile/.overlay-*` + `RotaClient.UI.OverlayPanel`). Owner rules in memory `owner-ui-standards`
  (pop-outs not expansions; rewards = ONE fixed replace-only slot; action buttons never move).
- Live server likely running on :5035 with the uncommitted backend; stop before rebuilding.

## What now (priority order)
0. **Owner review → commit** the uncommitted work in both repos.
1. **PROFILE screen UI overhaul** (owner-declared next; template rollout #2).
2. **Class-specific icons** in the HeaderBar portrait (owner has the generated icon files — ask
   where they are) + a stylistic resource-bar restyle (keep the T4 GetLiveResource data flow).
3. **T5 battalion backend slice** (spec system-24 §0b D8 — formula LOCKED, client already matches).
4. **Lore → items** (queued): fill the art/lore slots reserved across the new screens.
