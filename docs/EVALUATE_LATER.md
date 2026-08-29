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
