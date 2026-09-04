# The opening — guided levels 1 to 3

Status: **design, not built.** The existing `TutorialOverlay` (client, `Runtime/UI/`) is a passive
tap-through slideshow and would be replaced, not extended.

Owner intent (2026-09-03, extended 2026-09-04): hard-lock the first three levels. The player must
click the TABS themselves to advance, so the lock teaches navigation rather than narrating it. It
should cover questing, levelling, allocation, potions, sigils, raids and summoning — and it should
explain what Discernment does **without numbers**, because the community should have something left
to work out.

---

## Corrections to the brief, from the code

Three things in the spec do not match what ships. Worth fixing here rather than shipping a tutorial
that lies — a first-hour player checks these directly.

**The sigil rerun chance is FLAT, not scaled by cost.** `QuestConfig.SigilRerunDropChance = 0.15`,
and the comment is explicit that it is "NOT Discernment-scaled and NOT the per-boss SigilDropChance".
The true rule: **first clear at a difficulty is guaranteed, every later clear is a flat 15%.**

**The sigil matches the DIFFICULTY, not a tier.** Each boss carries a four-entry map —
`sigil_c1z1b_normal / _hard / _legendary / _nightmare`. Clear on Hard, get the Hard sigil.

**Only the zone's FINAL boss drops sigils** — 25 of 139 nodes. Mid-zone nodes never do, by design, so
"defeating main quest bosses" is right but narrower than it sounds.

**A convenient accident.** Chapter 1 zone 0's final boss is `q003` *The Iron Colossus Awakens*, and
the first clear already guarantees a sigil at 100%. **The tutorial does not need to force the sigil —
only to point at it.** Just the potion has to be granted specially.

**Potion tiers do not exist yet.** The shipped consumables are flat-restore: `potion_energy_minor`
(25) and `potion_energy_major` (60). There is no middle-of-five to give. Use the **major** draught
now; when the percentage tiers land it becomes the 7.5% one.

---

## What the numbers force

**Level 1 → 2 is four clicks.** `q001` costs 5 energy and pays 1.5 XP per energy. The roll
(`ResourceReward.RollSummed`) rounds away from zero, so 7.5 becomes **8 XP an attempt**, not 7.5.
TNL(1) is 30 — three attempts pay 24 and fall short, four pay 32 and clear it. Four, against a
25-energy pool that affords five, so the player levels one click before running dry without being
told to stop.

