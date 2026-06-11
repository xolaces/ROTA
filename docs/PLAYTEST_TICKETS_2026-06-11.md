# ROTA — Playtest Tickets (2026-06-11)

Source: owner live-server playtest of the just-deployed waves (backend `4bd9850`, client
`d7f9609`). Five tickets, owner-indexed `TICKET-N-061126`. Analysis + pointers added by the
sort/triage pass. Priority is a recommendation — re-order freely.

> **STATUS 2026-06-11 (fix session): T1 ✅ · T4 ✅ · T3 ✅ — built + green (957 unit + 111
> integration; client Runtime compile 0 errors), UNCOMMITTED.** T5: chat removal + crit
> verification done; economy/battalion decisions LOCKED by owner (system-24 spec §0b D6–D9);
> battalion backend + UI overhaul remain. T2: not started (clusters with T5.5 Fable UI pass).

> The new chat picks these up from here. Each ticket lists the affected layer (backend/client),
> a root-cause hypothesis grounded in the code, file pointers, scope, and acceptance criteria.

## Sorted backlog (recommended order)

| # | Ticket | Type | Layer | Effort | Priority | One-line |
|---|--------|------|-------|--------|----------|----------|
| 1 | T1 — Campaign difficulty gate not enforced | Bug | **Client** (server OK) | S | **P1** | Locked difficulties are still *selectable* in the picker |
| 2 | T4 — Energy/Stamina delta + HUD↔profile mismatch | Bug | **Client** (backend OK) | M | **P1** | Current pool not bumped on spend; HUD ≠ profile |
| 3 | T3 — Raid loot enforcement + list removal | Bug | Both | M | **P1** | Loot only from completed-raids; remove from all indexes on claim |
| 4 | T5 — Gauntlet full UI overhaul + system clarification | Epic | Both | XL | **P2** | Remove per-stage chat, battalion power, clean stage UI, shop/leaderboard |
| 5 | T2 — Raid summon screen remodel | Enhancement | Client | L | **P3** | Polished remodel; slot-ready for lore + AI art |

**Clustering note:** T2 + T5 are UI-modernization passes the owner wants done with the **Fable
model** (use the visualize/mockup tooling for design), built to accept lore text + AI-generated
art later — so they naturally cluster with the **lore→items** phase (still queued). T1/T4/T3 are
correctness bugs and should land first.

---

## TICKET-1-061126 — Campaign Difficulty Gate Not Enforced  **[P1 · Bug · Client]** ✅ FIXED 2026-06-11

> **Fix:** the global difficulty dropdown in QuestScreen only OFFERS tiers unlocked on at least
> one node (`BuildDifficultyChoices`, rebuilt on every quest load) — locked tiers cannot be
> selected at all; a 🔒 hint names the next tier and how to unlock it. Per-node T74 lock hints
> kept for nodes lagging behind. Mock auto-covered (it already serves HighestUnlockedDifficulty
> + gates attempts). Server gate confirmed authoritative (unit-tested DifficultyLocked path).

**Owner:** Players can select higher campaign (quest) difficulties without completing the prior
tier. Lock + make unselectable until the preceding difficulty is completed. Enforce on **both**
client display and server validation so it can't be bypassed.

**Analysis — server is already authoritative; the gap is the client picker:**
- **Server (looks solid, verify no hole):** [QuestService.cs](src/ROTA.Application/Services/QuestService.cs)
  `AttemptQuestAsync` lines ~264-267 — "Verify difficulty gate (Hard requires Normal, etc.)" reads
  `DifficultyGates[difficulty]` and returns `QuestFailureCode.DifficultyLocked = 6`
  ([QuestDTOs.cs:93](src/ROTA.Shared/DTOs/QuestDTOs.cs)). Comment at line 152: *"the attempt-time
  gate stays authoritative."* So a locked-tier attempt is rejected server-side today.
- **Client (the real bug):** T74 added `QuestAvailabilityResponse.HighestUnlockedDifficulty`
  (QuestService.cs:217) so the client can render locked tiers unselectable, and the QuestScreen was
  supposed to show "🔒 <tier> — Clear <prev> first" instead of an attempt button. Playtest shows the
  **difficulty picker still offers locked tiers as selectable**, so either the picker ignores
  `HighestUnlockedDifficulty`, or only the attempt *button* was gated while the *selector* wasn't.

