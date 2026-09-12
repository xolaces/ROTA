# Agent backlog — the self-supplied work queue

This file IS the queue. It survives context loss; conversation memory does not. Any session picking
up unattended work starts here and finishes here.

## Protocol

1. Read this file top to bottom before doing anything else.
2. Take the highest-ranked item in **Ready** that is not `BLOCKED`.
3. Do it. Verify it (build + tests, or an explicit calculation shown in the artefact).
4. Move it to **Done** with the commit sha and one line on what actually changed.
5. If the work reveals new work, add it to **Ready** ranked, or to **Owner decisions** if it needs a
   human call. Never silently expand an item's scope — split it instead.
6. Commit. Small commits, explicit paths.
7. **Stop the running API before building.** A `dotnet run` instance on port 5035 holds
   ROTA.Application.dll and ROTA.Infrastructure.dll, so `dotnet build` fails with MSB3027/MSB3021
   file locks — and `dotnet test --no-build` then passes against the STALE binaries, which reads as
   a clean run. Stop it, build, test, restart it:
   `Get-Process ROTA.Api | Stop-Process -Force`

## Standing rules that override any item here

- **Core game fundamentals never change.** Stat allocation is the point of the game. The energy wall
  is intended design. Server authority is not negotiable. If an item seems to require changing a
  fundamental, it becomes an Owner decision instead.
- **`git add -A` and `git add src` are forbidden.** They sweep the held content JSON into commits.
  Stage explicit paths, every time. (This has happened twice and had to be amended out both times.)
- **The owner applies production migrations.** Local dev auto-migrates in Development; the droplet
  does not.
- **The Application layer never touches EF Core, Redis, or the database directly.**
- **`xolaces/ROTA` is public.** A commit describing a weakness that is FIXED in that same commit is
  safe to push. One describing a weakness still LIVE is not. Three branches stay held and unpushed:
  `fix/forwarded-headers-prod-guard`, `docs/state-reconciliation`, `docs/security-sweep-2026-08-26`.
- **No Dawn of the Dragons art in the asset tree.** Kabam / 5th Planet IP.
- **UI copy voice:** say the fact, not the explanation. Short and human.
- Verify every claim against the code before writing it down. Prove a test catches a defect by
  temporarily neutering the fix and watching it fail, then restore.

---

## RE1 closed — 2026-09-11, the icon pipeline is live end to end

What the overnight run delivered, so nobody re-derives it:

- **Live:** `play.riseoftheancients.com` serves the `ROTA.Client6` build of `cd5ecc6` (`e29ede9` on
  top is mock-only). Verified in a fresh tab with `UnityCache` and `/idbfs` deleted:
  new wasm (41,157,831 bytes, Last-Modified 21:41Z), `[ROTA icons] atlas loaded: 362 frames`.
  Caddy sends `must-revalidate` on `/Build/*`, so a plain reload picks up the next deploy;
  `/opt/rota/web.prev` is the previous (c281c77) build for rollback via `cp -r`, never `mv`.
- **Pictures, in the Windows client in mock mode against the live atlas:** Bazaar potions as 32px
  rarity-bordered tiles; Crafting with the real Oathsteel Helm (output) and Conscript Helm
  (ingredient); the Bag with both helms; the gear detail header; and the Profile's equipped HEAD
  tile drawing the Conscript Helm after equipping it. Mock-only ids (`helm_ironstrike`,
  `chest_void_weave`, `mount_dusk_wolf`) have no art and keep the letter — that is the designed
  fallback, not a gap.
