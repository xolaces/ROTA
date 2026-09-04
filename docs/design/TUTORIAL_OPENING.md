# The opening — guided levels 1 to 3

Status: **design, not built.** The existing `TutorialOverlay` (client, `Runtime/UI/`) is a passive
tap-through slideshow and would be replaced, not extended.

Owner intent (2026-09-03, revised): hard-lock **levels 1 to 3 only**. The point is to walk the player
through the MENUS — they must click the tabs themselves to advance, so the lock teaches navigation
rather than narrating it. After level 3 the game is open. Text carries the world's voice: sparse,
old-styled, no whimsy.

> **Revised from an earlier five-level draft.** Shortening to three changes the shape for the better:
> the LSI cap does not refuse a point until level 4, so the cap is now something the player meets
> AFTER the tutorial rather than inside it. That removes the one beat that needed a conditional, and
> the tutorial no longer has to teach a rule — it only has to show where things live.

---

## What the numbers force

Three facts from the shipped config decide the shape. None of them were designed for a tutorial;
they happen to be well suited to one.

**Level 1 → 2 is exactly four clicks.** `q001` "Ruins of the Old Guard" costs 5 energy and pays
1.5 XP per energy = 7.5 XP. TNL(1) is 30. Four attempts, exactly. A fresh pool of 25 affords five —
so the player levels one click before running dry, without ever being told to stop.

**Each level grants exactly 10 skill points.** So "+10 into Energy" is precisely one level's whole
grant. Nothing is left over and nothing needs explaining.

**The LSI cap bites at level 4, on its own.** `(Energy + Stamina × 2) / level ≤ 7.45`, so with no
stamina the ceiling on Energy investment is `7.45 × level`:

| level | max +Energy this level | skill points left stranded |
|---|---|---|
| 2 | +10 | 0 |
| 3 | +10 | 0 |
| **4** | **+9** | **1** |
| 5 | +8 | 3 |

**This is the tutorial's best moment and it is free.** At level 4 the instruction "put it all in
Energy" stops being possible. The game does not have to explain the cap — the player runs into it
with one point in hand and asks why. That is the cue to introduce Stamina, and it lands because they
felt it first.

A tutorial that scripts "+10 Energy" for five levels would be **wrong at level 4** and would strand
points it told the player to spend.

**Pools stop covering a level from level 3.** L2→L3 needs 34.7 energy against a 35 pool — it just
fits. L3→L4 needs 48 against 45, so the player waits ~15 minutes for regen. That is the first time
the game asks for patience, and it should be named rather than hidden.

## The sequence

Five beats across three levels. **Every beat advances on a real click of a real tab** — never on a
tap-through, and never on a synthetic "Next" button. The lock is the teaching device: the player
learns where a screen lives by being made to go there.

| # | fires when | the player must click | releases |
|---|---|---|---|
| 1 | first login | **QUEST** tab | on arrival at the quest screen |
| 2 | quest screen open | the attempt button on `q001` | after four attempts (exactly one level) |
| 3 | level 2 reached | **PROFILE** tab | on arrival at the profile |
| 4 | profile open | Energy **+10** | on allocation |
| 5 | level 3 reached | **RAIDS** tab | everything — tutorial ends |

Beat 5 exists only to show the tab. The player is not asked to run a raid at level 3 — they have five
stamina and nothing to spend it on. It ends by pointing at what comes next, then gets out of the way.

**Rules for the lock**

- **A dim skip stays reachable at every beat**, and skipping is remembered.
- **Beat state lives on the server, keyed to the account.** The current overlay stores
  `rota_tutorial_done_v1` in `PlayerPrefs`, which loses itself on any reinstall and does not follow the
  player to another device.
- **Never lock a target that is off screen or not yet loaded** — a lock on an element that has not
  rendered is a soft-lock with no way out.
- **The highlight must be a cut-out, not a tint.** Dimming everything except the target reads as
  "this is the only thing that works", which is exactly what is true.

## The copy

The house voice, taken from the lore bible: declarative, unhurried, no exclamation. "The Ancients are
not dormant, not dead, and not absent. They are listening." That is the register — grave and plain,
never ornamental. **No archaism for its own sake** — no *thou*, no *hark*, no inverted syntax. Old
style here means restraint, not costume.

The current overlay is the counter-example and should be read as one: *"The QUEST tab is your main XP
and energy sink"*, *"Good hunting!"* That is a manual talking about a database.

---

**1 — on first login**

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

**3 — on reaching level 2**

> Something in you has settled.
> The Watch calls it a level. It is only the body catching up to the work.

*locks: the PROFILE tab*

**4 — profile, allocation**

> Ten points. Spend them on Energy.
>
> Not because Energy is the best of them — because the others are useless to a man
> who cannot reach the fight.

*locks: the Energy row and the +10 control*

**5 — on reaching level 3**

> Enough. You know where things are.
>
> There are older things than the Old Guard, and they do not fall to one sword.
> Look, and then go back to work.

*locks: the RAIDS tab, then releases everything*

---

## Notes on the copy

Beat 4 gives a **reason**, not an instruction. "Energy is best" would be a lie — the pacing eval shows
every E:S split levels at exactly the same rate. What is true is narrower: a level-1 character has no
raids to spend stamina on, so energy is the only pool that does anything yet. The line says that
without saying "sink" or "pool".

Beat 5 deliberately does not ask for anything. A tutorial that ends on a task the player cannot
complete — a raid, at level 3, with five stamina — ends on a failure. This one ends on a door.

Beat 2's "four passes" is exact, not approximate — 4 × 7.5 XP = 30 = TNL(1). If quest costs or XP
rates are ever retuned, **this line becomes a lie**, and it is the kind of lie players notice
immediately. Either derive the number at runtime or pin it with a test. → **R11**.

## What the player meets AFTER the tutorial, unguided

Ending at level 3 means the LSI cap is now a post-tutorial encounter. At level 4 the cap refuses the
tenth point (ceiling `7.45 x 4 = 29`, investment already 20 from beats 4 and the level-3 grant), and
the player meets a refusal with no script running.

That is a reasonable place to leave it — the refusal message is the teacher — but **the refusal text
had better be good**, because it is now the only explanation the player gets. Worth checking that the
current message (`"Allocation would exceed LSI cap of 7.45. Current LSI: ..."`) says something a
player can act on. It currently does not; it states a number and a threshold with no advice.
→ **R12**.

## Open questions

1. **Is skipping permanent?** A player who skips at beat 1 sees none of the menus and meets the LSI
   cap cold.
2. **Where does beat state live?** Server-side against the account is the only version surviving a
   reinstall, but it is a schema change.
3. **Does beat 2 hold if quest costs are retuned?** "Four passes" is exact — 4 x 7.5 XP = 30 = TNL(1).
   Retuning `q001`'s cost or the XP rate makes the line a lie. Derive it at runtime, or pin it. → R11.