**Scope:**
1. Client: in the QuestScreen difficulty selector, drive selectability from
   `HighestUnlockedDifficulty` — locked tiers are visually locked AND not selectable (not click→409).
2. Verify (live, not mock) the server rejects a locked-tier attempt with `DifficultyLocked` — confirm
   no path skips the gate (e.g., boss nodes, the new zone-boss path).
3. MockRotaApi: ensure the mock also gates the picker (owner may retest in mock).

**Acceptance:** A locked difficulty cannot be selected in the UI; if forced, the server returns
`DifficultyLocked`. Verified live + mock.

**Refs:** owner-standard "locked options must be UNSELECTABLE, not click-then-409" (memory
`owner-ui-standards`); System 8 difficulty gates; T74.

---

## TICKET-4-061126 — Energy/Stamina Delta on Stat Spend + HUD↔Profile Mismatch  **[P1 · Bug · Client]** ✅ FIXED 2026-06-11

> **Fix:** displayed resource values now have ONE source — `PlayerState.GetLiveResource`
> (anchored extrapolation; re-anchors only when authoritative data arrives via Set/PatchResource).
> HeaderBar, ProfileScreen health row, and RaidCombatView hit gating all read it, so they cannot
> disagree. Root cause found: `PatchResource` used to re-snapshot ALL pools from stale profile
> data on every raid hit, silently resetting regen timers — that was the drift. Mock allocate
> already credited the delta (prior wave); backend T30 credit confirmed present.

**Owner:** Spending SP into Energy/Stamina doesn't raise the **current** pool by the delta (T30
regression), AND the top-left HUD resource values still don't match the profile screen. Resolve
together: confirm the delta hits current immediately on spend, and confirm one source of truth feeds
both HUD and profile.

**Analysis — backend T30 credit is present; this is a client single-source-of-truth bug:**
- **Backend (correct, verify live):** [StatService.cs](src/ROTA.Application/Services/StatService.cs)
  `AllocateStatPointAsync` lines ~86-99 — after raising the max it calls
  `RefillEnergyAsync(playerId, ResourceType.Energy/Stamina, amount, ct)`, crediting +amount (the
  delta; MaxEnergy = 25 + EnergyInvestment is 1:1) to **current**. So the live backend already does
  what T30 specified. (Health mirrors it, T56.)