*(An earlier version of this line read "4 × 7.5 XP = 30 = TNL(1)" and called it exact. The answer
was right and the derivation was not: the grant is 8, and four of them overshoot 30 by 2. Pinned in
`TutorialFourPassesTests`, which derives the number from the same config and the same roll the
server uses — retuning the energy cost, the XP rate or the level curve now fails the build rather
than the player's first four clicks.)*

**Each level grants exactly 10 skill points**, so "+10 into Energy" is one level's whole grant.

**The LSI cap does not bite until level 4.** With no stamina the ceiling is `7.45 × level`, so the
tenth point is refused at level 4 — *after* the tutorial ends. That is good: the tutorial never has to
teach a rule, and the refusal message (rewritten in `8e94cf4`) is the teacher when it arrives.

---

## The sequence

Ten beats. **Every one advances on a real click of a real tab or button** — never a "Next", never a
tap-through. The lock is the teaching device.

| # | fires when | the player must | releases |
|---|---|---|---|
| 1 | first login | click **QUEST** | on arrival |
| 2 | quest screen | attempt `q001` | after four attempts (exactly one level) |
| 3 | level 2 | click **PROFILE** | on arrival |
| 4 | profile | put **+10 into Energy** | on allocation |
| 5 | back to quest | clear the rest of zone 0 | on defeating `q003` |
| 6 | `q003` defeated | — (sigil + potion land) | on acknowledging |
| 7 | after the drop | click **PROFILE** → stats | on arrival |
| 8 | stat page | — (Discernment, Attack, Defence) | on acknowledging |
| 9 | after stats | click **RAIDS** | on arrival |
| 10 | raid screen | click **SUMMON** | everything releases |

Beats 2 and 5 must let the player simply play — lock the first action, then release until the next
milestone. A lock that fires on every click is a cage.

**Rules for the lock.** A dim skip stays reachable at every beat and is remembered. Beat state lives
**server-side against the account** — the current overlay uses `PlayerPrefs`, which loses itself on
reinstall and does not follow the player to another device. Never lock a target that is off screen or
not yet rendered; that is a soft-lock with no way out. The highlight is a cut-out, not a tint.

---

## The copy

House voice from the lore bible: declarative, unhurried, no exclamation. *"The Ancients are not
dormant, not dead, and not absent. They are listening."* Grave and plain, never ornamental. **No
archaism for its own sake** — no *thou*, no *hark*, no inverted syntax. Old style here means
restraint, not costume.

The current overlay is the counter-example: *"The QUEST tab is your main XP and energy sink"*, *"Good
hunting!"* That is a manual talking about a database.

---

**1 — first login**

> The Old Guard held this ground for four hundred years.
> They are not holding it now.
>
> Go and see what took it.

*locks: the QUEST tab*

**2 — quest screen**

> Every road costs something to walk. Yours costs energy.
> You have twenty-five. The ruins ask five.
>
> Four passes will teach you more than any of it will kill you.

*locks: `q001`, the attempt button. Releases after four attempts.*

**3 — level 2**

> Something in you has settled.
> The Watch calls it a level. It is only the body catching up to the work.

*locks: the PROFILE tab*

**4 — profile, allocation**

> Ten points. Spend them on Energy.
>
> Not because Energy is the best of them — because the others are useless to a man
> who cannot reach the fight.

*locks: the Energy row and the +10 control*

**5 — returning to quest**

> Thirty-five now. It will not feel like more for long.
>
> The ruins run deeper than the gate. Walk them to the end.

*locks: nothing. Free play until `q003` is defeated.*

**6 — the Iron Colossus falls**

> It does not die so much as stop.
>
> Two things come off it. The draught is what the road gives back — the quests keep
> you walking, if you walk enough of them. The other is a sigil, and a sigil is a
> name. Speak it and the thing it names comes to you.
>
> The first kill always yields one. After that the ruins are less generous.

*grants: `potion_energy_major`. The sigil is already guaranteed by the first-clear rule.*
*locks: the PROFILE tab*

**7 — stat page**

> Attack is how hard you land. Defence is how little it costs you when something
> lands back — that reckoning is coming, and you will want to have paid for it.
>
> Discernment is the quiet one. The world does not give more to the discerning.
> It gives *better*. How much better is not written down anywhere, and the Watch
> has been arguing about it for two hundred years.

*locks: acknowledge, then the RAIDS tab*

**8 — raids**

> A quest is a road. A raid is a thing at the end of one, and it does not fall to a
> single sword.
>
> Stamina buys your blows. Everyone who strikes it takes a share of what it was
> carrying, and the share is measured in damage done. You are not required to land
> the last hit. You are required to have been there.

*locks: the SUMMON tab*

**9 — summoning**

> The sigil you took from the Colossus is not a trophy. It is a summons.
>
> Spend it and the thing returns, on ground of your choosing, at the strength you
> beat it at. Harder ground yields a harder name. What you do with the company you
> keep is your own business.

*locks: nothing — tutorial ends*

---

## Notes on the copy

**Beat 4 gives a reason, not an instruction.** "Energy is best" would be a lie — every E:S split
levels at exactly the same rate (`StatParityTests`). What is true is narrower: a level-1 character has
no raids to spend stamina on, so energy is the only pool that does anything yet.

**Beat 6 states the first-clear rule and implies the rest.** *"After that the ruins are less
generous"* is true (a flat 15%) without printing a number, which is the brief. It also introduces
potions-from-questing in one clause rather than a paragraph.

**Beat 7 refuses to quantify Discernment on purpose.** *"It gives better"* is exactly what the rare
drop curve does. The Watch arguing for two hundred years is the invitation to work it out — the
community gets the curve, not the tutorial.

**Beat 7's Defence line is future tense** because Defence does almost nothing today: mitigation is
Gauntlet-only, capped at 800 Defence, and health does not gate a hit. *"That reckoning is coming"* is
honest about intent without claiming a mechanic that is not there. **This line has a known expiry —
revisit it when raids deal damage.** It is also where a strike-based PvP line would attach later.

**Beat 8 does not mention Defence or health**, because in a raid today neither matters. Adding them
would make the tutorial wrong in a way a player can check inside an hour.

---

## Dependencies before this can ship

1. **A grant hook on `q003` first clear** for the tutorial potion. The sigil needs nothing.
2. **Server-side tutorial beat state**, keyed to the account. Schema change.
3. **`potion_energy_major` as the reward** until percentage potions exist; then re-point at the 7.5%
   tier.
4. **Beat 5 assumes zone 0 is three nodes.** It is (`q001`, `q002`, `q003`). Adding a node to that
   zone silently changes how long beat 5 runs.

## Open questions

1. **Is skipping permanent?** A player who skips at beat 1 sees no menus and meets the LSI cap cold.
2. ~~**Does beat 2's "four passes" survive a retune?**~~ **Answered.** `TutorialFourPassesTests`
   derives the number from the shipped config and the real roll, so a retune fails the build.
3. **Should beat 9 require actually spending the sigil?** Written as show-and-release. Forcing the
   summon costs the player their guaranteed sigil on a raid they cannot yet fight well.