- **Two client fixes on the way:** the equipped-slot grid drew initials and never used the swatch
  (`1fb15bd`); a mock save written before gear carried `IconPath` overrode the seed and showed dots
  for pieces with real art (`e29ede9` derives the path from the id on load, the server's rule).
- **The brief now answers "what is left?" from disk** (`b906870`): `ART_BRIEF.md` opens with a
  progress table — 127 of 359 drawn, batches 1–16 done, 17–49 to do, next up Batch 17 (Materials 3
  of 6) — and every batch heading carries DONE / TO DO. `gen_placeholder_icons.py` no longer
  overwrites delivered art.
- Droplet junk removed: `/opt/rota/web.broken`, `/opt/rota/web.diag`.

**A lesson worth the line:** when driving the Windows client by screen capture, check WHICH window
is in front first. Firefox was fullscreen on the live game while the owner played; the first pass
captured Firefox cropped to the client's rectangle and sent three clicks into the owner's live
session before that was noticed. `SetForegroundWindow` is refused from a background process —
`SetWindowPos(HWND_TOPMOST)` plus `AttachThreadInput` for the front call works, and a
`GetLastInputInfo` idle guard (remembering that one's own synthetic input resets it) keeps the
pass out of the owner's way.

**Still open from this work:** the live `EquipmentService` path was not exercised in-browser — no
login was performed (the owner's password is not to be entered anywhere). The server always
populates `IconPath` per the IconReferenceTests; the first real login on the new build is the
owner's own confirmation. Everything else RE1 asked for is done.

---

## Ready — ranked

> **Note for an autonomous tick, revised 2026-09-11: Unity DOES build headlessly on this machine.**
> The 2026-09-04 note below said the opposite, and it was wrong for a reason worth knowing: the
> build script passed `-version`, which is Unity's own "print version and exit" flag, so every
> headless attempt quit before running anything and looked like a licensing failure. Fixed in
> `ROTA.Client6` `e097f7d`. The working gate for client items is now
> `.\tools\build-client.ps1 -Target WebGL` — a full IL2CPP build, ~10 min, exit 0 = compiles.
> **So R4 is takeable** (RE1 was, and is done). R0 is partly moot (see its entry).
>
> ~~The tick protocol's gate is `dotnet build ROTA.slnx` + `dotnet test tests/ROTA.UnitTests`, and
> neither compiles a line of Unity, so an agent cannot VERIFY either item under the rule it is given.
> R0's actual fix is also an Inspector checkbox, which is not a code change at all. Both need the
> owner, or a tick with a Unity headless-compile gate added to the protocol.~~

### RP2. The player model, phase 2 — real worn gear on the figure  *(owner go 2026-09-11; art-gated)*
Phase 1 is live: the Profile's EQUIPPED card is a mannequin with every worn piece's icon docked on
the body (`ROTA.Client6` `39e5920`, server `be9a126`). Phase 2 draws the gear ON the body: one
full-figure image per set in the mannequin's exact pose (`assets/icons/body/<setId>.png`, prompts in
the brief's last section), plus a one-time JSON of slot regions on that pose; the client shows each
worn piece through a window over its set's figure — head window from set A, torso from set B —
so mixed sets compose without hand-cut layers. Hooks already in place: `EquippedItemResponse.SetId`
/ `ReforgedFrom`, `FigureArt` (one figure today, a dictionary tomorrow), `PaperDoll` (docks today,
windows tomorrow). Blocked on the 13 figures; when they land, about a day of client work.

### RG1. Three relics nothing on the live server hands out  *(found 2026-09-11; Conscript settled 2026-09-12)*
The Conscript set is now the starter kit — granted and worn at registration, `grant-starter-kit`
backfills older accounts. Three no-set relics still have no acquisition path: `gear_cold_token`,
`gear_colossus_core` (both listed in `wire_drop_tables.py`'s DEEP_DROPS for zones that have no
table) and `gear_sovereign_tithe`. Owner call on where they drop, or whether they exist.

### RD1. Sweep — repeat a cleared node without replaying it  *(owner-deferred 2026-09-07)*
Auto-battle that unlocks only AFTER a first manual clear, so the proof-of-mastery gate survives but
the repetition does not. The paper calls this table stakes for 2026 and names the precedents (Raid:
Shadow Legends "Multi-Battle", MapleStory Idle "Sweep", AFK Arena "Fast Rewards").

**Why it matters here specifically:** the zone-rerun achievement ladder asks for up to **500 reruns**
of a node the player has already proven they can beat. That ladder shipped in System 25 and is the
single strongest argument for Sweep in the game.

Owner's framing for how it is earned: possibly a **$5 shop pack**, or a **zone 10+ reward** once the
campaign reaches that depth. Not yet decided, and the choice matters — a paid Sweep in a game whose
core loop is repetition is a monetisation decision, not a convenience one.

### RD2. Tourist mode / auto-scaling old raids  *(owner-deferred 2026-09-07)*
The paper is blunt that the "can't catch up" problem is what actually killed Dawn's acquisition, not
its grind: *"It is much more difficult for new players to get up to the ranks of other players these
days, due to a combination of power-creep and inaccessible raid events."*

ROTA already has the gap in miniature — chapter-1 raids sit at 4,000 HP and chapter-7 raids at
4,670,000. A new player joining a server a year in cannot meaningfully participate in most of the
raid list. Lost Ark's January 2025 Frontier system is the reference implementation.

Owner: assess later. Recorded now because the cost of retrofitting this grows with every chapter.

### RB1. Beta-tester acknowledgement — a badge on the profile  *(owner idea, 2026-09-07)*
The people who play the beta should carry a mark of it afterwards. Owner's framing: "a banner or
icon on their profile", explicitly a later feature — recorded now so the DATA it needs is captured
while the wave is running, because the one thing that cannot be reconstructed afterwards is who was
actually there.

**Do the cheap half now, the visible half later.** The backend already knows: a redeemed `beta_keys`
row names the player and the moment they were admitted, and `beta-reset` deliberately keeps redeemed
keys for exactly this reason. So wave membership is already durable and nothing is at risk.

What is NOT yet captured, and would be lost:
- **Which wave.** `beta_keys` has no wave/cohort column, so keys minted for wave 1 and wave 2 are
  indistinguishable after the fact. One nullable `cohort` column on `beta_keys`, set at generation
  time, is the whole fix — and it has to exist BEFORE the keys are minted, not after.

  *Softened 2026-09-11:* `BetaKey.CreatedAt` already exists, and the waves are separated by the
  reset itself — the 4 redeemed keys that survived `beta-reset` were all minted in June; anything
  minted after 2026-09-11 is wave 2. So the cohort is recoverable from the timestamp for as long as
  waves are separated by resets. The column is still the right long-term shape; it just stopped
  being a blocker for minting the next batch.

Later, when the badge itself is built:
- Derive it rather than storing a flag: `PlayerProfileResponse.BetaCohorts` from the redeemed keys.
  A stored bool drifts; a derived list cannot.
- Client renders it next to the display name. Unity-side, so not verifiable by the backend gate.

Ranked below the client items because it ships nothing a player sees this wave, but the `cohort`
column is genuinely time-sensitive — it is free before the first key is minted and archaeology after.

### R0. Client runs in MOCK mode — the playtest never touched the backend
> **Scoped down 2026-09-11.** This no longer affects the WebGL build, which is the primary way to
> play. `AppBootstrap.ApplyConfigOverrides` forces `useMock = false` and the production URL whenever
> `Application.platform == WebGLPlayer`, before any config is read — verified live: the September
> WebGL build rejects a duplicate registration, which `MockRotaApi.RegisterAsync` is incapable of
> doing (it unconditionally succeeds). What remains is Editor Play mode and the Windows standalone,
> both of which still read the scene's `useMock: 1`. Lower priority than it was.

`AppBootstrap.useMock` defaults to `true` and the scene's serialized value wins over the code default,
so `Assets/Scenes/Main.unity` starts on canned data. The console says `[ROTA] client started (MOCK).`
and the profile shows DEV_Owner at Lv 2498 with 24.8M gold — none of it from the API.

Fix is one checkbox: select `UIDocument` in the Hierarchy, Inspector → AppBootstrap → Backend →
uncheck **Use Mock**. The log line becomes `[ROTA] client started (http://localhost:5035).`

Worth doing properly rather than leaving as a checkbox: `ApplyConfigOverrides()` already reads
`ROTA_USE_MOCK` / `ROTA_BASE_URL` and a `rota-config.json`, but neither reaches the Editor
conveniently. Consider an editor-only default of live-when-a-backend-answers. *(The visible badge
shipped 2026-09-12: a red MOCK chip on the login foot and beside the header buttons whenever the
client is not talking to a server — `8024717` in the client repo.)*

### R1c. The Gauntlet shop is the last untested economy seam — BLOCKED on an owner call
Gems (`216c985`), raid loot (`a9ad926`) and quest rewards (`3adb93a`) are all now proven under
contention. The Gauntlet shop is the remaining one, and it cannot be tested yet: its gem-bundle
repeatability is an open owner decision (see below). A test written now would pin whichever behaviour
happens to exist rather than the intended one, which is worse than no test.

**BLOCKED on Owner decision 2.** Once settled, reuse the QuestRewardConcurrencyTests shape and
neuter the guard to confirm the test fails for the right reason.

### R10. Is 2,000,000 Discernment reachable near level 7,500?
Open tuning question left by the pacing eval. If quest cost tracks the pool, R depends only on
Discernment (0.366 floor -> 1.025 ceiling), which matches the owner's stated intent — autolevelling
available past 7,500, earned rather than reached. Whether the ceiling ANCHOR is right then depends on
whether ~2M Discernment is realistically held around level 7,500.

At 300 SP/raid that is ~6,667 raids. Nothing in this repo maps raids to level, so this needs either a
play-rate assumption from the owner or telemetry from the beta. If 2M lands far past 7,500 the anchor
moves down; the SHAPE is right either way, which is the good position to tune from.

**Partly answered, and the premise moved** (`docs/eval/DISCERNMENT_SINK_OPTIONS.md` §2). Measured from
`loot_tables.json`, a full World-raid clear grants 132 unassigned SP + 16 A/D/Disc, so a pure
Discernment build banks about 137 per clear — not the 300 this item assumed. 2,000,000 Discernment is
therefore ~14,600 World-raid clears: eight years at 5/day, four at 10. The anchor is reachable by a
dedicated player on a long horizon, and NOT by a typical one near level 7,500. The shape is still
right; the anchor is high.

---

## Blocked

*(B1 cleared 2026-09-04 — see Done.)*

---

## Owner decisions — the agent must not decide these

0m. **The atlas is a derived file being committed as a source file, and it is 24% of the repo.**
   *(measured 2026-09-10.)* `assets/icons/atlas.png` is rebuilt whenever any of the 362 icons
   change, and every rebuild writes a wholly different 4.4 MB binary — compression means one changed
   tile alters the entire stream, so git stores a full new blob every time and can never delta them.

   Measured, not estimated: **9 versions, 19.7 MB, in an 82 MB `.git`.** Nine art batches have
   landed. Thirty-five remain. At the current rate that is roughly 150 MB more of pure derived
   churn, on a repo that is **public**.

   **This is cheap to fix right now and expensive later: all 129 commits are unpushed.** Nothing has
   left the machine, so history is still local and rewritable.

   Three ways out, and the choice is a real tradeoff:
   - **Keep committing it.** Simplest. A fresh clone builds and runs with art, no toolchain needed.
     Costs the repo size, permanently and publicly.
   - **Rebuild only at release points**, not per batch. Keeps most of the benefit, cuts most of the
     churn. But `IconReferenceTests.The_atlas_carries_a_frame_for_every_icon_on_disk` fails the
     moment the atlas is stale, which is the test doing its job — so this means accepting a red test
     during an art pass, and that is a bad habit to install.
   - **Drop it from git and generate it during the Docker build.** Correct in principle: derived
     artifacts do not belong in source control. Costs a Python step in the image, and a fresh clone
     no longer has an atlas until something builds one.

   The agent should not pick. Rewriting published history is destructive, and *when* to spend the
   one cheap window is a judgement about how much the public repo's size matters.

0l. **Two Gauntlet rank magics are 3.4x the strongest ordinary Orange, and the mechanism they were
   built for no longer exists.** *(found while rebalancing the magic catalogue, 2026-09-07.)*
   `magic_wrath_of_the_ancients` (27% x +250%, EV 0.675) and `magic_blessing_of_the_ancients`
   (15% x +425%, EV 0.637) carry `OffCap = true` — they were designed as System 16 auras applied
   OUTSIDE `MaxAggregateProcBonus`. That path was later removed; `RaidService` now says plainly
   *"there is no longer any off-cap aura path at all; offCapBonus stays 0."*

   So they no longer bypass the cap — they flow through the ordinary magic loop, where the strongest
   normal Orange is EV 0.198. They are 3.4x it. I rebalanced them into the band, their
   locked-number tests failed, and **the tests were right**: this is pinned Gauntlet prize content,
   not catalogue content, and it is not the agent's call. Reverted and exempted from
   `MagicProcBandTests`.

   Three ways out, all owner calls: (a) leave them — a seasonal trophy for the single top Gauntlet
   player is arguably meant to be this strong; (b) restore an off-cap path so they behave as
   designed; (c) bring them into the band and let the prize be the exclusivity rather than the
   number.

0k. **The sigil fix cut raid access by 40x. What should the rerun rate be?** *(highest-value open
   call in the queue — it decides how often half the game happens.)*
   `5ababaa` was right: sigils were dropping per boss ATTEMPT, and a boss is 40 attempts, so the 15%
   rerun chance was firing 40 times a clear (6.00 expected, at-least-one 99.85%). But
   `SigilRerunDropChance = 0.15` was the number that SURVIVED the fix, and it had been living inside a
   40x multiplier nobody had priced. Measured in `docs/eval/SIGIL_SUPPLY_AFTER_THE_CLEAR_FIX.md`:

       zone                    energy/sigil before   after    days (Conscript)
       c1z1 Ashen Causeway                     127   5,067                  9d
       c6z4 Throne of Ancients               1,040  41,600                 70d

   A chapter-6 raid summon is now 34-70 days of banked energy depending on class. Nothing else supplies
   a personal sigil: the 104 first-clear guarantees are one-off, guild `Sigil` is a separate ledger
   currency for guild raids, no shop sells one and no loot table drops one. Four options priced in the
   doc — raise the rate (one line: 0.30 -> 35 days, 0.50 -> 21, 1.00 -> 10), grant N instead of rolling
   (removes a long geometric tail), cut the 40-attempt boss so a rerun costs less, or accept raids as a
   monthly event. Raid cadence is core pacing, so this is the owner's.

0j. **Hoard's raid drop bonus is killer-only, and both World raids have no killer.**
   `RaidService`: `hoardForThisPlayer = p.PlayerId == callerPlayerId ? callerHoardDropMultiplier : 1.0`
   — a documented performance trade (scaling every participant needs a mastery read per participant
   inside the advisory-lock tx, the cost System 22 deferred). Its consequence, measured in
   `docs/eval/DROP_CURVE_COVERAGE_AUDIT.md` §4: a MAXED, PLEDGED Hoard is worth 8% relative to one
   player per kill, so 0.32% in expectation across a 25-player raid — and **exactly 0.000% on both
   World raids**, because they ship `baseHp: 0`, end only on their timer, and settle with
   `callerPlayerId = Guid.Empty`, which no participant can match. Those are the two raids that grant
   132 unassigned SP against a Standard raid's 20 and are the game's only source of
   Attack/Defense/Discernment points. Either the per-participant read gets paid for, or Hoard's raid
   lane is accepted as decorative. Not the agent's call: it is a performance/design trade, not a bug.

0h. **The 15,000,000-100,000,000 endgame Discernment anchor is not reachable — what replaces it?**
   Measured in `docs/eval/DISCERNMENT_SINK_OPTIONS.md` §2 from shipped content: a full World-raid clear
   grants 137 Discernment to a pure build, so 15,000,000 is **109,223 clears — thirty years at ten a
   day** and 100,000,000 is four hundred. This figure is load-bearing: it is the stated rationale for
   the `f59e481` retune and the frame for R6b, R7b and R10. Either the anchor comes down to something
   like 1,000,000-10,000,000 (which is what the current caps already fit), or SP grants go up by two
   orders of magnitude. Both are fundamentals; neither is the agent's to pick.

0i. **Crit chance caps after 728 raid clears. Is that intended?**
   `MaxCritChanceBonus / CritChancePerDiscernment = 0.10 / 1e-6 = 100,000` Discernment, which at 137
   per clear is **73 days at ten clears a day, a year at two**. Crit damage caps at 500,000 (one to
   five years) and rare drops reach 90% of their bonus at 1,000,000 (two to ten years) — those are
   fixed sinks reached after a long time, which is allowed. Crit chance is the outlier by roughly 10x
   and is the one genuine saturation defect the sweep found. Options in §5 of the same doc; it also
   interacts with Owner decision 6 (whether the early-game crit nerf stands).

0f. **Should the market be switched on, and what must be settled first?**
   System 27 ships in `5cdc3c1` with `MarketConfig.Enabled = false`. Three calls belong to the owner:
   **(i)** the Gauntlet gem bundle (Owner decision 2) is a STATED PREREQUISITE in
   `docs/design/PLAYER_MARKET.md` §4 and is still open — its idempotency reference is constant per
   account, so a second purchase charges nothing and returns SUCCESS. Behind a market that is a money
   printer. **(ii)** the combined fee is 2% + 8% = 10%, the bottom of the band the design doc calls
   normal; raising it is easy, lowering it and needing it back is not. **(iii)** consumables and gems
   are NOT tradeable, which sidesteps the pacing interaction and declines to build the whale/F2P
   bridge. Both are config, both are reversible, and both change what the market is for.

0g. **Is there a bind-on-pickup concept?**
   Nothing in the item model marks an item untradeable, so every Orange in the game — the Armory
   relics, Pano's set — is listable the moment the market opens. `MarketConfig.Untradeable` is a
   by-id stand-in. The question `PLAYER_MARKET.md` open question 5 raised is now live rather than
   theoretical: a chase item that can be bought changes what the rare-drop curve means.

0e. **Should raid threshold gear roll against its chance, or stay unconditional?**
   `RaidService.DistributeKillRewardsAsync` grants threshold gear with **no chance roll** — the
   comment states it deliberately: *"a GUARANTEED drop, so it is intentionally NOT Hoard-scaled."*
   Threshold rewards are also cumulative, so gear on rung *n* is granted once for every rung at or
   below the player's damage. Together those mean a `chance` value on a `GearDropChance` inside a
   RAID table is **dead data** — the field exists, the JSON carries it, nothing reads it. (In QUEST
   tables the same field IS read, which is what makes this a trap.)
   The 26 raid tables written on 2026-09-04 now put exactly one piece on the last rung at chance
   1.0, so the JSON states what the engine does. That is a workaround, not a decision. The call:
   **(a)** leave it unconditional and treat one top-rung piece as the raid's gear reward, **(b)**
   make raid gear honour `Chance` like quest gear does, so a raid can carry a chase piece, or
   **(c)** drop `Chance` from `GearDropChance` on the raid path so the dead field stops inviting
   the mistake. (b) is the only one that lets a stamina build chase gear the way an energy build can.

0d. **Regen is flat while pools grow linearly, so nothing paces play past low level.**
   `RegenMinutesPerPoint` grants one point per interval regardless of level, so daily regen is a
   constant 288 while the pool it fills is linear in level. A level costs 5.4 days at level 500 and
   92.6 days at 7,500 on regen alone; the "little or no waiting" early game exists only below roughly
   level 200. Potions are therefore not a supplement to the pacing curve past low level — they are
   the whole curve. Four options (proportional regen, level-scaled interval, accept potions as the
   lever, or target R at 0.6-0.8) are in `docs/eval/AUTOLEVELLING_AND_PACING_EVAL.md` Finding 4.

0c. **Defense is a dominated stat — there is no build where it is the right allocation.**
   It is weighted 1x against Attack's 4x in both raid damage (`RaidService.cs:918`) and Gauntlet
   battalion power (`GauntletBattalionService.cs:140`). Its only unique role, Gauntlet damage
   mitigation, saturates at **800 Defense** and protects a resource that gates nothing —
   `RaidService.cs:899` clamps health at 0 and never blocks a hit. Ordinary and guild raids have no
   Defense mitigation at all.

   This matters more than an ordinary balance number because stat allocation is the stated point of
   the game, and this makes the Attack/Defense half of it not a choice. Four options with trade-offs
   are in `docs/eval/SATURATING_SINKS_SWEEP.md`; only enabling the 0-health gate AND giving mitigation
   a scaling curve makes Defense real, and the gate means a third pool starts rationing play.

0b. **There is no prestige mechanic, and the literature treats one as the standard release valve.**
   ROTA has mastery (additive, permanent) and one-way stat allocation with no respec — nothing that
   resets any curve. The idle-game literature treats prestige as the normal answer once late-game
   costs make progression prohibitively long, which is the exact shape ROTA's late game has. A
   Dawn-faithful async RPG may deliberately not want one; the point is that its absence should be a
   decision rather than an omission. See `docs/research/ECONOMY_VS_GENRE_STANDARDS.md`.

0. **The potion refund ratio has the wrong SHAPE — decide before it is built.**
   *(Reframed 2026-09-03 after the owner clarified that autolevelling is wanted past 7,500. The
   original framing — "the design ends the energy economy" — was written assuming autolevelling was a
   defect. It is not; the ratio is simply mis-sited.)*

   Against the stated intent the design is too STINGY early and unbounded late: at level 7,500 a
   50/50 player gets R = 0.287 where ~1.0 is wanted, then R climbs to 3.8 by level 50,000. Both halves
   are one cause — R is proportional to level because the pool grows and quest cost caps at chapter
   16. Letting cost track the pool removes the level term and leaves R depending only on Discernment
   (0.366 -> 1.025), which matches the intent by construction and makes autolevelling earned through
   raid SP. See `docs/eval/POTION_ECONOMY_EVAL.md` and `AUTOLEVELLING_AND_PACING_EVAL.md`.

   ORIGINAL NOTE, still true: quest cost caps at chapter 16 while the pool grows forever, and below
   the 200-energy gate the cost cancels out of the ratio entirely, so no choice of quest avoids it.
   `QuestConfig.ChapterScalingCap = 16` caps quest energy cost at chapter 16 while the pool grows
   linearly with level forever, so the refund ratio crosses 1.0 and keeps climbing. Below the
   200-energy gate the cost cancels out of the ratio entirely, so no choice of quest avoids it and a
   player who never invests in Discernment still crosses at 36,716. Four options with trade-offs are
   in `docs/eval/POTION_ECONOMY_EVAL.md`; only "make cost track the pool" removes the term rather
   than bounding it, and it is what the design doc already assumed was true. This is a core-design
   call about how quest cost works, so the agent will not pick.

Full context in `docs/EVALUATE_LATER.md`. Summarised here so the queue is self-contained:

1. **Should zone Guardians drop items at all?** 23 of 25 raids carry no loot table. Empty is a
   supported state, not a defect — the loot pass short-circuits before `GetById`.
2. **Can a Gauntlet gem bundle be bought more than once?** Its idempotency reference is constant per
   account, so a second purchase charges nothing, grants nothing and returns SUCCESS. A fix was
   drafted and deliberately reverted: `GauntletShopIdempotencyTests` pins buy-twice-charges-once as a
   System 16 Slice 6 spec requirement while the enum calls the entry repeatable. Both cannot be right.
3. **How long should a guild invite live?** `GuildJoinRequest.Expire()` has no callers and no time
   field to drive it, so a Pending invite lives forever and silently blocks re-invitation.
4. **A retention window for four unpruned tables.** Refresh tokens need a WINDOW, not a purge —
   deleting them silently breaks replay detection. Also the mastery/achievement idempotency ledgers,
   and whether to materialise a balance column instead of SUMming the lifetime ledger on every
   profile read. No purge job exists anywhere in the repo.
5. **Gauntlet settlement is now automatic** — prizes go out on a timer with no human review. Say if
   it should auto-close only and leave payout manual. One-line change.
6. **Does the early-game crit nerf stand?** Moving crit saturation out 100x means 1,000 Discernment
   is now worth +0.1pp instead of a capped +10pp. Endgame is correct; early game got quieter.
7. **Block scope** — PMs only, or no contact at all.
8. **Developer leaderboard eligibility** — in or out.

---

## Done

- **Conscript is the starter kit** *(2026-09-12, owner call)*. `starter: true` on the eight pieces in
  `gear.json`; `EquipmentService.GrantStarterKitAsync` grants and wears them inside the registration
  transaction, idempotently (a piece owned is not granted twice, a worn slot keeps what it wears);
  `grant-starter-kit <user|--all>` in the admin CLI backfills. Boot validation refuses two starter
  pieces in a slot. Unit + content tests, and one end-to-end registration test.
- **RF1 — TrustedProxies accepts a CIDR** *(2026-09-12)*. `TrustedProxies.Apply` puts an entry with a
  `/` in `KnownIPNetworks` and a bare address in `KnownProxies`; the droplet override now names
  `172.18.0.0/16`, so a recreated Caddy on a new address keeps X-Forwarded-For honoured. Four unit
  tests; `BETA_DEPLOY.md` §7b carries the CIDR.
- **A looted raid is not stored** *(2026-09-12)*. Loot deletes the participation in the grant's
  transaction, the last claimant's conditional delete takes the raid; the sweeper purges Lootable
  raids past `RaidConfig.LootClaimDays`, failed health-pool raids past their clock, and legacy Looted
  rows. The completed-raid history (endpoint, DTO, query) is gone. Client: Raid lands on the Raids
  tab (loot / yours / open), Public is only unjoined raids, Completed tab gone, sigil summon opens
  the raid, guild raids are lootable from the guild list.
- **R4 — nine-rung ladders in the client** *(2026-09-12)*. The achievements screen was already
  data-driven (no six-tier assumption), but it rendered every rung as a card — ~500 rows. A mastery
  ladder is now one card: the rung being worked on and how many are earned; ZoneMastery and
  RaidMastery have names; the mock ladder matches the server's nine rungs with threshold-keyed ids
  (`1902a54`). Same day: the combat header shows the damage rung you are on and what the next costs,
  the summon ledger shows a rung's damage rather than `0%+`, and the mock seed is regenerated from
  the live tables (`a52f6e6`); the Raids nav dot is live (`cc1658f`).
- **Security sweep** *(2026-09-12)*. `EndpointAuthorizationSweepTests` walks the live action table
  (every route protected or on a named anonymous list; admin prefixes carry their policy; 401 with
  no token, 403 as a plain player, live). play.riseoftheancients.com now sends HSTS / nosniff /
  DENY / referrer / permissions / a CSP (`'wasm-unsafe-eval'` is all Unity needs) and serves Build/
  precompressed (.gz/.zst twins made by `tools/deploy-webgl.ps1`). API: no `Server` header, 64 KB
  body cap, `/icons/body` revalidates. Client: rich text off on chat, PMs, guild text.

- **Player model, phase 1** *(2026-09-11)*. `tools/art/gen_body.py` → a mannequin + dock map served
  at `/icons/body/`; `EquippedItemResponse`/`OwnedGearResponse` carry `SetId` and `ReforgedFrom`
  (`be9a126`). Client `FigureArt` + `PaperDoll`: the Profile's EQUIPPED card is the body with each
  worn piece's icon docked where it is worn, gilt for reforged, + for empty (`39e5920`). Live.
  Also `tools/ledger/build.py` now writes complete DotD-shaped raid loot pages (`d2c08a7`).

- **Raid loot, proper** *(2026-09-11)*. Three server fixes and one content pass. `b8884cd`: the
  live profile's ATK/DEF chips were always 0 (Stats never loaded with the profile). `9d8cfd9`:
  campaign raids paid every rung to everyone — damage-keyed rungs were only honoured on World
  raids. `4f7920c`: Reforge recipe category; the crafting catalogue carries art references.
  `02563af`: `tools/content/reforge_sets.py` — 96 reforged pieces, 24 parts, 96 recipes, parts on
  every raid ladder (scrap 12→30%, tack 4→10% at the top by difficulty), guild raids get tables,
  quest gear pools re-rated to 1/50-and-rarer per piece, equal within a pool. Manual §5.3–5.5.
  Client: Reforge tab, art from the DTO.

- **RE1 — client art pipeline** *(2026-09-10 → 11)*. `ROTA.Client6` `c281c77`: `IconAtlas` fetches
  `/icons/atlas.json` + `atlas.png` once at boot, `Sprite.Create` per frame cached by stem, one
  `Apply(VisualElement, artKeyOrIconPath)` for both DTO conventions, 19 sites across ten screens;
  swatch stays the rarity dot when art is missing. `1fb15bd` the equipped grid; `cd5ecc6`
  `runInBackground`; `e29ede9` mock saves. Live on `play.riseoftheancients.com`, verified with
  pictures — see the close-out note above Ready.

- *(2026-09-11)* — **The beta is wiped and the September client is live.** `beta-reset --confirm
  WIPE-BETA` applied on production after a dry run and a pre-wipe backup (772K): 5 accounts reset in
  place, 0 purged, 11 unredeemed keys deleted, 4 redeemed kept, 3,688 rows across 24 tables, dry run
  and real run identical to the row. The post-check passed, so all 5 accounts are loadable at level 1.
  **RC2 closed by this** — nobody is past a milestone any more, so the pinnacle grant covers everyone
  from here and the backfill never needs to exist. Separately, the WebGL client was rebuilt from
  `feat/mock-playtest-accounts` (all five client branches, 20 commits past `master`) and shipped to
  `/opt/rota/web` — 41,018,641-byte wasm, byte-identical local and remote. Three builds failed
  first: twice on `-version` being Unity's own flag (fixed, `ROTA.Client6` `e097f7d`), once on a
  June artifact owned by the old Windows install's SID (`takeown` from an admin shell). Caddy now
  sends `must-revalidate` on `Build/*` so the next deploy is picked up without a cache clear.
  Confirmed live against the real API rather than mock: a duplicate registration is rejected, which
  `MockRotaApi` cannot do.
- *(deploy, 2026-09-10)* — **132 commits to production, and the first real art off the server.**
  Backup taken (769K), `AddPlayerMarket` and `AddAchievementIncompleteIndex` applied via idempotent
  script and verified three ways (history rows, `to_regclass` on every new object, the full
  `verify-prod-schema.sql` — every verdict OK, 55 files = 55 recorded). Image built with the icon
  layer; `/health` Healthy; `/icons/gear/gear_stoned_horns.png` returns 200 at 169,311 bytes, the
  same byte count as the local file. **One outage, self-inflicted by the runbook:** step 7b wrote
  `ForwardedHeaders__Enabled: "true"` with no `TrustedProxies`, and `a7abe05`'s new boot guard
  refused to start on exactly that — correctly, since the combination already behaved as disabled.
  Fixed on the server by listing Caddy's IP; fixed in the runbook so 7b writes both lines and 7c no
  longer calls it "not a launch blocker". Raised RF1 for the CIDR fix that removes the remaining
  silent-failure mode.
- `b29b3f3` — **RC1: reaching a milestone level now actually awards the magic.** Everything
  around it already worked — the levels, the seven definitions, the gems, the first-claim row — and
  the one line that hands the player the magic did not exist. Granted at the same chokepoint as the
  gems (`StatService.GrantLevelUpPointsAsync`), by convention rather than a mapping table: level
  10,000 grants `magic_pinnacle_10000`, and `PinnacleMagicTests.EveryPinnacleLevelHasAMagicAndViceVersa`
  already pins that correspondence in both directions. **Not gated on the first claim** — the first
  player designs the magic, everyone after inherits the design and owns it too, so gating would give
  each milestone's magic to exactly one player forever. Seven tests, proven by neutering the grant
  and watching them fail. Verified live as well: crossing 1,000 granted Ascendant's Banner, then
  crossing 2,500 granted Luminary's Vow, two rows and no duplicates. Raised RC2 (no backfill for
  players already past a milestone).
- `dafe718` — **the icons are served, and 186 of the 362 paths pointed at nothing.** The API
  registered no static file middleware at all, so every icon path it sent a client addressed a URL
  it would not answer. Most were wrong regardless: every hand-set path dropped the id's family
  prefix, and every recipe pointed into `icons/craft/`, which has never existed. Nothing caught it
  because with no file server, no request for these paths was ever made. Fixed in three places —
  `normalize_icon_refs.py` now repairs rather than preserves, the csproj links `assets/icons/**`
  into the build output, and `Program.cs` serves that directory AT `/icons` so the DTO string is the
  URL verbatim. Ahead of auth and rate limiting, because art is public and a cold-cache client would
  otherwise spend its whole bucket on pictures. Uses `AppContext.BaseDirectory`, not
  `ContentRootPath` — linked icons exist only in the build output, and the warning on the not-found
  branch is what caught that on the first run. Verified live: 400/400 references return 200, bytes
  match disk, `If-None-Match` gets 304, `../` and `%2e%2e/` get 404. Three tests added so it cannot
  rot. Raised RE1 and Owner decision 0m.
- `0025a1e` — **audit tick: what the sigil fix did to raid access.** Every Ready item was blocked or
  Unity-side, so this was R6/R7-style work. Started from the raid-HP retune (did cutting HP up to 280x
  inflate kill rewards? yes, 28x-313x per stamina — but the SHAPE improved, spread 123x -> 15.5x), which
  led to the real question: stamina is not the binding constraint on raid farming, sigils are. A
  chapter-6 sigil costs 41,600 energy in expectation, 70 days for a Conscript. Also measured en route:
  gem rewards are banded flat per chapter while HP rises 32.6% a raid, so only **8 of 26** raids are ever
  the best gem farm at any player power (gold and XP are fine at 24 of 26) — noted, but moot while
  sigils bind. Raised Owner decision 0k.
- `8f845a9` — **R11: the tutorial's "four passes" is pinned, and its arithmetic was wrong.**
  Four tests derive the number from the shipped `appsettings.json`, the shipped `q001` row, the real
  `ResourceReward.RollSummed` and a real `StatService` — not from restated constants. The answer is
  four, but the doc's "4 x 7.5 XP = 30 = TNL(1), exactly" was not: the roll rounds away from zero, so
  an attempt pays **8**, three pay 24 and fall short, four pay 32 and overshoot 30 by 2. Doc corrected.
  Neutering the XP rate to 2.0 fails the guard, and appsettings restored with zero diff.
- **Owner-reported, outside the queue:** `5ababaa` **sigils dropped per ATTEMPT, not per clear** — a
  boss depletes 2.5 from 100, so 40 rolls a clear turned a 15% rerun chance into 6.00 expected sigils
  and 99.85% at-least-one, a 40x oversupply of the item that gates raid access; the first-clear
  guarantee fired on attempt ONE, contradicting System 25's own comment. Same defect the quest-boss
  gem grant had and was fixed for in 2026-06-22, left in the block beside it. `b723830` **raid health
  retuned** to the owner's anchors, 4,000 -> 2,000,000 across the 23 campaign raids (+32.6% a step),
  with the loot ladders regenerated because they are keyed to FRACTIONS of each raid's own pool —
  retuning health alone would have stranded every rung silently, and `RaidLootLadders_StayKeyedToTheirRaidsOwnPool`
  now catches exactly that.
- `R7b` — **audited; both halves came back differently than the item assumed**
  (`docs/eval/DROP_CURVE_COVERAGE_AUDIT.md`, guard in `DropCurveCoverageTests`). Quest side: there are
  no unflagged entries to move — all 832 quest chance/gear drops carry `rareScaling`, so the capped
  multiplier `Scale()` is unreachable on the quest path with shipped content. Raid side: the twin is
  NOT the same shape. Discernment multiplies by `1 + 0.03D` (unbounded); Hoard multiplies by at most
  **1.08** (4.0% at level 5 x PledgeMultiplier 2.0), so the 0.95 clamp is inert — 93.3% of the 1,424
  raid entries get Hoard in full, and of the 96 clipped, 80 have base >= 0.95 where Hoard does nothing
  anyway. Three tests pin the quest coverage at 100% going forward (it was NOT protected, and one
  unflagged future drop lands on a curve finished at 30 Discernment for a 50% base); neutering one
  entry fails the guard, and the content file restored byte-identical. Raised Owner decision 0j.
- `R6b` — **answered, and its premise was wrong** (`docs/eval/DISCERNMENT_SINK_OPTIONS.md`). The item
  asked for a scaling sink because the fixed ones "saturate inside the first 0.5% of a 15M-100M
  endgame". Measured against shipped grant rates, that endgame is **30 to 400 years of daily play** —
  so the sinks are not badly placed, the anchor is fiction. What IS broken is narrower: crit chance
  caps after 728 clears (73 days at ten a day), crit damage after 3,641, while rare drops are
  correctly placed at four to ten years and the 10M hard clamp is simply unreachable. Four scaling
  shapes are priced anyway (log / multiplier-lane / rank ladder / clamp removal, with the arithmetic)
  in case the rate ceiling is ever lifted. Raised Owner decisions 0h and 0i, and gave R7b and R10 the
  numbers they were missing.
- `643b926` — **R5d: the partial index, as a migration.** `ix_ap_player_incomplete` on
  `achievement_progress (player_id) WHERE NOT is_completed AND NOT is_deleted`, added to the EF model
  and generated with `dotnet ef migrations add` so the snapshot stays in step (MigrationSnapshotTests
  green is what proves it). The benchmark it implements: reads 1.662 ms -> 0.096 ms, writes +10.9 us
  per incremented row, 432 kB against the 56 MB composite. NOT APPLIED — fifth pending. The migration
  carries the live-database procedure in its doc comment, including that a re-run over an existing
  index FAILS rather than no-ops, so the CONCURRENTLY route needs the history row written by hand.
- `5cdc3c1` — **System 27, the player market**, off by default. Consignment only: no endpoint moves
  an object between two named players. Escrow at listing, a status latch before any gold moves, an
  atomic seller credit (new `IPlayerRepository.AddGoldAsync`), daily gold caps read from an
  append-only ledger, and an expiry sweep so a lapsed listing gives the stack back. 22 unit tests
  plus a 33-case live pen suite (`tools/pentest/market_pentest.py`): 8 concurrent buys -> 1 winner
  and 1 debit; 6 concurrent cancels -> 1 success and the stack back once; gold conserved with the
  fee destroyed. Raised Owner decisions 0f and 0g.
- `d40dc84` — **Raid gear was four guaranteed copies a clear.** My own defect from `66808bf`: I put
  gear on the top four of eight cumulative rungs with a `chance` field the raid path never reads, so
  a Mythic clear paid 4x a Purple mount and 4x an ORANGE relic, every time. Now one piece on the last
  rung at chance 1.0, never Orange. The two hand-authored raid tables are byte-untouched. Raised
  Owner decision 0e.
- `3b0d2b1` — **A relic item stopped being rare at 63,300 Discernment.** `RareScaling` was on
  `GearDropChance` and not on `ItemDropChance`, so every item chance-drop used the generic
  `base x (1 + 0.03d)` curve clamped at 0.95. A 0.0005 relic hits that clamp at
  `d = (1900-1)/0.03 = 63,300`. Added the flag and honoured it; four tests, one of which asserts the
  OLD behaviour so the trap stays visible. Neutered: 2 of 4 fail.
- `66808bf` — **Every zone and raid now drops something, with a reason.** 134 quest nodes and 26
  raids had no loot table at all. +56 tables. Road reagents (energy/quests) and Field reagents
  (stamina/raids) never appear in each other's tables, so no single pool buys the other's materials.
  Verified live: cleared ch1 z0 against the running API and the Ashen Causeway paid `mat_causeway_ash`
  x144 off the new `lt_zone_c1z1`.
- `369e441` — **B1 cleared.** The held Black Archive nodes and raids are committed (separately, so
  they stay easy to drop), and `q_lastwatch_boss` got the sigils map it was missing — it declared
  `sigilDropChance: 0.2` against a null map, which the content validator refused, blocking all
  content verification.
- `398f134` — **59 new items.** A full Green/Blue/Purple gear ladder across all 8 slots (the game had
  8 Grey, 1 Blue, 8 Orange and nothing between), six Armory relics from Master Canon XIX, two reagent
  lines, the upper draught rungs, two stat-bag rungs, four Black Archive sigils.
- `e9ce608` — **A large `page` 500'd three endpoints.** `(page - 1) * pageSize` in int arithmetic
  overflows at page 10,737,420 against the shipped PageSize 200; PostgreSQL then refuses the negative
  OFFSET. Reproduced live on `/api/leaderboards` and `/api/guilds`, fixed in one shared helper,
  re-verified live.
- (R9) — **E/S parity pinned to all four constants that create it**, not the two the item named: the
  LSI weight, stamina's skill-point price, and both XP rates. Tests read the config rather than
  repeating it. Verified by retuning each constant alone — every one breaks parity. Also collapsed
  the LSI stamina weight from a literal duplicated in `PlayerStats` and `StatService` into one
  `PlayerStats.StaminaLsiWeight`.
- `8e94cf4` — **R12: the pool-cap refusal now teaches.** Was a cap constant and a current value with
  nothing to act on; now states how many points fit at this level, that the ceiling rises, and — only
  when it is actually biting — that Stamina counts twice. Five tests assert the count against the cap
  arithmetic rather than a fixed string, so retuning 7.45 does not falsify them. Verified by
  restoring the old message: six tests fail.
- `91c3980` — **stamina now costs 2 SP.** It counts double toward the LSI cap but charged 1, so a
  stamina build reached the same ceiling for half the skill points and banked the rest in
  Attack/Defense/Discernment — strictly better by exactly 2x. Price moved to
  `LevelingConfig.SkillPointCostByStat`, every `AllocateToX` now takes the charge explicitly so a
  future price cannot silently no-op, and Energy 1 / Stamina 2 is recorded as owner-locked. Verified
  by reverting the price: the parity test reports 372 SP against 745.
- `223dcd2` — SP cost-curve options, the tutorial opening design, and a correction: my earlier "all
  E:S splits are equal" finding was true per LSI point and false per skill point.
- `2d7ae80` — **R8 numeric-headroom sweep, and the one real find fixed.** All the big accumulators are
  already `long` (TotalDamageDealt, GauntletEntry.Score, gem ledger) and the narrowing casts are safe
  by construction. The exception: `GauntletConfig` had NO validation, and raising `MaxLadderStage` from
  250 to 300 overflows `long` by ~4,000x — silently, because the double→long cast is undefined and
  yields a garbage MaxHp that reads as a timer-only raid. Now a boot guard with tests.
- `a55a226` — **saturating-sinks sweep.** Found that Defense is strictly dominated by Attack
  everywhere: 1x vs 4x in damage, its only unique role (Gauntlet mitigation) capped at 800 Defense,
  and the health it protects gates nothing. Now Owner decision 0c. Also found the generic Discernment
  drop multiplier saturating between 30 and 3,133 Discernment (now R7b). Full inventory of every
  capped sink in the document.
- `9fbfab8` — **economy cross-checked against published genre standards.** The potion design is the
  worst shape the literature names (constant sink vs linear source); the Discernment retune is
  "extend the fixed sink" rather than "make it scale" (now R6b); no prestige mechanic exists (now
  Owner decision 0b). Corrected my own error mid-write: the Gauntlet health ramp is piecewise LINEAR,
  not exponential — the exponential is the HP curve at 1.0493, which sits in the same band as the
  1.07 the literature quotes.
- `bcaba3c` — **partial index benchmarked: worth ~14 to 1.** Reads 1.662 ms -> 0.096 ms (440 of 466
  heap fetches eliminated); writes +10.9 us/row. First write benchmark was invalid — compared arms
  against different table states — and was redone with identical state per arm. Migration deliberately
  not written: that is R5d, now written in `643b926` and still awaiting owner application.
- `409a3f8` — **achievement sweep narrowed.** The completion sweep no longer fetches completed rows
  (new filtered repository method — `GetForPlayerAsync` untouched, the overview needs every row) and
  the loop is driven by the player's incomplete rows rather than all 466 definitions. Reduction
  measured rather than asserted; verified by neutering the filter, which fails all three sweep tests.
- `4790326` — **achievement pacing eval.** No dead stretches: the reward rate holds inside
  0.467-0.800 AP/clear across a 500x span of thresholds. 5,000 across the roster is 125,000 clears,
  2.5x the stated endgame — unreachable, as intended. Surfaced that extending the ladder moved 89.3%
  of all AP behind a 9x longer grind, and that the hot path got ~3x heavier (now R5b).
- `daa2801` — **potion economy eval sheet, and it answers the old R3.** Quest energy cost IS capped
  (`ChapterScalingCap = 16`), which reinstates the runaway the design doc warned about. Below the
  200-energy gate the cost cancels out of the refund ratio, so R = 1.025e-5 x pool and crosses 1.0 at
  level 13,092 regardless of which quest is played. Promoted to Owner decision 0 — the fix is a
  core-design call.
- `a67fbbe` — corrected a stale QuestService comment claiming reward steps were not transactional.
  They have been for some time; the note predated the mutation lock and told readers the opposite of
  the truth.
- `3adb93a` — **quest reward grant pinned.** Five cases. The neuter was the informative part: removing
  the mutation lock failed only the ATOMICITY tests and left the concurrency test passing, because
  the resource row's SELECT ... FOR UPDATE prevents overspend on its own. The two guards do different
  jobs, and removing the mutation lock on the reasoning that the row lock covers it would silently
  reintroduce charge-without-reward.
- `a9ad926` — **raid loot claim pinned.** Four cases against the real composition (advisory lock +
  conditional latch + grant on the same transaction). Verified against TWO separate neuters: dropping
  the latch condition failed three tests; making the loser commit rather than roll back failed the
  rollback test with 1099 gems where 100 was expected. Implementation was already correct.
- `216c985` — **gem double-spend pinned.** Five concurrency cases; verified against the real bug by
  neutering the advisory lock, which charged 11 spends against a 10-gem balance and took it to -1.
  The implementation was already correct — this closes the open defect as tested, not as argued.
- `b8191c3` — **CRITICAL: the API issued tokens it would then reject.** No `Jwt` section existed in
  appsettings.json, so issuer/audience were undefined; signing omitted them while validation still
  required them. Every login returned 200 and every authenticated request then 401'd, silently. Found
  by probing the running API, not by reading it.
- `15daf86` — security response headers (nosniff, DENY, no-referrer, Permissions-Policy, strict CSP
  outside Development), set early so they land on 401/429/403/500 too. Four pipeline-level tests,
  verified by neutering the registration.
- `a7abe05` — ForwardedHeaders now refuses to boot on an ambiguous config instead of warning.
- `docs/eval/SECURITY_AUDIT_2026-09-03.md` — the full audit, including what was NOT tested.
- `b5244b3` — **docs: potion economy rewritten to the shipped model.** Axes were documented
  backwards (Discernment→frequency); corrected to Discernment→tier, energy→frequency. All figures
  re-derived from the model with the arithmetic shown. Surfaced three things not previously written
  down: self-supply lands at D=1,808,205 rather than the 2M anchor (R settles at 1.025, so it crosses
  1.0 early); above level 13,420 R is level-INDEPENDENT because cost tracking pool makes
  MaxPool/QuestCost constant; below it the energy gate scales R down linearly, which is the
  "pushback not a wall" as one number (7.7% recovery at L1,000).
- `43dbb7f` — chore: Unity scratch dirs ignored in the backend repo after the editor was pointed at
  it by mistake and generated Library/, Temp/, UserSettings/, ProjectSettings/ beside the .NET source.
- `f59e481` — **balance: stretch every Discernment sink across the real endgame.** Crit saturation
  ×100 (chance 1k → 100k, damage 5k → 500k, caps unchanged); rare drops re-anchored on 1M with a hard
  10M ceiling (halfway 50k → 111,111 + `RareDropDiscernmentCap`); zone ladder 6 → 9 rungs to 5,000;
  new per-raid `RaidClears` ladder scoped by raid definition id; ladder ids re-keyed on threshold
  because `ItemRarity` stops at Orange permanently; `GetCritProfile` widened to `long`. 1143 unit
  tests pass, 0 warnings.
