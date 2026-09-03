# Evaluate later

Things built or deferred on purpose, with an open question attached. Not a backlog of work to do —
a list of calls to revisit once there is evidence to make them on. Each entry says what would settle it.

Delete an entry when it is decided, and record the decision in `DESIGN_DECISIONS.md` if it matters.

---

## Gauntlet stage jump — keep it? make it work live?

**Added:** 2026-08-26, at the owner's request, mock only.
**Where:** `GauntletDevTab` → "JUMP TO STAGE", backed by `MockRotaApi.SetMockGauntletStage`.

Drops a player straight onto a ladder stage so late-ladder behaviour is testable without clearing
179 stages to reach stage 180. Score and highest-cleared move with the jump, or the leaderboard would
contradict the position.

**Deliberately mock only.** The live dev surface is `grant` / `grant-item` / `refill`. A live version
would have to write `gauntlet_entries`, then re-run the rank snapshot so the standing agrees — and a
dev tool that writes competitive ladder state is a different risk class from one that grants gold.

**Open questions:**
1. Is this useful enough to keep at all once the Gauntlet is stable, or was it scaffolding?
2. If it stays, does it need to work against live? That means a new AdminOnly endpoint and an audit
   row per jump, because a ladder position that moved without being climbed must be explicable.
3. If it works live, does jumping DISQUALIFY the account from that run's prizes? A dev account sitting
   at stage 250 without climbing distorts every league it is in.

**What would settle it:** running one full Gauntlet season with the mock jump and seeing whether the
live gap actually got in the way.

---

## The 5000+ league still has no lore name

**Raised:** D-016. Still open.

The top league is "Ancient", which collides with the Ancient class at level 10,000. Config and
content-string change only; the owner picks the name.

---

## League band expansion

**Raised:** D-016. Trigger, not a date.

Open-ended 5000+ is fine while the population above 5,000 is thin. Higher bands (10000–24999,
25000+) get added when population justifies it — `GauntletConfig.LeagueBounds`, config only.

**What would settle it:** a league leaderboard where the top and bottom of the 5000+ band are no
longer comparable.

---

## 23 of 25 raids have no loot table

**Found:** 2026-08-28, while building the summon screen's loot preview.
**Where:** `content/raids.json` — every raid except `raid_ironcolossus` and `raid_malachar`
carries an **empty `lootTableId`**. `content/loot_tables.json` defines only those two raid tables
(the other five are quest tables).

**This is handled, not broken.** `RaidService` guards with
`if (!string.IsNullOrEmpty(definition.LootTableId))`, so those raids still grant their
`baseGoldReward` / `baseExperienceReward` / `baseGemReward` and the Rare/Participant contribution
multiplier still applies. What they do **not** grant is any item drop, and any threshold stat points —
`unassignedStatPoints`, attack/defense/discernment — since those live on the loot table's brackets.

So every zone Guardian, which is 23 of the 25 raids and the entire mid-game raid ladder, pays gold
and XP and nothing else.

**Open question:** is that the intended economy, or did the zone Guardians simply never get tables
written? The two that have one are both World bosses, which reads like the content phase started at
the top and stopped.

**Why it surfaced now:** the summon screen shows loot per contribution bracket. It offers that
disclosure only when the raid actually has a table, so today it appears on two bosses out of
twenty-five. The UI is correct either way — but if the answer is "they should have tables", the
screen will look far emptier than intended until they do.

**What would settle it:** an owner call on whether Guardians are meant to drop items at all. If yes,
it is a content-authoring task (23 tables), not a code one — nothing in the engine needs to change.

**Verified 2026-09-02.** Re-checked against the code, and the conclusion above holds: this is a
content gap, not a defect. `LootTableProvider.GetById` is a plain dictionary lookup that returns null
for an unknown key, and the loot pass in `DistributeKillRewardsAsync` short-circuits on
`!string.IsNullOrEmpty(definition.LootTableId)` BEFORE it is ever called. Nothing throws, nothing is
lost. An empty `lootTableId` is a supported state that the Gauntlet ladder stages rely on deliberately
— `RaidDefinitionProvider` documents exactly that.

The count is **23 of 25** on the committed content pack. (A later note quoting "26 of 28" was counting
three uncommitted raids alongside it.)

