# ROTA — Current Task

*Updated 2026-06-10. Short by design — answers "what now?" so a fresh session bootstraps cheaply.*

> **Canonical source: [SESSION_HANDOFF.md](SESSION_HANDOFF.md) (top section is always the freshest).**
> This file is a pointer + snapshot; when the two disagree, the handoff wins.

## Snapshot (2026-06-10)
- **Built this cycle (ALL UNCOMMITTED, owner reviews → commits):** Wave 2 public-beta blockers
  **T65–T70** (password reset · deploy artifacts · CI migration gate · terms/privacy ·
  tutorial · Windows build script), UX wave **T72–T75** (no-grey-buttons theme fix ·
  inline quest rewards/optional popups · locked difficulties unselectable · dev tools expansion
  incl. [AdminOnly] DevController), and the **T76 Gauntlet event-experience foundation**
  (4 level brackets incl. Ancient 5000+ · highest-stage-completed ranking · late-ladder HP ramp ·
  Neck/Ring event kinds + run identity + countdown header).
- **Green:** 956 unit + 111 integration = **1067**. Client (C:\Dev\ROTA.Client6) compiles clean.
- **Session 2 (same day):** T76's non-blocked slices shipped — prize preview endpoint+table,
  per-player settlement summary ("you placed #N"), Coming-Soon gate + three-state Home CTA,
  stateful mock event phases + dev-tab toggle. Spec §6 updated.
- **Migrations applied to dev DB:** through `AddGauntletEventIdentity` (incl.
  `AddPasswordResetTokens`, `AddTermsAcceptance`). None pending.
- Last commits: backend `bde33f5`, client `e0bdbfe`, docs `7d52270`.

## What now (priority order)
1. **Owner:** review + commit both repos; replace placeholder legal text
   (src/ROTA.Api/content/legal/); first real `tools/build-client.ps1` run.
2. **T76 next slices** (spec: docs/specs/active/system-24-gauntlet-event-experience.md §6):
   rank-GEAR seasonal grant/removal (needs T77 gear content), token-bonus parity at settle,
   banner art slot. (Settlement screen, CTA states, prize table: DONE session 2.)
3. **T77 content wave** (owner-led, lore-gated): Ch4–6 loot tables, raid pool 2→6-8,
   pinnacle magics, Pano set bonus, Gauntlet run names.
4. **Pre-beta hardening still open:** BanGateMiddleware HTTP-pipeline test; global soft-delete
   filter sweep; client TokenStore stores tokens as PLAINTEXT JSON (encrypt-at-rest before
   public beta); client magic-shop catalogue endpoint (BETA-PLACEHOLDER — shop shows owned-only).
