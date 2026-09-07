# Wave 2 — lore audit, and the next two updates

Written after the 2026-09-07 content night: **+64 gear in 8 sets, +29 items, +24 magics, +26 units,
+6 legions, +18 recipes, +772 drop entries, and the raid-tag system underneath all of it.**

Two jobs here. First, hold the new content against the canon's own stated rules and report what it
fails. Second, speculate the next two updates far enough that they can be built without re-deciding
anything.

---

## Part 1 — The lore audit

Checked against **Master Canon XX, "Principles Carried Forward"**, which is the only place the rules
are written down.

### The four principles, and how wave 2 sits against them

**"Mystery items must stay mysterious." — PASSES.**
The Cold Token and the Unworn Crown are the two the canon names, with an explicit instruction: *do
not, in any future content, let this token translate the script.* Nothing in wave 2 translates,
deciphers or explains either. The new Pano general goes near the thread and deliberately stops:
*"The banner came back. Nobody has ever explained the rest of it, and the Watch has stopped asking
in writing."* That keeps the question warm without spending it.

**"Not everything is Old Guard, and not everything is Ancient." — PASSES, comfortably.**
Measured rather than asserted: **24 of 371** content entries reference the Old Guard, the Ancients,
the Sundering or Essence. **6%.** The canon's own example set runs three-in-ten. Most of wave 2 is
Iron Weir frontier issue, Last Watch grave-work, hunting gear and swamp reagents — the world being
bigger than its heroes, which is what the principle asks for.

**"Mundane drops are load-bearing." — PASSES, but thin.**
The economy's floor is **5 Grey/White materials** against 43 materials total: Iron Shard, Arcane
Dust, Causeway Ash, Hollow Reed, Mire-Ichor. That floor still exists and still works, but wave 2
added almost nothing to it — every new reagent landed Green or above, because tag reagents are
defined by having fought something. **This is the one real finding.** See the fix in Part 2.

**"Corruption is renewable; Heralds are not." — PASSES.**
The Shadow tag is the Glutbound overlay and is on ten raids, which is correct: the amber-rot layer
is *supposed* to run forever. No new content promotes anything to Herald. Vaskarr the Bargained is
explicitly *bound by an agreement*, not risen — a Swollen under contract, which is Order II and not a
crossing.

**"Tone holds: dark, mythic, grave. No item winks except Gravewend." — DELIBERATELY BROKEN, ONCE.**
The Stoned Devil set and its eleven associated entries are the owner's requested farewell to Dawn's
Drunken Angel. The house rule now has exactly one sanctioned exception, and it is written to survive
it: the archdevil is **sincere**, and the game around him stays entirely straight-faced. He filed his
horns down because they caught on doorframes. The Choir has written four papers on what else he might
have meant. That is a joke the world tells about him, not a joke the world tells about itself.

### A false positive, reported because I generated it

My automated tone sweep flagged `gear_unworn_crown` as a "wink". It is not. The regex searched for
`epic` as a substring and matched **"de-pic-ts"** in *"made for a head no carving depicts"*. The
sweep found nothing real; I am recording the miss so nobody re-runs it and trusts the hit.

### What I did NOT check

- **Whether the eight new sets read as eight distinct voices** when seen together in a UI. On the
  page they do; at icon size with a name and two numbers, Marchwatch and Gravewarden may be closer
  than intended. That is an art-pass question.
- **Faction population balance.** Iron Weir and the Last Watch got the most new material; the
  Threnody Houses and the Dawnward Choir got one set each. Whether that matches their intended
  weight in the world is an owner call, not a canon violation.
- **Name collisions with Dawn of the Dragons.** Everything here is original or built from ROTA's own
  canon, but I have not cross-checked against DotD's item list, and the Stoned Devil is by design an
  *answer* to one of theirs rather than a copy of it.

---

## Part 2 — Two content updates, speculated

Both are built from what already exists. Neither needs a new system, and each names the one mechanic
it would light up.

### Update A — *The Warrens Answer* · the Broods organise

**The hook.** Goblins are the game's floor enemy and the canon is explicit that Order III has "no
cosmological significance whatsoever — which is precisely why they matter." So the first update does
not escalate to a god. It escalates to *competence*: something in the warrens has started giving
orders, and the Broods have begun holding a line.

**What it ships.**

| | |
|---|---|
| **The mechanic it lights up** | **Gear set bonuses.** `SetId` shipped in wave 2 with nothing reading it. This is the update that gives all 13 sets a two-piece and a four-piece, and the natural bonus is a **tag affinity on gear**, mirroring what legions already do. |
| New sets | 2 — a Goblin-warlord set (Blue) and an Iron Weir counter-set (Purple) |
| New raids | 3, all Goblin-tagged, one of them a **Goblin + Legion** dual-tag: the first time the Broods field a formation |
| New tag | **none.** Deliberately — an update that adds a tag before the existing ten are exercised is spending headroom it has not earned |
| Reagent work | **The mundane floor.** 4 new Grey/White materials, which is the Part 1 finding fixed rather than noted |

**Why this one first.** Set bonuses are the largest piece of already-paid-for design sitting unused,
and the Goblin tag is the one a player meets earliest and therefore the cheapest place to teach the
whole counter-play idea.

### Update B — *The Quiet Wind* · the Black Archive opens

**The hook.** Chapter 7 exists as three nodes and three raids and almost no fiction. The Archive is
where the Last Watch keeps what the world decided to forget, and the Threnody Houses want it. This is
the faction-conflict update: two organisations that both believe they are the correct custodians of
the same building.

**What it ships.**

| | |
|---|---|
| **The mechanic it lights up** | **The player market.** It is built, pen-tested 33/33 and switched off pending Owner decision 0f. An Archive update is the right fiction for it: the Houses trade in what they know, and a market that opens *because a faction opened it* is better than one that appears because a flag flipped. |
| New sets | 2 — a Threnody Houses set (Purple) and a Last Watch archive set (Orange) |
| New raids | 4, Shadow and Horror tagged, plus **the first Dragon-tagged raid outside the Gauntlet** |
| New tag | **1 — `Archive`**, for the Wrought that guard records rather than ground. Earned by then, because ten tags will have been in play for a full cycle |
| Magic work | The five **inert pinnacle placeholders** get designed. They are gated on levels the owner has not sized, and an Archive update is the natural place to size them |

**Why second.** It depends on two owner decisions (0f, the market; and the pinnacle levels), so it
should not be first. It also wants the set-bonus mechanic from Update A already live, because an
Orange archive set with no bonus is a worse reward than a Purple one with a good bonus.

### What both updates deliberately do not do

- **No new chapter.** Chapters 1–7 hold 139 nodes and the campaign's health curve was retuned three
  days ago. Adding chapter 8 before the retune has been played is guessing on top of guessing.
- **No new rarity.** Orange is the permanent ceiling and both updates respect it.
- **No prestige mechanic.** It is the genre's standard answer to the late-game problem and it is
  Owner decision 0b. Neither update assumes it either way.

---

## The one thing to decide before either update

**Owner decision 0k — the sigil rerun rate.** Both updates ship raids, and raids are gated on sigils,
and a sigil currently costs 34–70 days of banked energy at chapter 6. Shipping seven new raids into
that is shipping seven things almost nobody will summon. It is one config value and it decides
whether any of this content is reachable.