**What did turn up:** nothing validated the reverse direction. `LootTableProvider` checked loot tables
against raids but never raids against loot tables, so a *typo* in a `lootTableId` would have failed
silently — GetById returns null, the loot pass is skipped, and the raid pays gold and XP only, which
at runtime is indistinguishable from a raid designed to carry no loot. Two guardrails now close that:

- `LootTableProvider`'s constructor throws at startup on a dangling `lootTableId`. Empty is still
  accepted. Covered by `LootTableProviderValidationTests`.
- `tools/validate_content_pack.py` fails on a dangling reference and *reports* (without failing) how
  many raids carry no loot table, so the gap can never quietly grow again.

The owner design question is unchanged and still open: should zone Guardians drop items at all?

---

## Audit sweep 2026-09-02 — what was found, fixed, and deliberately left

Two read-only audits ran over the codebase: one for the "terminal moment with nothing running"
shape, one for currency spend/grant safety. Everything below was verified against the code by hand
afterwards, not taken on the auditor's word.

### Fixed

- **Gauntlet events never closed.** Same shape as the World-raid expiry defect and costlier: rank
  prizes stranded AND every future event blocked, because `OpenEventAsync` refuses while an Active
  event exists. Now swept by `GauntletEventSettlementService`.
- **Skill-point grants were silently lost.** `PlayerStats` has no concurrency token and the grant was
  a read-modify-write; raid loot claims serialize on the PARTICIPANT row while quests serialize on
  the PLAYER, so concurrent grants for one player overwrote each other. Now an atomic SQL increment.
- **Strike purchases could charge without delivering.** `BuyStrikesAsync` was missing the
  `IPlayerMutationLock.RunAsync` wrapper its sibling `BuyFromShopAsync` has. Now wrapped.
- **Guild creation could half-commit.** Three separate `SaveChanges` calls with no lock, and the
  final players write had no retry against its own xmin token — a failure left the player charged
  with `GuildId` unset, which the "already in a guild" gate reads. Now one transaction, and the gold
  goes through `TrySpendGoldAsync`.

### Checked and found CORRECT — do not re-file

- **Lapsed temporary bans and mutes feeding the moderator-authority gate.** Flagged as a possible
  staleness bug because `PunishmentLog.ExpiresAt` is written but never evaluated, and
  `FindActivePunishmentAsync` therefore reports a lapsed punishment as still in force. It never
  reaches a decision: both `UnbanPlayerAsync` and `UnmutePlayerAsync` gate on the DERIVED
  `target.IsBanned` / `target.IsMuted` first and return early, and the permanent-vs-temporary split
  reads `target.BannedUntil`, not the log. The derived-expiry design covers it.
- **Ordinary raids expiring un-killed.** Nothing is stranded: rewards are only computed on the
  killing hit, so an un-killed raid has no banked value to lose. Deliberately NOT settled — expiry is
  a failure, and failure pays nothing.
- **Claiming loot on a long-expired defeated raid.** `LootRaidAsync` never checks `ExpiresAt`. That
  is deliberate: a raid you helped kill stays claimable.

### Open, needing an owner decision rather than a code answer

0. **Can a Gauntlet gem bundle be bought more than once?** `BuyFromShopCoreAsync` builds
   `referenceId = $"gauntletshop:{playerId}:{entry.Id}"` — constant for the life of the account. The
   currency ledger returns `AlreadyCharged` for any repeat of a reference and the gem grant is deduped
   on the same string, so a player's SECOND purchase of `shop_gembundle_small` or
   `shop_strikerefill_medium` charges nothing, grants nothing, and returns SUCCESS. In effect each is
   a once-per-account purchase that reports otherwise.

   **This may well be intended.** `GauntletShopRewardKind` documents GemBundle as "repeatable" and the
   catalogue deliberately never marks these entries owned — but `GauntletShopIdempotencyTests` pins
   "buy-twice charges once" as a SPEC requirement of System 16 Slice 6, and its header calls the
   GemBundle repeatable while asserting exactly this behaviour. The two readings cannot both be right.

   A fix was drafted and REVERTED rather than shipped: it time-bucketed the reference for repeatable
   kinds, mirroring `ConsumableService` refills. It preserved every behaviour the existing tests check
   (two rapid buys still charge once) and failed them only on the literal reference string. It was
   backed out because changing a spec'd, test-pinned economy rule is an owner's call, not an
   overnight one.

   **What settles it:** should a player be able to buy a second gem bundle at all? If yes, the shape
   question follows — a time bucket like refills, a purchase counter, or a client-supplied
   idempotency key as `BuyStrikesAsync` already takes.


