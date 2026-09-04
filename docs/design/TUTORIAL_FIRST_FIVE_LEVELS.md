# The first five levels — guided opening

Status: **design, not built.** The existing `TutorialOverlay` (client, `Runtime/UI/`) is a passive
tap-through slideshow and would be replaced, not extended.

Owner intent (2026-09-03): the opening levels semi-hard-locked to a fixed sequence — quest, level,
put the skill points into Energy — with the target highlighted on screen and the rest of the UI
inert. Text should carry the world's voice: sparse, old-styled, no whimsy.

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

Seven beats across five levels. Each locks input to one target, dims the rest, and advances only on
the real action — never on a tap-through.

| # | trigger | locked target | unlocks |
|---|---|---|---|
| 1 | first login | QUEST tab | — |
| 2 | quest screen open | `q001`, the attempt button | four attempts |
| 3 | level 2 reached | PROFILE tab | — |
| 4 | profile open | Energy, +10 | allocation confirmed |
| 5 | back to quest | attempt button | free until level 3 |
| 6 | level 3 → 4, cap hit | Stamina row | the leftover point |
| 7 | level 5 | RAIDS tab | everything |

Beats 2 and 5 must allow the player to simply *play* — a lock that fires on every single click is a
cage. Lock the first action of a beat, then release until the next milestone.

**Rules for the lock.** It must never trap: a dim "skip" affordance stays reachable at every beat, and
skipping is remembered. It must survive a reload — beat state on the server, keyed to the account,
not `PlayerPrefs` (the current overlay uses `rota_tutorial_done_v1` in prefs, which loses itself on
any reinstall). And it must never lock a target that is off screen or not yet loaded.

## The copy

The house voice, taken from the lore bible: declarative, unhurried, no exclamation. "The Ancients are
not dormant, not dead, and not absent. They are listening." That is the register — grave and plain,
never ornamental. **No archaism for its own sake** — no *thou*, no *hark*, no inverted syntax. Old
style here means restraint, not costume.

The current overlay is the opposite and should be read as the counter-example: *"The QUEST tab is your
main XP and energy sink"*, *"Good hunting!"* That is a manual talking about a database.

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

**5 — returning to quest**

> Thirty-five now. It will not feel like more for long.
>
> Go back. The ruins have not moved.

*locks: nothing. Free play until level 3.*

**6 — level 4, the cap refuses the tenth point**

> One point will not go in.
>
> The body has a ceiling, and you have found yours. Energy carries you to the fight.
> Stamina is what you have left when you arrive. Neither is free of the other.
>
> Put the last one into Stamina. You will need it before you want it.

*locks: the Stamina row*

**7 — level 5**

> Enough. You know where things are.
>
> There are older things than the Old Guard, and they do not fall to one sword.
> When you are ready, look at the raids.

*locks: the RAIDS tab, then releases everything*

---

## Notes on the copy

Beat 4 gives a **reason**, not an instruction. "Energy is best" would be a lie — the pacing eval shows
every E:S split levels at exactly the same rate. What is true is narrower: a level-1 character has no
raids to spend stamina on, so energy is the only pool that does anything yet. The line says that
without saying "sink" or "pool".

Beat 6 is the one that earns the whole sequence. It fires on a refusal the player just experienced,
so it explains a rule they already met rather than pre-loading one they have not.

Beat 2's "four passes" is exact, not approximate — 4 × 7.5 XP = 30 = TNL(1). If quest costs or XP
rates are ever retuned, **this line becomes a lie**, and it is the kind of lie players notice
immediately. Either derive the number at runtime or pin it with a test. → **R11**.

## Open questions

1. **Does beat 6 fire if the player split their points differently?** The sequence assumes pure
   Energy through level 3. A player who ignores beat 4 will hit the cap at a different level, or not
   at all. Either enforce the allocation (a true lock) or make beat 6 trigger on the *cap refusal
   event* rather than on level 4.
2. **Is skipping allowed to be permanent?** A player who skips at beat 1 never learns the LSI cap
   from the game and will meet it as a bug report instead.
3. **Where does beat state live?** Server-side against the account is the only version that survives
   a reinstall, but it is a schema change.
4. **Is the seven-beat length right?** Owner said "first 5 or so levels". Beats 1–4 are the
   fundamentals; 5–7 could ship later without leaving the opening incoherent.