- **If the owner retested in MOCK:** the known gap is `MockRotaApi.AllocateStatAsync` omitting the
  LiveValue bump (handoff §A bug #1) — make the mock mirror the live credit.
- **HUD↔profile mismatch (the live bug):** HeaderBar advances displayed values from the server's
  regen rate per-second and reconciles on fetch (T29); the profile reads a fresh `GetProfileAsync`.
  If they read different fields, or the HUD doesn't re-fetch after an allocate, they drift. Trace:
  after `AllocateStatAsync` returns, does the client push the new current/max into the **same**
  PlayerState the HUD ticker and the profile both read? They must reconcile off one authoritative
  fetch, not two independent reads.

**Scope:**
1. Client: make HUD + profile read one `PlayerState` resource snapshot; after an allocate (and any
   spend), refresh that snapshot so both update simultaneously.
2. Mock: credit the current-pool delta in `MockRotaApi.AllocateStatAsync` (live-fidelity).
3. Verify live: spend 1 SP into Energy → current rises by 1 immediately, HUD == profile.

**Acceptance:** Current pool rises by the spent delta instantly (live + mock); HUD and profile show
identical resource values at all times.

**Refs:** T30, T56; memory `mock-fidelity-playtest`; handoff "OPEN PLAYTEST BUGS" §A.

---

## TICKET-3-061126 — Raid Loot Enforcement and List Removal  **[P1 · Bug · Both]** ✅ FIXED 2026-06-11

> **Fix (backend):** `GetRaidByIdAsync` now returns null for the summoner once they've claimed
> (RewardsClaimed latch) — a claimed raid is unreachable by id too (+1 regression test). List
> paths audited: active list (Active-only + RewardedAt==null lootables), guild list (!IsDefeated),
> Gauntlet ladder (own screen by design) — all correct.
> **Fix (client):** Loot lives ONLY in Raids → Completed (new unclaimed-loot section with claim
> button; card removed immediately on claim, spoils shown in status). RaidCombatView's Loot
> button/path REMOVED — the kill log points to the Completed tab; share panel hides once defeated.
> Public tab no longer lists Lootable raids. Mock mirrors all of it (lootable seed raid, Looted
> raids vanish from list + 404 by id).

**Owner:** Raids may only be looted from the completed-raids menu — nowhere else. On claim, the raid
must be **immediately removed** from the completed-raids list and all other indexes; no longer
visible or accessible in any form.

**Analysis — lifecycle exists (System 23 + T57); two gaps to close:**
- **Lootable surface:** `IActiveRaidRepository.GetLootableUnclaimedForPlayerAsync` +
  `GetActiveRaidsAsync` surface unclaimed lootable raids; per-participant `LootRaidAsync` grants
  pending rewards and latches `RewardedAt` (T57). System 23 `Loot()` flips lifecycle
  Lootable→Looted but **does NOT soft-delete** (keeps FK/history) — so a Looted raid can linger in
  indexes if any query doesn't filter `LifecycleState==Looted` / `RewardedAt != null`.
- **Gap A (enforcement):** confirm the Loot action is reachable ONLY from the completed-raids menu.
  T57 moved the Loot button to "any participant," but the owner wants looting blocked from every
  other screen/state (e.g., the combat view, public list). Audit every Loot entry point.
- **Gap B (removal):** after claim, ensure the raid drops out of the completed list AND every other
  index (active-raids list, lootable-unclaimed query, join-by-id). Likely a missing
  `RewardedAt`/`Looted` filter in one or more reads.

**Scope:**
1. Backend: audit all raid list/lookup queries for a consistent post-loot filter
   (`RewardedAt != null` for the caller, and/or `LifecycleState==Looted`) so a claimed raid vanishes
   from every index. Confirm `LootRaidAsync` is the only claim path.
2. Client: Loot action only on the completed-raids menu; remove/disable it everywhere else
   (RaidCombatView, public list). On claim, remove the card immediately.
3. Verify live: claim loot → raid gone from completed list + not reachable by id/other tabs.

**Acceptance:** Loot is claimable only from the completed-raids menu; after claim the raid is absent
from all lists/indexes and not accessible by any route.

**Refs:** System 23 (raid visibility/lifecycle), T57 (per-participant deferred loot), System 19.

---

## TICKET-5-061126 — Gauntlet Full UI Overhaul + System Clarification  **[P2 · Epic · Both]** ◐ PARTIAL 2026-06-11

> **Done:** (1) per-stage chat removed — combat view shows no chat on Personal (solo) raids and
> never joins the raid group (covers Gauntlet stages + solo sigil raids; hub already
> participant-gated, no server change). (4) crits VERIFIED — the crit roll in
> RaidService.HitRaidAsync sits outside every GauntletEventId gate, so strikes already crit.
> **Decisions LOCKED by owner (now in system-24 spec §0b):** D6 single STRIKE action @ exactly
> 1 ticket (hit sizes dropped in Gauntlet); D7 "ticket" = renamed Strike (same ledger, UI-only
> rename); D8 dedicated Gauntlet BATTALION loadout (any unit/general, any race) drives hit power.
> **Remaining:** battalion backend (entity/migration/endpoints/power formula/hit-fork) + the
> single-strike API change + ticket rename in UI + the full Fable UI pass (mockups first).

**Owner (system, now locked):** Complete Gauntlet UI overhaul. **Remove the per-stage chat** — not
needed. The Gauntlet is a **staged solo-progression** event: each player runs their own stage
sequence, stages **auto-advance** and are visible only to that player. Placement = **highest stage
cleared** (if A and B are both on 170, whoever clears 171 first ranks higher until the other
catches up). Leaderboard = **4 tiers by player level range**, ranked by highest stage defeated.
**Each strike costs exactly 1 ticket.** **Crits can occur on strikes.** **Battalion power** governs
hit strength, built from a **free-form legion** — any unit/general of any race, no race restrictions,
following a base general + troop structure. UI must have: a clearly accessible **leaderboard
button**, a **shop button** (buy tickets + battalion upgrades), and a clean **stage display**
(current stage, battalion power, strike action). Organized, uncluttered. Stage **250 = competitive
late-game ceiling**; tiers reflect natural level distribution to that range.

**Analysis — much of the *system* already matches; the work is UI + a few backend reconciliations.**
Extends [system-24 spec](docs/specs/active/system-24-gauntlet-event-experience.md) — fold this in.

*Already true (confirm, don't rebuild):* solo auto-advance ladder of Personal raids (System 16/24);
highest-stage ranking (T76 `GauntletEntry.HighestStage`); 4 brackets by level
(Whelpling 1–999 / Wyrm 1000–2499 / Dragon 2500–4999 / Ancient 5000+, T76); stage-250 ceiling
(`GauntletConfig.MaxLadderStage=250`).

*Needs work — sub-tasks:*
1. **Remove per-stage chat (quick, client + maybe backend):** the ladder reuses `RaidCombatView`,
   which has raid chat (JoinRaid/SendRaidMessage). Strip chat from the Gauntlet combat path; don't
   join the raid chat group for `GauntletEventId` stages.
2. **Battalion power (backend, the big one — promotes the PHASE-2 placeholder):** "Gauntlet Legion
   Power" is currently an inert placeholder (T54). Build battalion-power computation from a free-form
   legion (any unit/general, any race, base general+troop structure) and feed it into the Gauntlet
   hit formula (RaidService gauntlet fork on `GauntletEventId`). Reuse System 15 legion/unit defs;
   drop race restrictions for the Gauntlet battalion.
3. **Strike/ticket economy reconciliation (backend + design):** owner says **1 strike = 1 ticket**.
   Current config: `StrikeRatePerSize {Small:1,Medium:5,Large:20}` + gems→strikes (`StrikeGemPrice=1`).
   Reconcile "ticket" terminology vs Strikes/Tokens/Pitchforks and make each strike cost exactly 1
   ticket; shop sells tickets + battalion upgrades. **Surface to owner before coding** (economy change).
4. **Crits on strikes (backend, verify):** confirm the Gauntlet hit fork applies the crit roll
   (CombatConfig). If suppressed in the gauntlet path, enable it.
5. **UI overhaul (client, Fable design pass):** rebuild GauntletScreen stage view — clean stage
   display (current stage · battalion power · strike action), prominent **Leaderboard** button,
   **Shop** button (tickets + battalion upgrades). Uncluttered. Built to accept lore + AI art.

**Acceptance:** No per-stage chat; battalion power drives strikes (free-form legion, any race);
1 strike = 1 ticket; crits roll on strikes; UI exposes leaderboard + shop + clean stage display;
ranking by highest stage across 4 level tiers to stage 250.

**Refs:** system-24 spec (extend it), System 16 (economy/combat fork), System 15 (legion),
memory `t76-gauntlet-foundation`, `raid-summon-economy`.

---

## TICKET-2-061126 — Raid Summon Screen Remodel  **[P3 · Enhancement · Client]**

**Owner:** Full visual overhaul of the raid summon screen. Use the **Fable model** as part of the
broader UI-modernization effort. Lay groundwork for lore text + AI-generated imagery to be slotted
in cleanly later. Should feel intentional and polished, not a placeholder — designed so lore and
raid artwork drop in without structural rework.

**Analysis:** Pure client (Unity/UI Toolkit) presentation work on the raid summon screen
(RaidScreen / summon flow). No backend change. Design with explicit, named slots for: raid art
(banner/portrait), lore blurb, and the existing summon controls (boss, difficulty, size/sigil cost).
Mirrors the Gauntlet event-page treatment (System 24) — reuse that visual language for consistency.

**Scope:**
1. Fable design pass (use the visualize/mockup tooling) for the new layout with reserved art + lore
   slots.
2. Rebuild the raid summon screen to that layout; keep summon intent/flow intact (server unchanged).
3. Leave clearly-labeled placeholders for art + lore so the content phase fills them in.

**Acceptance:** Polished, intentional raid summon screen with clean, reserved slots for lore text +
raid art that can be populated later without restructuring.

**Refs:** memory `owner-ui-standards` (events get full-page treatment), `unity-ia-nav-overhaul`,
LORE_HANDOFF / Master Canon (raid bosses: Iron Colossus, Sunken Leviathan, Kronarch, etc.).

---

## Cross-cutting notes for the new chat
- **Server is authoritative** for T1 (gate) and T4 (delta) — both backends look correct; the bugs are
  client-side. Don't "fix" the backend without first reproducing live.
- **Mocks must stay stateful** (memory `mock-fidelity-playtest`) — several of these reproduce in mock
  only because the mock under-models the live path. Fix mock fidelity alongside the client fix.
- **UI standards** (memory `owner-ui-standards`): no grey/unreadable buttons; locked options
  unselectable; marquee features get full-page treatment.
- **Lore→items** remains queued (post-playtest) and overlaps T2/T5's art/lore slots.