1. **`GuildJoinRequest.Expire()` has no callers, and there is no time field to drive it.** The entity
   doc comment promises an `Expired` terminal state; the enum has one; nothing reaches it. A `Pending`
   invite therefore lives forever, and because `FindPendingAsync` short-circuits re-invitation with a
   silent success, an officer re-inviting someone who ignored a stale invite gets nothing. No currency
   is at risk — this is a stuck row and a UX dead end. **The question is how long an invite should
   live**, which is a design call, not a defect fix.

2. **Should zone Guardians drop items at all?** Unchanged from the entry above. 23 of 25 raids carry
   no loot table.

---

## Second audit sweep 2026-09-02 — growth, and player lockout

Two more read-only audits: unbounded row growth, and whether a player can get stuck. Verified by
hand afterwards. What was fixed is in the commit log; what needs a decision is below.

### 1. A player who never invests in Energy hits a wall at Chapter 4, and cannot undo it

**This is the most player-facing thing found all session, and it lands squarely in beta.**

Max energy is purely investment-driven — `ComputeMaxEnergy() => BaseMaxEnergy + EnergyInvestment`,
with `BaseMaxEnergy = 25`. Level does NOT raise it; `GrantLevelUpPointsAsync` resyncs GuildStamina's
max and deliberately not Energy's.

Quest cost scales by chapter. Quest `c4z0b` costs `ceil(20 x 1.38) = 28` on Normal — the first node
in the chain to exceed a base pool of 25. Every restore path clamps to `MaxValue`, so gems, potions
and waiting are all useless. And stat allocation is ONE-WAY: `PlayerStats` exposes only
`AllocateTo*`, with no de-allocation anywhere. The only respec in the codebase is
`MasteryService.RespecAsync`, which respecs masteries, not stat points.

A new player who pours all 10 SP/level into Attack and Defence — a completely reasonable "I want to
hit harder" build — arrives at Chapter 4 unable to attempt the next quest, with no in-product action
that fixes it.

**It is a wall, not a tomb.** A zone-boss clear resets the zone (`ResetZoneAsync`), so Chapters 1-3
stay farmable indefinitely: XP, levels, new skill points, then Energy. The LSI cap aggravates it —
Stamina counts double, so a Stamina-heavy build is REJECTED when it later tries to allocate Energy
and must level further first.

**What settles it, and it is a design call:** a gem-priced stat respec mirroring the mastery one, or
a floor under `ComputeMaxEnergy` that grows with level. Either removes the trap; they imply very
different economies. There is currently no signposting either — nothing tells a player their build
is about to wall them.

### 2. Refresh tokens are never deleted

Rotation revokes in place (`IsRevoked = true`) and nothing ever removes a row. The 3-session cap
counts only live rows, so revoked and expired ones are invisible to it and accumulate forever. With
a 15-minute access token a connected client rotates roughly 96 times a day per session.

**Do not just add a purge.** Replay detection works by recognising a revoked token being presented
again (and revoking the family) — deleting history would silently disable it. The question is a
RETENTION WINDOW: how long after expiry is a token still worth keeping to catch a replay? Pick that,
and the purge follows.

### 3. Balances are a full SUM over the player's lifetime ledger, on hot GET paths

`GetBalanceAsync` for gems, strikes and Gauntlet currency each sum every row the player has ever
had. They are called from the player profile, from the Gauntlet overview GET (three ledger scans in
one handler), and from the raid hit path. Read cost per request therefore grows linearly with
account age, forever.

Correct, but not free. It will not bite in a closed beta; it is the sort of thing that is much
cheaper to fix before there is a year of ledger history. **The design question is whether to keep a
materialised balance column** alongside the append-only ledger — which has real money-safety
implications and so is not a change to make casually.

### 4. Idempotency ledgers grow per action with no bound

