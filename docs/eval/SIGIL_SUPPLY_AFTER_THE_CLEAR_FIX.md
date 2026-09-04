# What the sigil fix did to raid access

**The fix in `5ababaa` was correct and it cut the raid pillar's fuel supply by 40×.** Both halves of
that sentence matter. Sigils were dropping per boss ATTEMPT rather than per CLEAR, and a boss takes 40
attempts, so the 15% rerun chance was firing 40 times a clear. Gating it on the clear was right. But
`SigilRerunDropChance = 0.15` was the number that survived the fix, and it had been living inside a
40× multiplier nobody had priced.

Nothing here is a defect in the fix. It is the tuning question the fix exposed, and it is an owner
call, so it is written up rather than acted on.

---

## 1. The supply, before and after

A boss node depletes 2.5 from 100, so a clear is 40 attempts.

```
EXPECTED SIGILS PER ZONE-BOSS CLEAR
  before 5ababaa (rolled every attempt): 40 x 0.15 = 6.00
  after  5ababaa (rolls on the clear)  : 0.15
  supply cut: 40x
```

The old behaviour also dropped at least one sigil 99.85% of the time (`1 - 0.85^40`), so in practice a
clear was a guaranteed sigil plus five spares.

## 2. What a sigil now costs, in energy

A rerun sigil requires re-running the whole zone — the zone-boss gate gives no shortcut — then clearing
the boss again, at 15%. Zone re-run costs are computed from shipped content: node count × attempts to
clear × `ComputeEnergyCost` (base × difficulty × chapter scaling × zone ramp).

```
zone                        zone re-run       before        after
c1z1 Ashen Causeway                 760          127        5,067
c3z0 Keepwall                     1,920          320       12,800
c6z4 Throne of Ancients           6,240        1,040       41,600
```

## 3. What that is in days

Regen is flat, so energy per day is a constant per class: `1440 / RegenMinutesPerPoint`.

```
  Conscript          596 energy/day   (2.4167 min per point)
  Arcanist           909 energy/day   (1.5833 min per point)
  HighArcanist     1,234 energy/day   (1.1667 min per point)

DAYS PER SIGIL after the fix
zone                          Conscript     Arcanist   HighArcanist
c1z1 Ashen Causeway                  9d           6d             4d
c3z0 Keepwall                       21d          14d            10d
c6z4 Throne of Ancients             70d          46d            34d
```

**A chapter-6 raid summon is between 34 and 70 days of banked energy.** The first raid in the game is
four to nine days. That is the steady state, and it is the number that decides how often the raid half
of the game happens at all.

## 4. Why nothing else covers it

- **The first clear per difficulty is guaranteed.** 26 zone bosses × 4 difficulties = **104 sigils**
  handed out across the campaign. That is the early supply, it is generous, and it is one-off. Once a
  player has cleared everything at every difficulty, §3 is the whole of it.
- **Guild sigils are a different thing.** `GuildCurrency.Sigil` is a ledger currency that funds
  *guild-raid* summons from the guild pool. It is not an `ItemType.Sigil` and cannot summon a personal
  raid. `DailySigilClaimAmount = 1` does not feed this economy.
- **Nothing else drops one.** No shop sells sigils, no raid drops one, and the loot audit
  (`DROP_CURVE_COVERAGE_AUDIT.md`) found no sigil in any loot table — the `Sigils` map on a zone boss
  is the only source in the game.

## 5. The thing this makes moot

Before pricing sigils, the obvious worry about the raid reward curve was gems-per-stamina. Gem rewards
are banded flat per chapter (2 / 5 / 12 / 28 / 64 / 140) while HP rises 32.6% every raid, so within a
band the first raid strictly dominates and only **8 of 26** raids are ever the best gem farm at any
player power. Gold and XP are fine — they rise 10-15% within a band, and 24 of 26 raids are optimal
somewhere.

That banding barely matters, because **stamina is not the binding constraint — sigils are.** A clear
costs a handful of stamina; the sigil that permits it costs weeks. The metric a player actually
optimises is gems per SIGIL, and there deep content wins by 70× (140 against 2), which is the right
direction. The gem banding is worth a look eventually; it is not the problem.

## 6. Options, priced

**A — leave it.** Raids become a monthly event at depth. Defensible if raids are meant to be
punctuation rather than a loop, but it is a large change from what the game did last week, and it was
never chosen.

**B — raise `SigilRerunDropChance`.** One config value. To restore the old *effective* supply you
would need 6.0 sigils per clear, which a single roll cannot express. What a single roll can do:

```
  rate    sigils/clear   c6z4 energy per sigil   days (Conscript)
  0.15           0.15                   41,600                70d
  0.30           0.30                   20,800                35d
  0.50           0.50                   12,480                21d
  1.00           1.00                    6,240                10d
```

**C — grant N sigils on the clear instead of rolling.** Removes the variance, which matters more here
than usual: at 15% the median player waits far longer than the mean suggests, and a 70-day mean with a
geometric distribution has a long and demoralising tail. `1.00` above is this option with N = 1.

**D — cut what a rerun costs rather than what it pays.** The 40-attempt boss (`BossDepletionPerAttempt
= 2.5`) is most of the zone re-run bill. Halving it to 5.0 halves the energy per sigil without touching
the drop rate. This also shortens every boss fight, which is a broader change than it looks.

**B or C is one line; D changes how a boss feels.** All four are owner calls — the raid cadence is a
core pacing decision, not a defect to patch.

---

## What I did not check

- Whether the **guaranteed 104** first-clear sigils are enough to carry a player through the campaign
  before the steady state bites. That needs a play-rate assumption, which is the same thing R10 is
  waiting on.
- The **Nightmare** picture. Every number above is Normal. Nightmare costs 3× the energy per attempt
  for the same 15%, so a Nightmare sigil is roughly 3× the figures in §3 — worth confirming before
  anyone tunes against this page.
- Whether raid **stamina** costs are sane, which is the question everyone asks first and which §5 shows
  is the wrong one.
