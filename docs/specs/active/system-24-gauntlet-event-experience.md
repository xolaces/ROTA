# System 24 — Gauntlet Event Experience (DotD parity) — IN PROGRESS (T76)

**Status:** S1 FOUNDATION BUILT 2026-06-10 (see §6). Plan originally written same day at owner
request. Source of truth for DotD mechanics: `docs/research/dotd-wiki/_clean/Gauntlet.txt`.
Builds ON TOP of System 16.

## 0. OWNER DECISIONS — LOCKED 2026-06-10
- **D1 — Combat shape:** the shipped SOLO AUTO-SUMMON LADDER **is** the DotD shape (owner
  confirmed: "ran through tiers, competed on a ladder of the highest tier completed,
  auto-summoned in the gauntlet page"). NO shared raids. Changes locked instead:
  (a) **brackets become DotD level brackets + a new top one: 1–999 / 1000–2499 / 2500–4999 /
  5000+ (Ancient)** — late-game power gaps are huge; (b) **rank by HIGHEST STAGE COMPLETED**
  (damage + earliest-to-reach as tiebreaks); (c) **late-ladder brutality ramp** — DotD got hard
  ~180 for low levels, brutal ~230, near-DOUBLING HP per stage by ~250 → config-gated ramp:
  growth interpolates from base 1.0493 at stage 200 to ×2.0 at stage 250.
- **D2 — Cadence:** 5–7 days, ~monthly, manually opened (CLI/admin). Countdown from config dates.
- **D3 — TWO EVENT FAMILIES:** the **Neck Gauntlet** (standard run: neck-slot rank gear + the
  rank MAGICS) and the **Ring Gauntlet** (rarer, ~every 3rd run: ring-slot rank gear, NO magics).
  Rank GEAR is seasonal in BOTH families — removed when the next event of the SAME kind opens
  (full DotD "removed each time X Gauntlet is summoned" parity). Trophies stay permanent.
- **D4/D5 — Names now, hooks later:** runs get lore names/blurbs/banners (content wave supplies
  final text); Tibius-style per-run unique mechanics deferred to T77.

## 0b. OWNER DECISIONS — LOCKED 2026-06-11 (TICKET-5-061126, the Gauntlet overhaul epic)
- **D6 — Single strike action, 1 ticket:** the Gauntlet DROPS raid hit sizes. One STRIKE button
  per press, costing exactly **1 ticket**; damage comes from battalion power (D8). Hit sizes
  (×1/×5/×20) remain in normal raids only. `GauntletConfig.StrikeRatePerSize` becomes
  irrelevant on the gauntlet fork (strike spend = flat 1).
- **D7 — "Ticket" = renamed Strike:** SAME ledger and earn/buy mechanics (+10 per stage defeat,
  gem purchase at `StrikeGemPrice`), surfaced as **tickets** everywhere player-facing (UI, shop,
  copy). Internal names (StrikeTransaction, DTOs) keep their names — no migration.
- **D8 — Dedicated Gauntlet BATTALION:** a separate loadout assembled from ANY owned
  unit/general, **no race restrictions**, configured on the Gauntlet page. Battalion power
  replaces the inert "Gauntlet Legion Power" placeholder (T54) and drives the gauntlet hit
  formula. Needs: battalion loadout state (new entity + migration), assign/read endpoints,
  power computation, hit-fork wiring, and a battalion-builder UI.
  **AMENDED 2026-06-12 (owner):** slots = **6 GENERALS + 20 TROOPS**. **BATTALION POWER is the
  gauntlet strike damage basis** with the raid weighting, base stats inherently included:
  `power = (playerATK + Σ battalionATK) × 4 + (playerDEF + Σ battalionDEF) × 1`.
  Strike damage = power × the raid RNG band; **Discernment raises crit chance passively as in
  normal raids and is NEVER folded into the displayed power.** The client (editor + card) and
  the mock combat already implement exactly this; the backend slice must match it.
- **D9 — No per-stage chat:** Gauntlet stages are solo rooms; the combat view shows no chat on
  Personal raids (SHIPPED 2026-06-11 client-side; the hub's participant gate already means a
  solo group has one member, so no server change). Crits on strikes VERIFIED already live —
  the crit roll in RaidService.HitRaidAsync is outside every GauntletEventId gate.
- **UI overhaul (T5.5) — SHIPPED 2026-06-12 (UI-first, owner-approved mockups):** GauntletScreen
  rebuilt — stage panel (stage number · boss-art slot · HP · single ⚔ STRIKE sending hit ×1,
  which costs exactly 1 ticket at current `StrikeRatePerSize.Small=1`), INLINE combat (no
  combat-view hand-off), ticket terminology throughout (D7), battalion placeholder card (D8
  backend pending — no dead Edit button), Leaderboard/Shop/Prizes nav toggles, gem→ticket buy in
  the Shop. Client mirrors `RaidHitResponse.NewStrikeBalance`; mock gauntlet hits spend tickets.
  The raid-summon remodel (TICKET-2) shipped in the same pass with the same visual language.
  REMAINING for D6-true: drop hitSize on the gauntlet fork server-side when the battalion slice
  lands (the UI never sends anything but ×1 already).

---

## 1. What DotD's Gauntlet actually was (the target)

From the wiki capture:

1. **Limited-time event** with a named location per run (West Kruna Gauntlet, Chalua Gauntlet,
   Karaduchi Gauntlet…). Each run is an *occasion* — its own identity, banner, and prize set.
2. **Strike loop:** players spend Gauntlet Strikes on Gauntlet Raids; **each raid defeated
   grants 10 Strikes + 1 Gauntlet Token** — the event self-refuels and snowballs for active
   players. Tokens spend in the Gauntlet Shop.
3. **3 leagues by level** (Whelpling / Wyrm / Dragon) — ROTA already mirrors this (by
   convergence tier). **Top 500 per league win prizes.**
4. **Prize ladder with steep, banded prestige:**
   - Rank-1 exclusive magic (SMITE) + rank 2–10 magic (Blessing of Mathala) — proc magics whose
     damage SCALES WITH TROPHIES OWNED (Aureate +100%/Argent +40%/Bronzed +10% on SMITE), and
     which are **removed each time the next Gauntlet is summoned** (you must defend your rank).
   - Rank gear bands: 1 / 2–10 / 11–50 / 51–100 / 101–200 (Gorgets/Bands — Atk/Def/Per % of
     base + flat + legion-power passive), also removed on next summon.
   - **Permanent trophies** at rank thresholds: Aureate (top 1), Argent (top 10), Bronzed
     (top 500) — legion-power boosts, max 1 each, kept forever. The trophy is the durable
     prestige; the magic/gear is the seasonal crown.
   - Top-500 token bonus table (Pitchforks/Tokens: 10/50 → rank 1 … 1/5 → rank 500).
5. **Boss-specific flavor** (Karaduchi's Tibius Sprightspring duel magic) — each event can
   carry a unique mechanical hook.

ROTA's System 16 already implements most of the *economy* (leagues, strikes, gem→strike,
token+pitchfork ledgers, highest-trophy multiplier, Wrath ×1.25 / Blessing ×1.10 auras,
rank-magic hand-off on open, settlement, shop, prize JSON). What's missing is the **event
shape and the page**.

## 2. Gap analysis (current ROTA vs DotD standard)

| # | Gap | Current state | DotD target |
|---|---|---|---|
| G1 | Event identity | One generic event row, CLI open/close | Named, themed runs ("The Gauntlet of <lore location>"), banner art slot, run number, per-run prize set |
| G2 | Event page | GauntletScreen = ladder/strikes/shop/leaderboard tabs, functional but flat | A true event landing: hero banner + lore blurb, countdown timer, league card with MY rank pinned, prize preview table, strike economy panel, shop, leaderboard — one cohesive page |
| G3 | Strike refuel loop | StrikesPerDefeat=10 exists; token-per-defeat exists | VERIFY both grant on every ladder-stage kill and surface the "+10 strikes +1 token" moment in the kill UI (the snowball feel is the point) |
| G4 | Solo ladder vs shared raids | Finite auto-advance solo ladder (power → stage) | DotD gauntlet raids were attackable raid bosses, not a solo treadmill. DECISION D1 below |
| G5 | Seasonal-crown removal | Rank magics hand off on next open | Extend to rank GEAR bands; make the "defend your crown" rule explicit in UI ("held until next Gauntlet") |
| G6 | Prize bands | gauntlet_prizes.json exists | Align bands to 1 / 2–10 / 11–50 / 51–100 / 101–200 / 201–500 + token-bonus table; rank-1-exclusive magic with trophy-scaled proc |
| G7 | Permanent trophies | Trophy mult shipped | Confirm acquisition thresholds (1 / 10 / 500), max-1, permanence, and show the trophy case on the event page |
| G8 | Event cadence | Manual CLI | Scheduled open/close (config dates), pre-event "coming soon" state on the Home CTA, post-event settlement screen ("you placed #N — rewards") |
| G9 | Event hook | None | Optional per-run unique mechanic slot (Tibius-style); content-driven, defer until content wave |

## 3. Build plan (slices, in order)

- **S1 — Event model upgrade (backend, LIGHT):** GauntletEvent gains Name, LoreBlurb, BannerKey,
  RunNumber, ScheduledOpenAt/CloseAt; lifecycle Scheduled→Active→Settling→Closed; lazy
  open/close on read (no scheduler dependency) + CLI override. DTO: GauntletEventResponse
  carries all of it + server countdown seconds.
- **S2 — Strike/token loop polish (backend, LIGHT):** verify + test 10-strikes-+1-token per
  stage defeat; add both to the hit/kill response DTO so the client can celebrate the refuel.
- **S3 — Prize/crown parity (backend, MODERATE):** per-run prize-set JSON (bands above);
  rank-gear hand-off mirroring rank-magic removal; token-bonus table at settlement; trophy
  thresholds 1/10/500 confirmed; settlement summary persisted per player (placed rank, what
  was won/lost).
- **S4 — THE PAGE (client, DEEP — the owner's actual complaint):** rebuild GauntletScreen as
  an event landing: hero banner + name + lore blurb + countdown; league card with my pinned
  rank + nearest rivals; prize table (banded, shows the seasonal-crown rule); trophy case;
  strike panel (count, regen/buy, "+10 per boss defeated" copy); shop tab; full leaderboard
  tab; post-event settlement view. Home CTA states: Coming Soon (date) / LIVE (countdown,
  glowing) / Settled (your placement).
- **S5 — Decision D1 implementation** (see below) if owner picks shared raids.

## 6. BUILD STATE (2026-06-10) — S1 foundation SHIPPED (uncommitted)
Built same session as the decisions (947u+111i=1058 green; migration **AddGauntletEventIdentity**
APPLIED to dev DB; client compiles clean):
- **Brackets:** `GauntletLeague` += `Ancient=3`; bounds → 1–999 / 1000–2499 / 2500–4999 / 5000+
  (code defaults + appsettings); provider validation covers the 4th league; mock mirrors.
- **Highest-stage ranking:** `GauntletEntry.HighestStage` (+ atomic SQL GREATEST
  `RecordStageDefeatAsync`, wired into the RaidService kill block via a new
  `GetGauntletRaidByDefinitionId` reverse lookup); rank snapshot ORDER BY highest_stage DESC,
  score DESC, tie_break_at ASC; leaderboard rows/DTOs + caller standing carry the stage; client
  board shows "Stg N · damage".
- **Late HP ramp:** `LateRampStartStage`/`LateRampFinalGrowth` (default OFF; appsettings 200→×2.0
  at 250); `GauntletStageCurve.Hp` piecewise — identical ≤ ramp start, near-doubling at the top
  (curve tests assert continuity, monotonicity, 1.95–2.05 final growth).
- **Event identity + kinds:** `GauntletEvent` += Kind(Neck/Ring)/RunNumber/LoreBlurb/BannerKey;
  open computes RunNumber per kind; rank-MAGIC hand-off is Neck→Neck only; settle uses kind-aware
  prize bands (`RingBands` in gauntlet_prizes.json optional — until authored, Ring falls back to
  Neck bands with MagicId stripped); CLI `gauntlet-open <name> <start> <end> [neck|ring]`;
  validator + admin endpoint accept kind/lore/banner.
- **Event page (first pass):** GauntletScreen header → identity card: NECK/RING badge (gold /
  purple), "name — Run #N", lore blurb, live 1-second countdown anchored on server
  `SecondsRemaining`; standing line = "Highest stage · Damage · Rank".
**S2-page slice SHIPPED 2026-06-10 (second session, uncommitted with the rest):**
- **Prize preview:** `IGauntletContentProvider.GetBands(kind)` (same Ring→magic-stripped-Neck
  fallback as the single-rank lookup) + `GET /api/gauntlet/prizes?kind=` →
  `GauntletPrizeTableResponse` (bands hydrated with trophy/magic display NAMES via a new
  `IMagicDefinitionProvider` injection into GauntletService). Client: PRIZES section on the event
  page + seasonal-crown copy ("trophies permanent; rank magic/gear held until the next <kind>").
- **Per-player settlement summary:** `GauntletOverviewResponse.LastSettlement`
  (`GauntletPlayerSettlementResponse`: event identity + League/FinalRank/HighestStage/Score +
  the band that rank landed in — tokens/pitchfork/trophy/magic + WonPrizes). Service:
  `GetMyLastSettlementAsync` = most-recent-settled event → caller entry → kind-aware band.
  Client: "LAST RUN — YOUR RESULT" card on the event page, shown in event lulls (no active event,
  or Coming-Soon window); hidden mid-live.
- **Coming-Soon state:** `GauntletEventResponse.SecondsUntilStart` (0 once started); JOIN rejects
  ("has not started yet") and the LADDER returns new flag `NotStarted` (nothing spawns) while
  `StartsAt > now` — an opened-with-future-window event is now visible but sealed. Client: Home
  CTA has THREE states (Coming Soon dim + "opens in Xd Yh" / LIVE glowing / Settled "you placed
  #N"); event-page countdown shows "⏳ Opens in…" pre-start; ladder card shows "arena sealed".
- **Mock (stateful):** `MockRotaApi.SetMockGauntletPhase("ComingSoon"|"Live"|"Settled")` +
  phase-aware overview/ladder/join/prizes + a MOCK EVENT PHASE toggle in the dev-tools Gauntlet
  tab (mock-only buttons) so all three states are playtestable without a backend.
- Tests: +9 unit (prize-table kind resolution + name hydration; settlement null/mapped/no-prize
  paths; join+ladder Coming-Soon gates; SecondsUntilStart) and +1 provider test (GetBands Ring
  strip). 956 unit + 111 integration green.

STILL TO BUILD (next slices): rank-GEAR seasonal grant/removal mechanism (needs neck/ring gear
content from T77 + a PlayerEventGear-style ledger); token-bonus table parity check at settle
(content tuning — T77/owner); banner art slot (needs art).

## 4. Decisions needed from owner (before build)

- **D1 — Ladder vs shared raids:** keep the solo power-ladder (current), switch to DotD-style
  shared league raids (every league member hits the same boss queue with strikes), or hybrid
  (solo ladder + one shared "league boss" that everyone's strikes also feed). Hybrid preserves
  shipped work and adds the communal DotD feel. **Recommend: hybrid.**
- **D2 — Event cadence:** how often / how long (DotD ran multi-day events; suggest 4–7 days,
  monthly-ish, manually scheduled at first).
- **D3 — Crown scope:** do rank GEAR bands also get removed next run (full DotD parity), or
  only magics (current)? **Recommend: full parity — removal is what makes rank 1 mean something.**
- **D4 — Per-run unique hooks (G9):** in scope for first rebuild or content-wave item?
- **D5 — Names/lore:** each run needs a named location → feed from the lore project
  (docs/LORE_HANDOFF.md; Gauntlet is "gladiatorial and prestigious" per tone guide).

## 5. Constraints

Everything in System 16's constraint block still binds: NO parallel combat path (all through
`HitRaidAsync` gated on `GauntletEventId`), ledger discipline, idempotent settlement,
server-authoritative everything. The page is presentation; the only new combat-adjacent code
is D1's shared-raid wiring, which reuses the existing raid engine.