`mastery_activity_events` and `achievement_progress_events` write a row per distinct referenceId —
per raid kill, per settled event, multiplied by the number of achievement tiers on the metric. Rows
older than the raid they reference can never be needed for dedup again, and nothing prunes them.
Ordinary raid HITS do not write here, so this is per meaningful action, not per request. Low
urgency, same retention-window question as the refresh tokens.

**There is no purge, cron, or retention job anywhere in the repo** — established by absence across
the whole tree, not by failing to find a specific one. That is fine today. It is worth one decision
covering all four tables above rather than four separate ones later.

---

## Third audit sweep 2026-09-03 — inventory and duplication

### Fixed
- **One gear copy could be worn twice.** `EquipCommanderAsync` required only ownership, never a spare,
  while `EquipAsync` required a spare but counted only equipment rows. One Orange mount therefore
  fired a mount proc AND a commander proc in the same attack. Both sides now count both slots.
- **A bulk potion use destroyed the surplus.** Ten 50-point potions at 90/100 restored 10 and burned
  all ten. Now consumes only what the pool can absorb.

### Checked and found CORRECT — do not re-file
- **The craft-loop exploit does not exist in shipped content.** The hypothesis was: farm a
  re-droppable Unit, craft it into stacking Gear, farm it again, repeat for unbounded gear. Of the
  five recipes, four output Unit/Legion — own-once, and `CraftAsync` refuses an already-owned output
  BEFORE charging gold or consuming ingredients. The only Gear-output recipe, `craft_oathsteel_helm`,
  consumes Gear + Item, not a Unit. There is no recipe turning a re-farmable collectible into a
  stacking output.
- **No stored stat bonuses anywhere.** Effective attack/defence and legion power are recomputed from
  the live definitions on every read, so the "unequip subtracts a value content has since changed,
  and the stat drifts" failure mode is structurally impossible.
- **Crafting is atomic and re-verifies under the lock** — ingredients checked inside
  `IPlayerMutationLock`, gold via conditional update, consume before grant, all one transaction.

---

## Fourth audit sweep 2026-09-03 — config defaults and formulas

The audit that previously caught `XpExponent` shipping 0.8 while defaulting to 0.7, re-run over
every config class and every formula.

**Good news first: there are ZERO accidental default-vs-appsettings value disagreements.** Every
scalar appsettings sets carries an identical C# default, `XpExponent` included — bug (b) has not
regressed. The three differences that exist are deliberate and documented in-code.

### Fixed
- **Six config tables defaulted to EMPTY** while appsettings ships the whole thing, so a binding
  failure degraded silently instead of throwing. Now `ValidateOnStart`.
- **The Gauntlet curve tests exercised the unramped curve**, including the one assertion guarding
  overflow headroom. Fixture now mirrors appsettings.
- **Flat conditional bonuses truncated toward zero** per bonus, compounding the shortfall. Now rounds.

### Open — owner decisions

1. **T76's late ramp moved the natural endgame frontier from stage 250 to about 213.** On the
   unramped curve an 80M-power player reached exactly stage 250, which is where the "250 is the
   natural frontier" design note came from. Under the shipped ramp the last ~37 stages are gem-pushed
   territory rather than natural progress. That may be exactly what the ramp was for — it contradicts
   the older note, so it is now pinned in `GauntletCurveTests` rather than left to drift. Retune, or
   update the design note.

2. **`AchievementConfig` has no appsettings section at all.** Its `ZoneRerunLadder` default IS the
   shipped curve, so nothing is broken — but `Program.cs` binds it against a section that does not
   exist, and the in-code comment calls the curve owner-tunable. It is currently code-locked. Add the
   section if it is meant to be tunable.

3. **Roughly twenty scalars are untunable without a code change** — most of `CombatConfig`'s Gauntlet
   health knobs and several `QuestConfig` drop rates — despite `CombatConfig`'s own comment saying
   "All values are appsettings-overridable for tuning." Their defaults are the shipped values, so
   nothing is wrong today; the comment is just no longer true.

4. **`RaidRewards.UnassignedStatPointsGranted` is still `int`** while its sibling
   `RaidParticipant.StatPointsEarned` was widened to `long` by the int32 audit. The upstream
   accumulator is `int` too and no content configuration reaches 2^31, so this is a leftover from the
   widening pass rather than a live overflow. Left alone deliberately: the Unity client mirrors both
   widths exactly, so changing it needs a coordinated two-repo change for an unreachable case.
