# ROTA — art brief

**359 icons, in 23 batches.** Generated from the shipped content, so it cannot drift from what the game actually contains.

## How to use this

Work one batch at a time. Paste the **style block** first, then the batch's items. Generating a whole set in one sitting is what makes the set look like a set.

## The style block — paste this before every batch

```
Flat 2D game icon, clean illustrated vector style. Bold readable silhouette, two or three tone
cel shading, one soft light source from the upper left. Thick soft outline. A single object,
centred, filling most of the frame. Fully transparent background. Square.

No pixel art. No photorealism. No painterly texture. No fine filigree or micro-detail. No text,
no numbers, no letters. No border, no frame, no card, no background scene, no ground shadow.
Simple and confident rather than intricate.
```

**Negative prompt:**

```
pixel art, 8-bit, photorealistic, hyperdetailed, intricate, ornate filigree, painterly brushwork, text, watermark, border, frame, card layout, background scene, drop shadow on the ground, multiple objects, collage
```

## Output specification

| | |
|---|---|
| Format | PNG with real alpha transparency |
| Size | 512 x 512 square, downscaled in-engine |
| Background | fully transparent — not white, not a colour |
| Filename | the `id` exactly, e.g. `gear_conscript_helm.png` |
| Goes in | `assets/icons/<family>/` replacing the placeholder of the same name |

## Three rules that are not stylistic

1. **No colour instruction, and no rarity colour in the art.** The engine draws the rarity frame and tints the tile behind the icon using the client's own palette. Art that bakes in a rarity colour is locked to that tier forever, and re-tiering an item would mean redrawing it.
2. **No frame, border or card.** Same reason — the frame is drawn in-engine.
3. **Transparent background, every time.** A white background becomes a white box on the dark inventory tile.

**Sigils are collapsed.** The 104 sigil items are 26 raids x 4 difficulty tiers; the four tiers share one seal and are told apart by the frame the engine draws, so this brief asks for one artwork per raid. Name the file for the base id (`sigil_ironcolossus.png`) and all four tiers resolve to it.

Consistency beats quality here. A set of forty merely-good icons that share a style reads as a game; forty beautiful icons that do not share one reads as a folder.

---

## set_conscript — 8 icons

*Family `gear` → `assets/icons/gear/`*

**`gear_conscript_boots`** — Conscript Boots
> Conscript Boots — boots. Worn leather boots. Better than bare feet.

**`gear_conscript_chest`** — Conscript Chest
> Conscript Chest — chest armour. Padded leather chest armour. Stops the smallest of blows.

**`gear_conscript_collar`** — Conscript Collar
> Conscript Collar — amulet or pendant. A crude neck guard offering minimal protection.

**`gear_conscript_gloves`** — Conscript Gloves
> Conscript Gloves — gauntlet or glove. Rough cloth gloves. Barely break-in.

**`gear_conscript_helm`** — Conscript Helm
> Conscript Helm — helmet or headgear. A battered iron helm worn by new recruits.

**`gear_draft_horse`** — Draft Horse
> Draft Horse — mount, shown as the animal alone in profile. A sturdy workhorse. Occasionally charges into the fray with surprising force.

**`gear_iron_ring`** — Iron Ring
> Iron Ring — ring. A plain iron ring. Somehow sharpens the wearer's strikes.

**`gear_worn_band`** — Worn Band
> Worn Band — ring. A scratched metal band. No discernible power remains.

---

## set_pano — 8 icons

*Family `gear` → `assets/icons/gear/`*

**`gear_pano_amulet`** — Pano's Amulet
> Pano's Amulet — amulet or pendant. An amulet warm to the touch, part of Pano's questing regalia.

**`gear_pano_band`** — Pano's Band
> Pano's Band — ring. The companion band to Pano's signet. Steadies the hand in battle.

**`gear_pano_cuirass`** — Pano's Cuirass
> Pano's Cuirass — chest armour. The famed breastplate of Pano. Turns aside blows that would fell a lesser fighter.

**`gear_pano_gauntlets`** — Pano's Gauntlets
> Pano's Gauntlets — gauntlet or glove. Gauntlets that lend crushing strength to every strike.

**`gear_pano_greaves`** — Pano's Greaves
> Pano's Greaves — boots. Greaves that carry the wearer surefooted across any field.

**`gear_pano_helm`** — Pano's War Helm
> Pano's War Helm — helmet or headgear. Legendary questing helm of the lost vanguard Pano. Hums with old power.

**`gear_pano_signet`** — Pano's Signet
> Pano's Signet — ring. A signet ring that sharpens the wearer's strikes.

**`gear_pano_steed`** — Pano's Steed
> Pano's Steed — mount, shown as the animal alone in profile. Pano's tireless warhorse. Charges with devastating force.

---

## gear (no set) — 7 icons

*Family `gear` → `assets/icons/gear/`*

**`gear_cinder_cuff`** — The Cinder-Cuff
> The Cinder-Cuff — gauntlet or glove. Wrath-slag, cooled and cuffed. It makes the wearer stronger and angrier in the same motion, and does not distinguish between the two.

**`gear_cold_token`** — The Vanguard's Cold Token
> The Vanguard's Cold Token — amulet or pendant. Kin to the lost signet: the same unknown script worn nearly smooth, the same cold that does not warm in the hand. Nobody alive can read it. That is the whole of what is known.

**`gear_colossus_core`** — Colossus-Core Shard
> Colossus-Core Shard — chest armour. Pried from a war-construct that never stood down. It still pulses with the last order it was given, and there is no one left to amend it: hold.

**`gear_oathsteel_helm`** — Oathsteel Helm
> Oathsteel Helm — helmet or headgear. Forged from oathsteel and the shards of a hundred conscript helms.

**`gear_sealwright_stylus`** — The Sealwright's Stylus
> The Sealwright's Stylus — ring. The instrument that inscribed null-sigil bindings, the script that holds a thing closed. The Old Guard made few and accounted for every one. This one is not on the ledger.

**`gear_sovereign_tithe`** — Sovereign's Tithe-Mark
> Sovereign's Tithe-Mark — ring. Granted by the Gauntlet in the name of the founding pact. It marks a fighter as having paid the tithe. What the Sovereign was promised is recorded nowhere a fighter may read.

**`gear_unworn_crown`** — The Unworn Crown
> The Unworn Crown — helmet or headgear. A circlet of a dark metal no living smith can name, made for a head no carving depicts. It has never, in any account, been worn. The Old Guard kept it because they were afraid to be the…

---

## set_weir — 8 icons

*Family `gear` → `assets/icons/gear/`*

**`gear_causeway_seal`** — Causeway Seal Ring
> Causeway Seal Ring — ring. Ash-pitted signet of a road warden. The Ashen Causeway still has wardens. They are just not paid.

**`gear_marchwarden_band`** — Marchwarden's Band
> Marchwarden's Band — ring. Worn thin at one edge, where a thumb rubbed through forty years of standing still.

**`gear_weir_brigandine`** — Weir Brigandine
> Weir Brigandine — chest armour. Riveted from the scrap of three older coats. Every plate in it has already survived something.

**`gear_weir_courser`** — Weir Courser
> Weir Courser — mount, shown as the animal alone in profile. Bred for the courier roads. Not fast. Tireless, which on the frontier is the same as fast.

**`gear_weir_gorget`** — Weir Gorget
> Weir Gorget — amulet or pendant. Plate at the throat and nowhere else. The Weir learned which wounds end a watch.

**`gear_weir_handguards`** — Weir Handguards
> Weir Handguards — gauntlet or glove. Cut long at the wrist. A frontier warden reaches into places they cannot see.

**`gear_weir_kettle_helm`** — Weir Kettle Helm
> Weir Kettle Helm — helmet or headgear. Frontier issue. The brim is wide because rain ruins a bowstring faster than an enemy does.

**`gear_weir_marchboots`** — Weir Marchboots
> Weir Marchboots — boots. Resoled more times than made. The Weir counts a boot's age in roads, not years.

---

## set_relay — 8 icons

*Family `gear` → `assets/icons/gear/`*

**`gear_lamp_seal`** — Lamplighter's Seal
> Lamplighter's Seal — ring. Proof the bearer may enter a sealed relay. Nine in ten of those relays no longer answer.

**`gear_relay_charger`** — Relay Charger
> Relay Charger — mount, shown as the animal alone in profile. Trained to run a route with no rider. Several still do, on roads no one has walked in an Age.

**`gear_relay_coat`** — Relay Coat
> Relay Coat — chest armour. Long, grey, unremarkable, which is the point. A courier who is looked at twice is a dead courier.

**`gear_relay_grips`** — Lampwright's Grips
> Lampwright's Grips — gauntlet or glove. Scorched across the palms. The lamps of the Watch were never meant to be handled cold.

**`gear_relay_hood`** — Relaykeeper's Hood
> Relaykeeper's Hood — helmet or headgear. Oiled against frost, hemmed in lamp-black. Worn by the ones who kept the dead lamps burning.

**`gear_relay_torc`** — Torc of the Quiet Wind
> Torc of the Quiet Wind — amulet or pendant. Cold iron, warm at the throat. The Watch says it hums a half-beat before a relay fails.

**`gear_relay_treads`** — Frostmere Treads
> Frostmere Treads — boots. Nailed for ice. Frostmere takes a careless step and keeps it.

**`gear_vaultkeeper_band`** — Vaultkeeper's Band
> Vaultkeeper's Band — ring. Taken from the Sunken Vaults, which the Watch marked as sealed and the water did not.

---

## set_sable_vein — 8 icons

*Family `gear` → `assets/icons/gear/`*

**`gear_sable_band`** — Vein-Cut Band
> Vein-Cut Band — ring. Split by a hairline fracture that has not widened in two hundred years of being watched.

**`gear_sable_circlet`** — Sable Vein Circlet
> Sable Vein Circlet — helmet or headgear. Archive work. The script around the band is a catalogue number, and the thing catalogued is you.

**`gear_sable_collar`** — Threnody Collar
> Threnody Collar — amulet or pendant. Worn by a scribe who recorded a Manifestation from close enough to be corrected by it.

**`gear_sable_courser`** — Sable Courser
> Sable Courser — mount, shown as the animal alone in profile. Archive stock. It will not cross running water, and the Houses have never said why.

**`gear_sable_grips`** — Archivist's Grips
> Archivist's Grips — gauntlet or glove. Thin, precise, reinforced at the fingertips. Some things in an archive must be held down.

**`gear_sable_mantle`** — Mantle of the Sable Vein
> Mantle of the Sable Vein — chest armour. Black on black, and the inner black is older. The Houses do not explain their dyes.

**`gear_sable_seal`** — House Sable Seal
> House Sable Seal — ring. Opens an archive hall that appears on no map the Houses admit to holding.

**`gear_sable_treads`** — Stair-Worn Boots
> Stair-Worn Boots — boots. Taken off the Eternal Stair. The wear is at the toe, not the heel. Whoever wore them was climbing.

---

## set_warrens — 8 icons

*Family `gear` → `assets/icons/gear/`*

**`gear_warrens_choker`** — Ratter's Choker
> Ratter's Choker — amulet or pendant. Leather, doubled. Goblins go for the throat because it works, so the Weir stopped leaving it bare.

**`gear_warrens_grips`** — Ratter's Grips
> Ratter's Grips — gauntlet or glove. Reinforced across the back of the hand. You will be hitting things that are already biting you.

**`gear_warrens_jack`** — Warrens Jack
> Warrens Jack — chest armour. Quilted and short-cut. Long coats catch on everything down there, and everything down there catches back.

**`gear_warrens_knuckle`** — Knuckle-Ring of the Weir
> Knuckle-Ring of the Weir — ring. Worn on the outside of the glove. Frontier smiths call this an ornament and frontier wardens do not.

**`gear_warrens_lamp_hood`** — Warrens Lamp-Hood
> Warrens Lamp-Hood — helmet or headgear. A hood with a lamp bracket sewn at the temple. Both hands stay free, which in a warren is the whole argument.

**`gear_warrens_pitpony`** — Warrens Pit-Pony
> Warrens Pit-Pony — mount, shown as the animal alone in profile. Small, foul-tempered, and unbothered by the dark. It has walked out of places its riders did not.

**`gear_warrens_seal`** — Tunnel-Warden's Seal
> Tunnel-Warden's Seal — ring. Marks the bearer as the one who counts everyone back out. It is not a promotion.

**`gear_warrens_treads`** — Warrens Treads
> Warrens Treads — boots. Nailed flat for wet stone. A warren floor is never dry and never once been level.

---

## set_marchwatch — 8 icons

*Family `gear` → `assets/icons/gear/`*

**`gear_marchwatch_boots`** — Marchwatch Boots
> Marchwatch Boots — boots. Heavy, and worn through at the heel rather than the toe — the wear pattern of a man who stands.

**`gear_marchwatch_coat`** — Long Marchwatch Coat
> Long Marchwatch Coat — chest armour. Cut to the knee and lined against the wind. Most of the job is weather.

**`gear_marchwatch_gorget`** — Watchman's Gorget
> Watchman's Gorget — amulet or pendant. Plain steel, no device. A marchwatch is not a house and does not want to be mistaken for one.

**`gear_marchwatch_helm`** — Marchwatch Helm
> Marchwatch Helm — helmet or headgear. Open-faced, because a watch that cannot see is a wall with a man behind it.

**`gear_marchwatch_mitts`** — Watch Mitts
> Watch Mitts — gauntlet or glove. Split at the fingertips so a bowstring can still be felt. Frostbite is a slower enemy but it is patient.

**`gear_marchwatch_ring`** — Ring of the Standing Watch
> Ring of the Standing Watch — ring. Given at the end of a first full winter on the line. Most are given posthumously; this one was not.

**`gear_marchwatch_rounder`** — Marchwatch Rounder
> Marchwatch Rounder — mount, shown as the animal alone in profile. Trained to walk a circuit and stop at every marker without being asked. It knows the route better than the rider.

**`gear_marchwatch_tally`** — Tally-Ring
> Tally-Ring — ring. Notched once a season. A warden with a smooth ring is new; one with a worn ring is rare.

---

## set_gravewarden — 8 icons

*Family `gear` → `assets/icons/gear/`*

**`gear_gravewarden_band`** — Binding Band
> Binding Band — ring. Old sigil-work, worn smooth. It does not hold anything closed any more. The Watch wears it anyway.

**`gear_gravewarden_coat`** — Gravewarden's Coat
> Gravewarden's Coat — chest armour. Heavy canvas with iron at the forearms, because the thing you are moving sometimes moves back.

**`gear_gravewarden_dray`** — Gravewarden's Dray
> Gravewarden's Dray — mount, shown as the animal alone in profile. Bred to stand still while unpleasant work happens behind it. The rarest quality a horse can have.

**`gear_gravewarden_gauntlets`** — Gravewarden's Gauntlets
> Gravewarden's Gauntlets — gauntlet or glove. Iron over leather over iron. Whatever is down there does not get to hold your hand.

**`gear_gravewarden_hood`** — Gravewarden's Hood
> Gravewarden's Hood — helmet or headgear. Waxed against the smell. The Watch is direct about what the job involves.

**`gear_gravewarden_seal`** — Seal of the Quiet Ground
> Seal of the Quiet Ground — amulet or pendant. Worn so a warden can be identified if they do not come back up. It has been needed.

**`gear_gravewarden_signet`** — Warden's Signet
> Warden's Signet — ring. Authorises the bearer to open a marked grave. There is no ring that authorises closing one.

**`gear_gravewarden_wades`** — Marsh Wades
> Marsh Wades — boots. Thigh-high and pitch-sealed. The Hollow Marches are where the Watch does most of this.

---

## set_drowned — 8 icons

*Family `gear` → `assets/icons/gear/`*

**`gear_drowned_band`** — Barnacle Band
> Barnacle Band — ring. Recovered encrusted and left that way. Coast salvagers say a clean ring means a short career.

**`gear_drowned_boots`** — Shalewalkers
> Shalewalkers — boots. Soled in cork and iron. The Drowned Coast is loose all the way down.

**`gear_drowned_courser`** — Coastwise Courser
> Coastwise Courser — mount, shown as the animal alone in profile. Sure-footed on wet shale and entirely unwilling to enter water above the knee. It has its reasons.

**`gear_drowned_grips`** — Salvager's Grips
> Salvager's Grips — gauntlet or glove. Webbed, tarred, and cut short at the thumb so a knot can still be tied blind.

**`gear_drowned_harness`** — Salvage Harness
> Salvage Harness — chest armour. Rings and line, no plate. Down there weight is not protection, it is a decision.

**`gear_drowned_helm`** — Salvager's Helm
> Salvager's Helm — helmet or headgear. Sealed at the collar with a glass plate. The Vaults are dark before they are deep.

**`gear_drowned_seal`** — Vault-Seal Ring
> Vault-Seal Ring — ring. Old Guard work. It opens one door in the Sunken Vaults and nobody has found which.

**`gear_drowned_torc`** — Tidewatch Torc
> Tidewatch Torc — amulet or pendant. Cold in cold water, warm in water that is not. Salvagers do not agree on what the warm kind means.

---

## set_wroughtbreaker — 8 icons

*Family `gear` → `assets/icons/gear/`*

**`gear_wrought_band`** — Breaker's Band
> Breaker's Band — ring. Cast from the melt of a construct that finally stopped. The Weir keeps the melt and the tally.

**`gear_wrought_collar`** — Siege Collar
> Siege Collar — amulet or pendant. Braced to the shoulders. It exists so the head stays on when the arm stops something heavy.

**`gear_wrought_destrier`** — Siege Destrier
> Siege Destrier — mount, shown as the animal alone in profile. Trained to stand under a falling thing. Horses are not built for this and it is taught anyway.

**`gear_wrought_gauntlets`** — Wroughtbreaker Gauntlets
> Wroughtbreaker Gauntlets — gauntlet or glove. Built to hold a bar against a moving core until the order stops. Most pairs are used once.

**`gear_wrought_keyring`** — Sigil-Key Ring
> Sigil-Key Ring — ring. Reads a commanding sigil well enough to guess at its order. Guessing is the whole trade.

**`gear_wrought_plate`** — Wroughtbreaker Plate
> Wroughtbreaker Plate — chest armour. Layered against impact rather than edge. Nothing made by the Old Guard bothers with edges.

**`gear_wrought_sabatons`** — Breaker's Sabatons
> Breaker's Sabatons — boots. Weighted so a shove does not become a fall. A Wrought's first move is almost always a shove.

**`gear_wrought_visor`** — Breaker's Visor
> Breaker's Visor — helmet or headgear. Slit-narrow and backed in lead. A Wrought does not aim, which makes it worse, not better.

---

## set_choir — 8 icons

*Family `gear` → `assets/icons/gear/`*

**`gear_choir_band`** — Attendant's Band
> Attendant's Band — ring. Given to those who stand and do not sing. Somebody has to be listening to the listeners.

**`gear_choir_crown`** — Listener's Circlet
> Listener's Circlet — helmet or headgear. Thin, and open at the ears by design. The Choir does not cover what it uses.

**`gear_choir_palfrey`** — Choir Palfrey
> Choir Palfrey — mount, shown as the animal alone in profile. Trained to a whisper and unshod, so a procession arrives without announcing itself.

**`gear_choir_ring`** — Ring of the First Answer
> Ring of the First Answer — ring. Worn by whoever spoke when something answered. The Choir has four. It will not say to what.

**`gear_choir_slippers`** — Vigil Slippers
> Vigil Slippers — boots. Soft-soled. The Choir keeps its vigils barefoot where the ground permits and these where it does not.

**`gear_choir_stole`** — Resonant Stole
> Resonant Stole — amulet or pendant. It hums a half-tone under any sung note. The Choir considers this agreement.

**`gear_choir_vestment`** — Dawnward Vestment
> Dawnward Vestment — chest armour. Undyed, unadorned, and cut so it hangs still in wind. Movement is noise and noise is interference.

**`gear_choir_wraps`** — Cantor's Wraps
> Cantor's Wraps — gauntlet or glove. Linen, wound to the second knuckle. A Cantor's hands are for counting time, not for holding.

---

## set_sovereign — 8 icons

*Family `gear` → `assets/icons/gear/`*

**`gear_sovereign_band`** — Tithe-Band
> Tithe-Band — ring. One notch per climb paid. Nobody has found the ring where the notches run out.

**`gear_sovereign_collar`** — Collar of the Founding Pact
> Collar of the Founding Pact — amulet or pendant. Names the Gauntlet's first tithe in a script the tournament no longer teaches.

**`gear_sovereign_cuirass`** — Regalia Cuirass
> Regalia Cuirass — chest armour. Ceremonial in cut and emphatically not in construction. The Gauntlet has always been honest about that.

**`gear_sovereign_diadem`** — Sovereign's Diadem
> Sovereign's Diadem — helmet or headgear. Worn by whoever currently holds the Dragon bracket. It is returned, always, and never willingly.

**`gear_sovereign_gauntlets`** — Sovereign's Gauntlets
> Sovereign's Gauntlets — gauntlet or glove. The Gauntlet's namesake, and the only pair the tournament has ever formally issued.

**`gear_sovereign_greaves`** — Regalia Greaves
> Regalia Greaves — boots. Made to be seen on a stair. The Eternal Stair, specifically, and the Gauntlet knows it.

**`gear_sovereign_signet`** — Sovereign's Signet
> Sovereign's Signet — ring. Opens the Gauntlet's inner registry. What is written there is the pact's other half.

**`gear_sovereign_wyrm`** — Sovereign's Wyrm
> Sovereign's Wyrm — mount, shown as the animal alone in profile. Not a gift. A loan, from an institution that has never once explained its terms.

---

## set_stoned_devil — 8 icons

*Family `gear` → `assets/icons/gear/`*

**`gear_stoned_band`** — Band of the Fourth Reconsideration
> Band of the Fourth Reconsideration — ring. There were three earlier ones. He is not looking for them. He is fairly certain one of them is in the garden.

**`gear_stoned_censer`** — The Perpetual Censer
> The Perpetual Censer — amulet or pendant. A vessel lit some time during the Age of Dawn and never once refilled. There is still some left. There has always been some left. Those who come to kneel do not always remember, afterwards,…

**`gear_stoned_goat`** — Extremely Relaxed Hell-Goat
> Extremely Relaxed Hell-Goat — mount, shown as the animal alone in profile. It will carry you anywhere at exactly one speed. Attempts to hurry it have never once succeeded and, by every account, have never once been forgiven.

**`gear_stoned_horns`** — The Sundown Ember
> The Sundown Ember — helmet or headgear. Rolled at dusk, lit at dusk, and worn behind one ear like a circlet by an archdevil who maintains this is what it is for. The congregation stopped correcting him some centuries ago. It has…

**`gear_stoned_mitts`** — Mitts of Amiable Menace
> Mitts of Amiable Menace — gauntlet or glove. He shakes hands. It is worse than the alternative and takes considerably longer. He will ask after your family, and he will remember the answer, which is the genuinely frightening part.

**`gear_stoned_robe`** — Robe of the Long Sabbatical
> Robe of the Long Sabbatical — chest armour. Cut for a being who intended to sit down and has now been sitting down for an Age. Remarkably comfortable. Alarmingly hard to damage. It smells, faintly and permanently, of the room.

**`gear_stoned_signet`** — Signet of Declined Ascendancy
> Signet of Declined Ascendancy — ring. He was offered a throne. He read the terms, asked two questions nobody could answer, and went back inside. He says he will look at it again. He has been saying so for four hundred years.

**`gear_stoned_slippers`** — Slippers of the Unwalked Path
> Slippers of the Unwalked Path — boots. Immaculate. Not a scuff on them. He means to go out. He means to go out most evenings.

---

## items — Material — 47 icons

*Family `item` → `assets/icons/item/`*

**`mat_amber_rot`** — Amber Rot
> Amber Rot — crafting material. The Awakening's own residue. It is warm, it is slightly wrong, and it keeps indefinitely in a sealed jar that nobody wants in their pack.

**`mat_arcane_dust`** — Arcane Dust
> Arcane Dust — crafting material. Residual arcane energy left behind by Malachar's servants.

**`mat_barrow_iron`** — Barrow Iron
> Barrow Iron — crafting material. Grave-goods iron, buried long enough to take on the habit. It does not rust and it does not hold an edge — the Watch considers the trade fair.

**`mat_bog_cotton`** — Bog Cotton
> Bog Cotton — crafting material. Grows in standing water and pulls out in handfuls. Wadding, tinder, bandage — a Weir pack carries it because it is the cheapest thing that is ever useful.

**`mat_brimstone_slag`** — Brimstone Slag
> Brimstone Slag — crafting material. Cools where a Swollen thing stood too long. Emberpan is paved in it, mostly.

**`mat_brood_carapace`** — Brood Carapace
> Brood Carapace — crafting material. Plate off something that outgrew three of these before anyone got close enough to measure.

**`mat_brood_chitin`** — Mire-Brood Chitin
> Mire-Brood Chitin — crafting material. Plate from a thing that grows a new one each season and abandons the old where it stood.

**`mat_causeway_ash`** — Causeway Ash
> Causeway Ash — crafting material. Grey, weightless, and everywhere on the Ashen Causeway. Smiths pack it around a quench because it takes heat without ever giving it back.

**`mat_censer_resin`** — Perpetual Censer Resin
> Perpetual Censer Resin — crafting material. Scraped from the inside of a censer lit during the Age of Dawn. There is still some left. There has always been some left.

**`mat_cinder_salt`** — Cinder-Salt
> Cinder-Salt — crafting material. Scraped off the Ashen Throne, where the heat drove everything out of the stone but this. Tastes of iron. Nobody tastes it twice.

**`mat_coarse_thread`** — Coarse Thread
> Coarse Thread — crafting material. Spun thick enough to sew canvas and cheap enough to waste. The frontier repairs more than it replaces, and this is what it repairs with.

**`mat_colossus_filament`** — Colossus Filament
> Colossus Filament — crafting material. Sigil-wire from the Iron Colossus's commanding core. Still carrying an order. Still trying to deliver it to a chain of command four hundred years dead.

**`mat_command_sigil`** — Cracked Command Sigil
> Cracked Command Sigil — crafting material. Prised off a Wrought mid-order. It is still trying to finish the sentence.

**`mat_cork_iron`** — Cork-and-Iron Sole
> Cork-and-Iron Sole — crafting material. Salvager's stock. Buoyant enough to float a boot and heavy enough to keep it down.

**`mat_deepwood_heart`** — Deepwood Heart
> Deepwood Heart — crafting material. Cut from an apex that had been growing since before the Sundering. Still warm four days out.

**`mat_emberfall_slag`** — Emberfall Slag
> Emberfall Slag — crafting material. Run-off from a fire that burned in the Age of Dawn and has not entirely stopped. Warm at the core a thousand years after the fact.

**`mat_glutbound_core`** — Glutbound Core
> Glutbound Core — crafting material. The dense part of a thing that had nearly finished becoming something else.

**`mat_gravesalt`** — Gravesalt
> Gravesalt — crafting material. The Watch packs it around anything it has to move twice. It works, and nobody has asked how.

**`mat_gravewend_haft`** — Haft of Gravewend
> Haft of Gravewend — crafting material. A farm tool from a village whose name is not written down, carried by a man whose name is not written down, who used it to kill a thing that should have killed him. The village is gone. The…

**`mat_hollow_reed`** — Hollow Marches Reed
> Hollow Marches Reed — crafting material. Grows only where the ground is too wet to bury anything. The Marches are full of them.

**`mat_iron_shard`** — Iron Shard
> Iron Shard — crafting material. A fragment of the Iron Colossus. Used in crafting.

**`mat_keepwall_mortar`** — Keepwall Mortar
> Keepwall Mortar — crafting material. Prised from a wall Malachar's masons raised in one night. Nobody has explained the speed.

**`mat_kronarch_seal`** — Kronarch's Broken Seal
> Kronarch's Broken Seal — crafting material. From the muster-rolls of the army that won against nothing. Most of the names are legible.

**`mat_lamp_black`** — Lamp-Black
> Lamp-Black — crafting material. Soot off a relay lamp, ground fine. The Watch hems its hoods with it so a courier does not shine in a doorway.

**`mat_leviathan_baleen`** — Leviathan Baleen
> Leviathan Baleen — crafting material. Cut from a Primordial that was never killed, only out-waited. It filters things out of air that air was not known to contain.

**`mat_leviathan_tooth`** — Leviathan Tooth
> Leviathan Tooth — crafting material. One of very many, and still the largest object most people will ever hold.

**`mat_mire_ichor`** — Mire-Ichor
> Mire-Ichor — crafting material. Pale, luminous, foul. Bled from the Brood-things of the drowned shallows. Useless alone; the basis of half the alchemy on the Drowned Coast.

**`mat_null_sigil_ink`** — Null-Sigil Ink
> Null-Sigil Ink — crafting material. The medium the Sealwrights wrote closure in. It does not dry so much as decide to stop.

**`mat_oathsteel`** — Oathsteel Ingot
> Oathsteel Ingot — crafting material. Steel quenched in a spoken oath. The forge remembers what was promised.

**`mat_pitch_seal`** — Marsh Pitch
> Marsh Pitch — crafting material. Boiled down over three days. Gravewardens seal their wades with it and their coffins too.

**`mat_quiet_lamp_oil`** — Quiet Lamp Oil
> Quiet Lamp Oil — crafting material. Drawn from a Last Watch relay lamp that has burned unattended since the fall. The Watch does not know what it burns and has stopped asking.

**`mat_resonant_brass`** — Resonant Brass
> Resonant Brass — crafting material. Cast to hum at one note and no other. The Choir orders it by the tone, never the weight.

**`mat_rime_glass`** — Rime-Glass
> Rime-Glass — crafting material. Frostmere water frozen so slowly it set clear. It does not melt in the hand. It does not melt in a forge either, which is the difficulty.

**`mat_road_flint`** — Road Flint
> Road Flint — crafting material. Picked off any causeway by anyone who bothers to look down. It has started every fire the frontier has ever needed and it has never once been remarkable.

**`mat_sable_thread`** — Sable Vein Thread
> Sable Vein Thread — crafting material. The Houses dye it twice and will not discuss the second dye.

**`mat_siege_lead`** — Siege Lead
> Siege Lead — crafting material. Backing for a breaker's visor. A Wrought does not aim, which makes shielding a guess everywhere at once.

**`mat_sounding_horn`** — Sounding-Horn of the Sunken Leviathan
> Sounding-Horn of the Sunken Leviathan — crafting material. It still holds one note. The Old Guard who took it never agreed on what the note does, only that the sea answered the one time it was sounded.

**`mat_sovereign_scale`** — Sovereign's Shed Scale
> Sovereign's Shed Scale — crafting material. The Gauntlet collects them. It has never said what for and has never been asked twice.

**`mat_stag_ash`** — Ashen Stag Ash
> Ashen Stag Ash — crafting material. What is left where one lay down. The Heartmarch hunters do not collect it and will not say why.

**`mat_stag_tendon`** — Ashen Stag Heart-Tendon
> Ashen Stag Heart-Tendon — crafting material. The Heartmarch hunters string bows with it and say an arrow loosed from one arrives before the sound does.

**`mat_starhollow_dust`** — Star Hollow Dust
> Star Hollow Dust — crafting material. Collected where the sky is closest and least reliable. It settles upward if left alone, so it is never left alone.

**`mat_tallow`** — Rendered Tallow
> Rendered Tallow — crafting material. Every relay lamp in the Watch burns it, every boot in the Weir is greased with it, and nobody has ever written a sentence about it before this one.

**`mat_unwalked_leather`** — Unwalked Leather
> Unwalked Leather — crafting material. Cut for slippers that were never worn outdoors. Immaculate, and faintly reproachful.

**`mat_vanguard_banner`** — Vanguard Banner
> Vanguard Banner — crafting material. Carried at the front until the front moved. Proof of a line that held.

**`mat_warren_teeth`** — Warren Teeth
> Warren Teeth — crafting material. Goblins replace them constantly and leave the old ones where they fall. A tunnel floor is half gravel and half this.

**`mat_wrathslag`** — Wrath-Slag
> Wrath-Slag — crafting material. The cooled residue of a Manifestation. Still faintly warm an Age later, and still, very slightly, trying to qualify whoever holds it.

**`mat_wyrm_scale`** — Wyrm Scale
> Wyrm Scale — crafting material. Prised from a raid boss that did not part with it willingly.

---

## items — StatBag — 6 icons

*Family `item` → `assets/icons/item/`*

**`statbag_ancient`** — Ancient's Reliquary
> Ancient's Reliquary — pouch or cache. Contains 100 unassigned skill points. What the Dawnward Choir calls an answer.

**`statbag_greater`** — Greater Stat Bag
> Greater Stat Bag — pouch or cache. Contains 40 unassigned skill points. Old Guard requisition, unopened since the fall.

**`statbag_major`** — Major Stat Bag
> Major Stat Bag — pouch or cache. Contains 15 unassigned skill points. Use to add to your stat pool.

**`statbag_minor`** — Minor Stat Bag
> Minor Stat Bag — pouch or cache. Contains 5 unassigned skill points. Use to add to your stat pool.

**`statbag_oathsworn`** — Oathsworn Cache
> Oathsworn Cache — pouch or cache. Contains 65 unassigned skill points. Left by a company that did not need them after all.

**`statbag_reconsideration`** — A Moment's Reconsideration
> A Moment's Reconsideration — pouch or cache. Grants 25 unassigned skill points. He suggests you think about where you put them. He is not going to elaborate and he is not going to stop looking at you.

---

## items — Sigil (one per raid, shared by all four tiers) — 26 icons

*Family `item` → `assets/icons/item/`*

**`sigil_c1z1b`** — Ashen Causeway Sigil
> Ashen Causeway Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Ashen Causeway on Normal difficulty.

**`sigil_c1z2b`** — Hollow Marches Sigil
> Hollow Marches Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Hollow Marches on Normal difficulty.

**`sigil_c2z1b`** — Emberfall Reach Sigil
> Emberfall Reach Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Emberfall Reach on Normal difficulty.

**`sigil_c2z2b`** — Cinderwood Sigil
> Cinderwood Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Cinderwood on Normal difficulty.

**`sigil_c2z3b`** — Gloomspire Sigil
> Gloomspire Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Gloomspire on Normal difficulty.

**`sigil_c3z0b`** — Keepwall Sigil
> Keepwall Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Keepwall on Normal difficulty.

**`sigil_c3z1b`** — Sunken Vaults Sigil
> Sunken Vaults Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Sunken Vaults on Normal difficulty.

**`sigil_c3z2b`** — Throne Approach Sigil
> Throne Approach Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Throne Approach on Normal difficulty.

**`sigil_c3z3b`** — Shattered Spire Sigil
> Shattered Spire Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Shattered Spire on Normal difficulty.

**`sigil_c4z0b`** — Rimewood Sigil
> Rimewood Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Rimewood on Normal difficulty.

**`sigil_c4z1b`** — Frostmere Sigil
> Frostmere Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Frostmere on Normal difficulty.

**`sigil_c4z2b`** — Glacier Maw Sigil
> Glacier Maw Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Glacier Maw on Normal difficulty.

**`sigil_c4z3b`** — Pale Citadel Sigil
> Pale Citadel Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Pale Citadel on Normal difficulty.

**`sigil_c5z0b`** — Dustfall Sigil
> Dustfall Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Dustfall on Normal difficulty.

**`sigil_c5z1b`** — Emberpan Sigil
> Emberpan Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Emberpan on Normal difficulty.

**`sigil_c5z2b`** — Magma Rift Sigil
> Magma Rift Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Magma Rift on Normal difficulty.

**`sigil_c5z3b`** — Ashen Throne Sigil
> Ashen Throne Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Ashen Throne on Normal difficulty.

**`sigil_c5z4b`** — Cinder Crown Sigil
> Cinder Crown Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Cinder Crown on Normal difficulty.

**`sigil_c6z0b`** — Twilight Gate Sigil
> Twilight Gate Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Twilight Gate on Normal difficulty.

**`sigil_c6z1b`** — Star Hollow Sigil
> Star Hollow Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Star Hollow on Normal difficulty.

**`sigil_c6z2b`** — Void Threshold Sigil
> Void Threshold Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Void Threshold on Normal difficulty.

**`sigil_c6z3b`** — Eternal Stair Sigil
> Eternal Stair Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Eternal Stair on Normal difficulty.

**`sigil_c6z4b`** — Throne of Ancients Sigil
> Throne of Ancients Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Throne of Ancients on Normal difficulty.

**`sigil_ironcolossus`** — Iron Sigil
> Iron Sigil — summoning sigil or seal. A binding sigil used to summon the Iron Colossus on Normal difficulty.

**`sigil_lastwatch_lamp`** — Sigil of the Last Lamp
> Sigil of the Last Lamp — summoning sigil or seal. The name of a lamp that has not gone out. Speak it and the vigil resumes.

**`sigil_malachar`** — Malachar's Sigil
> Malachar's Sigil — summoning sigil or seal. A dark sigil bound to Malachar's essence. Summons him on Normal difficulty.

---

## items — Consumable — 19 icons

*Family `item` → `assets/icons/item/`*

**`elixir_health_restoration`** — Ancient's Mending
> Ancient's Mending — flask, vial or potion. Refills your Health completely. Found, never sold.

**`elixir_restoration`** — Ancient's Restorative
> Ancient's Restorative — flask, vial or potion. Refills your Energy completely. Found, never sold.

**`elixir_stamina_restoration`** — Ancient's Draught
> Ancient's Draught — flask, vial or potion. Refills your Stamina completely. Found, never sold.

**`potion_devils_tea`** — The Devil's Tea
> The Devil's Tea — flask, vial or potion. Restores 260 Energy. He offers it to everyone who comes to kill him. Several have stayed for a second cup and one is reportedly still there.

**`potion_energy_grand`** — Grand Energy Draught
> Grand Energy Draught — flask, vial or potion. Restores 400 Energy. The Weir issues these to couriers who are not expected back soon.

**`potion_energy_greater`** — Greater Energy Draught
> Greater Energy Draught — flask, vial or potion. Restores 150 Energy. Brewed at the relays, for a road that does not end at dusk.

**`potion_energy_major`** — Energy Draught
> Energy Draught — flask, vial or potion. Restores 60 Energy. Brewed for those who refuse to stop.

**`potion_energy_minor`** — Minor Energy Draught
> Minor Energy Draught — flask, vial or potion. Restores 25 Energy. The road does not walk itself.

**`potion_energy_relay`** — Relay Draught
> Relay Draught — flask, vial or potion. Restores 900 Energy. Issued to a courier who is not expected to stop.

**`potion_health_grand`** — Grand Healing Poultice
> Grand Healing Poultice — flask, vial or potion. Restores 400 Health. Field-standard for anything the Weir calls a bad week.

**`potion_health_greater`** — Greater Healing Poultice
> Greater Healing Poultice — flask, vial or potion. Restores 150 Health. Closes what a company opened.

**`potion_health_major`** — Healing Poultice
> Healing Poultice — flask, vial or potion. Restores 60 Health. Closes what the raid opened.

**`potion_health_minor`** — Minor Healing Poultice
> Minor Healing Poultice — flask, vial or potion. Restores 25 Health. Crude, bitter, and enough.

**`potion_long_afternoon`** — The Long Afternoon
> The Long Afternoon — flask, vial or potion. Restores 260 Stamina. Bottled from a nap of genuinely historic proportions.

**`potion_stamina_grand`** — Grand Stamina Draught
> Grand Stamina Draught — flask, vial or potion. Restores 400 Stamina. Drunk before a company commits to something it cannot walk away from.

**`potion_stamina_greater`** — Greater Stamina Draught
> Greater Stamina Draught — flask, vial or potion. Restores 150 Stamina. The arm gives out before the will does. This is for the arm.

**`potion_stamina_major`** — Stamina Draught
> Stamina Draught — flask, vial or potion. Restores 60 Stamina. The long fight favours the prepared.

**`potion_stamina_minor`** — Minor Stamina Draught
> Minor Stamina Draught — flask, vial or potion. Restores 25 Stamina. Steadies the arm for one more strike.

**`potion_stamina_relay`** — Warhorn Draught
> Warhorn Draught — flask, vial or potion. Restores 900 Stamina. Drunk by a company that has decided the thing in front of it is going down today.

---

## magics — 43 icons

*Family `magic` → `assets/icons/magic/`*

**`magic_age_of_dawn_fragment`** — Fragment of the Age of Dawn
> Fragment of the Age of Dawn — arcane rune, sigil or talisman representing a spell effect. A piece of a lesson from before the Sundering. The rest of it is not recoverable.

**`magic_blessing_of_might`** — Blessing of Might
> Blessing of Might — arcane rune, sigil or talisman representing a spell effect. The pinnacle boon. Sustained, reliable, and never a gamble.

**`magic_blessing_of_the_ancients`** — Blessing of the Ancients
> Blessing of the Ancients — arcane rune, sigil or talisman representing a spell effect. The Gauntlet ranks 2–10 aura. An off-cap proc applied to the Gauntlet raid, outside the five-magic slot cap. Rank-acquired only (per-event consumable).

**`magic_coinsense`** — Coin-Sense
> Coin-Sense — arcane rune, sigil or talisman representing a spell effect. A caravan lord's habit, distilled. It notices what a room is worth on the way in.

**`magic_considered_inaction`** — Considered Inaction
> Considered Inaction — arcane rune, sigil or talisman representing a spell effect. The archdevil's contribution to the war effort. He thought about it for an Age and then, on balance, contributed this. It is genuinely quite good, which nobody has forgiven.

**`magic_drillmasters_cant`** — Drillmaster's Cant
> Drillmaster's Cant — arcane rune, sigil or talisman representing a spell effect. The Weir's marching count. Nothing about it is magical and it works anyway.

**`magic_expose_weakness`** — Expose Weakness
> Expose Weakness — arcane rune, sigil or talisman representing a spell effect. Reveals a flaw in the target's defence, increasing crit chance.

**`magic_field_commission`** — Field Commission
> Field Commission — arcane rune, sigil or talisman representing a spell effect. Promotion granted where it was earned, by whoever was still standing to grant it.

**`magic_gamblers_sigil`** — Gambler's Sigil
> Gambler's Sigil — arcane rune, sigil or talisman representing a spell effect. Old Guard camp-work, technically forbidden. Enforcement was reportedly inconsistent.

**`magic_greater_poison`** — Greater Poison
> Greater Poison — arcane rune, sigil or talisman representing a spell effect. A potent venom with meaningful proc damage.

**`magic_hoarders_eye`** — Hoarder's Eye
> Hoarder's Eye — arcane rune, sigil or talisman representing a spell effect. Hoard's dominion is not petty. It is the principle of accumulation itself, and it is contagious.

**`magic_hollowpoint`** — Hollow Point
> Hollow Point — arcane rune, sigil or talisman representing a spell effect. A round bored out and left empty. What fills it on the way in is the argument.

**`magic_impending_doom`** — Impending Doom
> Impending Doom — arcane rune, sigil or talisman representing a spell effect. A catastrophic curse that rarely fires but devastates when it does.

**`magic_keen_eye`** — Keen Eye
> Keen Eye — arcane rune, sigil or talisman representing a spell effect. The Watch teaches it before it teaches anything else: look at the seam, not the shield.

**`magic_kindling`** — Kindling
> Kindling — arcane rune, sigil or talisman representing a spell effect. Occasionally doubles the experience earned from a hit.

**`magic_kronarchs_last_order`** — Kronarch's Last Order
> Kronarch's Last Order — arcane rune, sigil or talisman representing a spell effect. The command he gave when the tide went out and nothing answered. It still lands.

**`magic_last_lamp_wick`** — Wick of the Last Lamp
> Wick of the Last Lamp — arcane rune, sigil or talisman representing a spell effect. It burns very slowly and it has never gone out. Arveth's lamp is still lit on the same principle.

**`magic_lesser_poison`** — Lesser Poison
> Lesser Poison — arcane rune, sigil or talisman representing a spell effect. A weak venom that occasionally laces attacks.

**`magic_metronome`** — Wardens' Metronome
> Wardens' Metronome — arcane rune, sigil or talisman representing a spell effect. It keeps time, and every beat lands. Small, certain, endless.

**`magic_midas_touch`** — Midas Touch
> Midas Touch — arcane rune, sigil or talisman representing a spell effect. Every hit has a chance to yield bonus gold.

**`magic_one_true_swing`** — The One True Swing
> The One True Swing — arcane rune, sigil or talisman representing a spell effect. Gravewend's peasant got exactly one. The pitchfork remembers the shape of it — and has learned to repeat it.

**`magic_pinnacle_1000`** — Ascendant's Banner
> Ascendant's Banner — arcane rune, sigil or talisman representing a spell effect. The first standard planted past the thousandth mark. It does not make its bearer stronger so much as it makes everyone within sight of it harder to discourage — and the less a soldier…

**`magic_pinnacle_10000`** — Ancient's Wrath
> Ancient's Wrath — arcane rune, sigil or talisman representing a spell effect. PLACEHOLDER. A pinnacle magic awarded at level 10,000. Its entire effect — proc, drop, or something stranger — is designed by the first player to reach this level; everyone who arrives…

**`magic_pinnacle_15000`** — Elder Resonance
> Elder Resonance — arcane rune, sigil or talisman representing a spell effect. PLACEHOLDER. A pinnacle magic awarded at level 15,000. Its entire effect — proc, drop, or something stranger — is designed by the first player to reach this level; everyone who arrives…

**`magic_pinnacle_2500`** — Luminary's Vow
> Luminary's Vow — arcane rune, sigil or talisman representing a spell effect. PLACEHOLDER. A pinnacle magic awarded at level 2,500. Its entire effect — proc, drop, or something stranger — is designed by the first player to reach this level; everyone who arrives…

**`magic_pinnacle_25000`** — Eternal Aspect
> Eternal Aspect — arcane rune, sigil or talisman representing a spell effect. PLACEHOLDER. A pinnacle magic awarded at level 25,000. Its entire effect — proc, drop, or something stranger — is designed by the first player to reach this level; everyone who arrives…

**`magic_pinnacle_5000`** — Luminary's Echo
> Luminary's Echo — arcane rune, sigil or talisman representing a spell effect. PLACEHOLDER. A pinnacle magic awarded at level 5,000. Its entire effect — proc, drop, or something stranger — is designed by the first player to reach this level; everyone who arrives…

**`magic_pinnacle_7500`** — Archon's Decree
> Archon's Decree — arcane rune, sigil or talisman representing a spell effect. PLACEHOLDER. A pinnacle magic awarded at level 7,500. Its entire effect — proc, drop, or something stranger — is designed by the first player to reach this level; everyone who arrives…

**`magic_poison`** — Poison
> Poison — arcane rune, sigil or talisman representing a spell effect. A reliable venom that procs with moderate frequency.

**`magic_readers_mark`** — Reader's Mark
> Reader's Mark — arcane rune, sigil or talisman representing a spell effect. House Sable Vein trains a scribe to find the one line that matters. It transfers.

**`magic_rimefang`** — Rimefang
> Rimefang — arcane rune, sigil or talisman representing a spell effect. Frostmere ice that never gave up being water. It finds the gap in a thing and then widens it.

**`magic_sealwrights_measure`** — The Sealwright's Measure
> The Sealwright's Measure — arcane rune, sigil or talisman representing a spell effect. Before you can close a thing you must know exactly where it opens.

**`magic_smite`** — Smite
> Smite — arcane rune, sigil or talisman representing a spell effect. The pinnacle strike. It lands often enough that you plan around it.

**`magic_sovereigns_cut`** — The Sovereign's Cut
> The Sovereign's Cut — arcane rune, sigil or talisman representing a spell effect. Every climb pays a tithe. This is the arrangement that decides which way it flows.

**`magic_spoils_of_the_march`** — Spoils of the March
> Spoils of the March — arcane rune, sigil or talisman representing a spell effect. What an army leaves is worth more than what it carried. The Weir has always known this.

**`magic_steady_hand`** — Steady Hand
> Steady Hand — arcane rune, sigil or talisman representing a spell effect. Never misses, never surprises. The Weir issues it to anyone who has been startled once too often.

**`magic_sunder`** — Sunder
> Sunder — arcane rune, sigil or talisman representing a spell effect. Old Guard siege doctrine, reduced to one word and one motion.

**`magic_the_long_watch`** — The Long Watch
> The Long Watch — arcane rune, sigil or talisman representing a spell effect. Four hundred years of standing somewhere, compressed into the part that teaches.

**`magic_tithe_ledger`** — Tithe Ledger
> Tithe Ledger — arcane rune, sigil or talisman representing a spell effect. The Gauntlet has kept one since its founding. Nobody has audited it and nobody has offered.

**`magic_veiled_eye`** — The Veiled Eye
> The Veiled Eye — arcane rune, sigil or talisman representing a spell effect. Discernment's own nature, briefly lent. It does not see everything. It sees the thing that matters.

**`magic_whetstone`** — Whetstone
> Whetstone — arcane rune, sigil or talisman representing a spell effect. Sharpens every blow; a guaranteed small bonus on every hit.

**`magic_wrath_of_the_ancients`** — Wrath of the Ancients
> Wrath of the Ancients — arcane rune, sigil or talisman representing a spell effect. The Gauntlet rank-1 aura. An off-cap proc applied to the Gauntlet raid, outside the five-magic slot cap. Rank-acquired only (per-event consumable).

**`magic_wrathslag_ember`** — Wrathslag Ember
> Wrathslag Ember — arcane rune, sigil or talisman representing a spell effect. A coal off a Manifestation. Still warm, still trying, still not quite a Herald.

---

## units — 37 icons

*Family `unit` → `assets/icons/unit/`*

**`gen_ashblade`** — Ashblade
> Ashblade — character portrait bust of a Human Melee. A swift Human Melee general who strikes with burning precision.

**`gen_ashblade_ii`** — Ashblade, Emberborn
> Ashblade, Emberborn — character portrait bust of a Human Melee. The blade drank the pyre and came back hungrier.

**`gen_ashen_stag`** — The Ashen Stag
> The Ashen Stag — character portrait bust of a Beast Special. Not tamed. Accompanying. The Heartmarch hunters are precise about the distinction and have been since the first one tried the other word.

**`gen_brannoc`** — Brannoc Deepvein
> Brannoc Deepvein — character portrait bust of a Dwarf Tank. The Weir's siege-master. Has taken down four Wrought and will discuss none of them.

**`gen_choirmaster`** — The Dawnward Choirmaster
> The Dawnward Choirmaster — character portrait bust of a Human Healer. Keeps a company standing by counting time at them. It should not work.

**`gen_durn`** — Durn Anvilkeep
> Durn Anvilkeep — character portrait bust of a Dwarf Special. Reads a construct's commanding sigil the way other people read weather. Wrong twice; he keeps both notes.

**`gen_gorruk`** — Gorruk Stonejaw
> Gorruk Stonejaw — character portrait bust of a Oroc Tank. An Oroc line-holder who has never once been moved off a position he agreed to hold. The agreeing is the hard part.

**`gen_ironward`** — Ironward the Steadfast
> Ironward the Steadfast — character portrait bust of a Human Tank. A stalwart Human Tank general whose shield stance can deflect devastating strikes.

**`gen_ironward_ii`** — Ironward the Unbroken
> Ironward the Unbroken — character portrait bust of a Human Tank. Ironward reforged. The shield that deflected a wyrm now turns aside armies.

**`gen_makh`** — Makh the Unhurried
> Makh the Unhurried — character portrait bust of a Oroc Melee. Fights at exactly one speed. Opponents consistently mistake this for an opening.

**`gen_morvath`** — Morvath the Unliving
> Morvath the Unliving — character portrait bust of a Undead Special. An Undead Special general whose cursed wisdom amplifies every strike of his legion.

**`gen_pano`** — Pano, the Lost Vanguard
> Pano, the Lost Vanguard — character portrait bust of a Human Melee. The banner came back. Nobody has ever explained the rest of it, and the Watch has stopped asking in writing.

**`gen_sentinel_prime`** — Sentinel Prime
> Sentinel Prime — character portrait bust of a Construct Tank. Given a new order by someone with no authority to give it. It has not noticed, or it has and does not care, and the Weir has stopped asking which.

**`gen_sister_arveth`** — Sister Arveth of the Lamp
> Sister Arveth of the Lamp — character portrait bust of a Human Healer. The lamp is named for her, not the other way round. She would like that corrected and the Watch has declined for two hundred years.

**`gen_sylvaire`** — Sylvaire
> Sylvaire — character portrait bust of a Elf Ranged. An Elf Ranged general whose arrows seek vital points with unerring accuracy.

**`gen_the_reconsidered`** — The Reconsidered
> The Reconsidered — character portrait bust of a Demon Special. An archdevil's aide, sent along on the understanding that he would 'have a look'. He has had a look. He is still here. Nobody has raised it.

**`gen_vaskarr`** — Vaskarr the Bargained
> Vaskarr the Bargained — character portrait bust of a Demon Special. Bound by an agreement the Choir drafted and the Threnody Houses will not read aloud.

**`troop_acolytes`** — Shadow Acolytes
> Shadow Acolytes — character portrait bust of a Undead Special. Undead Special troops channeling dark wisdom into devastating blasts.

**`troop_archers`** — Wood Archers
> Wood Archers — character portrait bust of a Elf Ranged. Elf Ranged troops who pepper enemies from a distance.

**`troop_choir_attendants`** — Choir Attendants
> Choir Attendants — character portrait bust of a Human Healer. They stand and do not sing. Somebody has to be listening to the listeners.

**`troop_dwarf_hammers`** — Anvilkeep Hammers
> Anvilkeep Hammers — character portrait bust of a Dwarf Melee. Issued one hammer and one instruction, both heavy.

**`troop_dwarf_sappers`** — Weir Sappers
> Weir Sappers — character portrait bust of a Dwarf Special. They go under the thing. Frontier doctrine has never improved on this.

**`troop_emberpan_imps`** — Emberpan Imps
> Emberpan Imps — character portrait bust of a Demon Ranged. Malicious, tireless, and extremely literal about instructions.

**`troop_field_chirurgeons`** — Field Chirurgeons
> Field Chirurgeons — character portrait bust of a Human Healer. Weir-trained, which means fast, unsentimental, and usually right.

**`troop_glacier_bears`** — Glacier-Maw Bears
> Glacier-Maw Bears — character portrait bust of a Beast Tank. Taken as cubs from a den nobody has found twice.

**`troop_lamp_walkers`** — Lamp-Walkers
> Lamp-Walkers — character portrait bust of a Construct Ranged. Relay-work that kept walking its route after the relay fell. The Watch marches beside them now.

**`troop_militia`** — Conscript Militia
> Conscript Militia — character portrait bust of a Human Melee. Untrained Human foot soldiers — numerous but unremarkable.

**`troop_mire_brood`** — Mire-Brood Swarm
> Mire-Brood Swarm — character portrait bust of a Beast Melee. Bled for reagent, herded for war. Neither use was the Brood's idea.

**`troop_oroc_maulers`** — Oroc Maulers
> Oroc Maulers — character portrait bust of a Oroc Melee. Recruited by the Weir at rates it does not put in writing.

**`troop_oroc_shieldline`** — Oroc Shieldline
> Oroc Shieldline — character portrait bust of a Oroc Tank. They do not advance. That is not a limitation, it is the entire service being offered.

**`troop_pikemen`** — Iron Pikemen
> Iron Pikemen — character portrait bust of a Human Tank. Stalwart Human Tank troops who hold the line against any charge.

**`troop_pikemen_ii`** — Oathsteel Pikemen
> Oathsteel Pikemen — character portrait bust of a Human Tank. Iron pikes re-forged in oathsteel; the line does not break.

**`troop_rime_wolves`** — Rimewood Wolves
> Rimewood Wolves — character portrait bust of a Beast Ranged. They hunt the cold better than anything the Watch has ever fielded, and they know it.

**`troop_sable_witnesses`** — Sable Vein Witnesses
> Sable Vein Witnesses — character portrait bust of a Human Special. House scribes who record a fight from inside it. Their accounts are the only ones that agree.

**`troop_slagborn`** — Slagborn
> Slagborn — character portrait bust of a Demon Melee. What cools where a Manifestation stood, if it cools into legs.

**`troop_vanguard_oathsworn`** — Vanguard Oathsworn
> Vanguard Oathsworn — character portrait bust of a Human Melee. They swore to a man who did not come back, and have not considered that a release.

**`troop_wrought_automata`** — Salvaged Automata
> Salvaged Automata — character portrait bust of a Construct Melee. Restarted, roughly. They execute the order and nothing else, including stopping.

---

## legions — 10 icons

*Family `legion` → `assets/icons/legion/`*

**`legion_anvilkeep`** — The Anvilkeep Siege
> The Anvilkeep Siege — military banner or standard. Dwarven engineers and the heaviest thing they could get to the site. It is slow, it is loud, and Wrought do not get back up.

**`legion_bargained`** — The Bargained Company
> The Bargained Company — military banner or standard. Demons under written agreement, fielded by people who have read the agreement very carefully. It answers its own kind better than anything else will.

**`legion_deepwatch`** — The Deepwatch
> The Deepwatch — military banner or standard. The Last Watch's answer to the things that were here first. Everyone in it has seen one and elected to come back, which is the only entry requirement.

**`legion_houndsmen`** — The Houndsmen
> The Houndsmen — military banner or standard. A hunting company, not an army. Built around beasts and the people who can stand near them, it goes where a formation cannot and arrives sooner.

**`legion_ironlegion`** — The Iron Legion
> The Iron Legion — military banner or standard. A specialist formation demanding Tank, Melee, and Ranged generals alongside Strength troops — its type-locked slots carry higher power bonus.

**`legion_sovereigns_climb`** — The Sovereign's Climb
> The Sovereign's Climb — military banner or standard. A Gauntlet formation, fielded under the founding pact. What it costs to raise is written in the inner registry, which fighters may not read.

**`legion_vanguard`** — Dawn Vanguard
> Dawn Vanguard — military banner or standard. A disciplined vanguard formation with three general and three troop slots, open to any unit.

**`legion_vanguard_ii`** — Dawn Vanguard II
> Dawn Vanguard II — military banner or standard. The Vanguard rebuilt around a core of oathsteel — the same banner, twice the weight behind it.

**`legion_warband`** — Free Warband
> Free Warband — military banner or standard. An undisciplined but adaptable warband with no unit-type restrictions.

**`legion_warrenguard`** — The Warrenguard
> The Warrenguard — military banner or standard. Iron Weir tunnel work. Short ranks, low ceilings, and a doctrine that assumes the enemy is already inside the line.

---

## crafting recipes — 23 icons

*Family `recipe` → `assets/icons/recipe/`*

**`craft_ashblade_ii`** — Ashblade, Emberborn
> Ashblade, Emberborn — crafting or forging emblem for the item it produces. Quench Ashblade in wyrm-fire. Costly, and there is no way back to the blade you had.

**`craft_censer`** — The Perpetual Censer
> The Perpetual Censer — crafting or forging emblem for the item it produces. Lit some time during the Age of Dawn and never once since. Whatever is in it, there is still some left. The Choir has a theory. He has never confirmed or denied it and appears to find the…

**`craft_choir_crown`** — Listener's Circlet
> Listener's Circlet — crafting or forging emblem for the item it produces. Thin, and open at the ears by design. The Choir does not cover what it uses.

**`craft_choir_stole`** — Resonant Stole
> Resonant Stole — crafting or forging emblem for the item it produces. It hums a half-tone under any sung note. The Choir considers this agreement.

**`craft_deepwatch_gauntlets`** — Gravewarden's Gauntlets
> Gravewarden's Gauntlets — crafting or forging emblem for the item it produces. Iron over leather over iron. Whatever is down there does not get to hold your hand.

**`craft_drowned_helm`** — Salvager's Helm
> Salvager's Helm — crafting or forging emblem for the item it produces. Sealed at the collar with a glass plate. The Vaults are dark before they are deep.

**`craft_durn_sigilwise`** — Durn, Sigil-Wise
> Durn, Sigil-Wise — crafting or forging emblem for the item it produces. He was wrong twice and kept both notes. This is what the notes were for.

**`craft_gorruk_oathbound`** — Gorruk, Oathbound
> Gorruk, Oathbound — crafting or forging emblem for the item it produces. The Oroc agreed to hold a position. Oathsteel is how the Weir makes an agreement heavier.

**`craft_gravesalt_batch`** — A Warden's Measure of Gravesalt
> A Warden's Measure of Gravesalt — crafting or forging emblem for the item it produces. The Watch packs it around anything it has to move twice. Six jars is a season.

**`craft_gravewarden_coat`** — Gravewarden's Coat
> Gravewarden's Coat — crafting or forging emblem for the item it produces. Heavy canvas with iron at the forearms, because the thing you are moving sometimes moves back.

**`craft_gravewarden_dray`** — Gravewarden's Dray
> Gravewarden's Dray — crafting or forging emblem for the item it produces. Bred to stand still while unpleasant work happens behind it. The rarest quality a horse can have.

**`craft_hell_goat`** — Extremely Relaxed Hell-Goat
> Extremely Relaxed Hell-Goat — crafting or forging emblem for the item it produces. It will carry you anywhere at exactly one speed. Attempts to hurry it have never once succeeded and, by every account, have never once been forgiven.

**`craft_ironward_ii`** — Ironward the Unbroken
> Ironward the Unbroken — crafting or forging emblem for the item it produces. Reforge Ironward around a core of oathsteel. The general is consumed; what walks out is not the same man.

**`craft_marchwatch_coat`** — Long Marchwatch Coat
> Long Marchwatch Coat — crafting or forging emblem for the item it produces. Cut to the knee and lined against the wind, because most of the job is weather.

**`craft_oathsteel_helm`** — Oathsteel Helm
> Oathsteel Helm — crafting or forging emblem for the item it produces. A hundred conscript helms, melted down and made to mean something.

**`craft_pikemen_ii`** — Oathsteel Pikemen
> Oathsteel Pikemen — crafting or forging emblem for the item it produces. Re-forge the pikes. The men are the same; the line no longer bends.

**`craft_sovereign_signet`** — Sovereign's Signet
> Sovereign's Signet — crafting or forging emblem for the item it produces. Opens the Gauntlet's inner registry. What is written there is the pact's other half.

**`craft_stoned_slippers`** — Slippers of the Unwalked Path
> Slippers of the Unwalked Path — crafting or forging emblem for the item it produces. Immaculate. Not a scuff on them. Making a pair requires leather that has never been walked on, which is harder to source than it sounds and much harder to explain.

**`craft_vanguard_ii`** — Dawn Vanguard II
> Dawn Vanguard II — crafting or forging emblem for the item it produces. Rebuild the Vanguard around a veteran core — the banner survives, the legion under it does not.

**`craft_warrens_helm`** — Warrens Lamp-Hood
> Warrens Lamp-Hood — crafting or forging emblem for the item it produces. A hood, a bracket, and a lamp. The Weir has never improved on it and has stopped trying.

**`craft_warrens_pitpony`** — Warrens Pit-Pony
> Warrens Pit-Pony — crafting or forging emblem for the item it produces. Small, foul-tempered, and unbothered by the dark. Nobody has ever bred one on purpose twice.

**`craft_wrought_visor`** — Breaker's Visor
> Breaker's Visor — crafting or forging emblem for the item it produces. Slit-narrow and backed in lead. A Wrought does not aim, which makes it worse, not better.

**`craft_wroughtbreaker_gauntlets`** — Wroughtbreaker Gauntlets
> Wroughtbreaker Gauntlets — crafting or forging emblem for the item it produces. Built to hold a bar against a moving core until the order stops. Most pairs are used once.

---

## raid bosses — 37 icons

*Family `raid` → `assets/icons/raid/`*

**`gauntlet_stage_1`** — Gauntlet — Whelp Warden
> Gauntlet — Whelp Warden — monster or boss portrait. 

**`gauntlet_stage_2`** — Gauntlet — Drake Sentinel
> Gauntlet — Drake Sentinel — monster or boss portrait. 

**`gauntlet_stage_3`** — Gauntlet — Wyrm Vanguard
> Gauntlet — Wyrm Vanguard — monster or boss portrait. 

**`gauntlet_stage_4`** — Gauntlet — Elder Drake
> Gauntlet — Elder Drake — monster or boss portrait. 

**`gauntlet_stage_5`** — Gauntlet — Ancient Wyrm
> Gauntlet — Ancient Wyrm — monster or boss portrait. 

**`gauntlet_stage_6`** — Gauntlet — Dragon Sovereign
> Gauntlet — Dragon Sovereign — monster or boss portrait. 

**`guild_raid_leviathan`** — The Sunken Leviathan
> The Sunken Leviathan — monster or boss portrait. 

**`guild_raid_titan`** — Kronarch, World-Ender
> Kronarch, World-Ender — monster or boss portrait. 

**`guild_raid_warlord`** — Gorehowl the Warlord
> Gorehowl the Warlord — monster or boss portrait. 

**`raid_c1z1b`** — Guardian of Ashen Causeway
> Guardian of Ashen Causeway — monster or boss portrait. 

**`raid_c1z2b`** — The Hollow Marcher
> The Hollow Marcher — monster or boss portrait. 

**`raid_c2z1b`** — Warden of Emberfall
> Warden of Emberfall — monster or boss portrait. 

**`raid_c2z2b`** — Guardian of Cinderwood
> Guardian of Cinderwood — monster or boss portrait. 

**`raid_c2z3b`** — The Gloomspire Sentinel
> The Gloomspire Sentinel — monster or boss portrait. 

**`raid_c3z0b`** — Guardian of Keepwall
> Guardian of Keepwall — monster or boss portrait. 

**`raid_c3z1b`** — The Drowned Custodian
> The Drowned Custodian — monster or boss portrait. 

**`raid_c3z2b`** — Herald of the Approach
> Herald of the Approach — monster or boss portrait. 

**`raid_c3z3b`** — Spirebreaker
> Spirebreaker — monster or boss portrait. 

**`raid_c4z0b`** — The Rimewood Stalker
> The Rimewood Stalker — monster or boss portrait. 

**`raid_c4z1b`** — Guardian of Frostmere
> Guardian of Frostmere — monster or boss portrait. 

**`raid_c4z2b`** — Maw of the Glacier
> Maw of the Glacier — monster or boss portrait. 

**`raid_c4z3b`** — Warden of the Pale Citadel
> Warden of the Pale Citadel — monster or boss portrait. 

**`raid_c5z0b`** — The Dustfall Colossus
> The Dustfall Colossus — monster or boss portrait. 

**`raid_c5z1b`** — Tyrant of Emberpan
> Tyrant of Emberpan — monster or boss portrait. 

**`raid_c5z2b`** — Guardian of Magma Rift
> Guardian of Magma Rift — monster or boss portrait. 

**`raid_c5z3b`** — The Ashen Throne
> The Ashen Throne — monster or boss portrait. 

**`raid_c5z4b`** — The Cinder Crown
> The Cinder Crown — monster or boss portrait. 

**`raid_c6z0b`** — Guardian of Twilight Gate
> Guardian of Twilight Gate — monster or boss portrait. 

**`raid_c6z1b`** — Devourer of Star Hollow
> Devourer of Star Hollow — monster or boss portrait. 

**`raid_c6z2b`** — The Void Threshold
> The Void Threshold — monster or boss portrait. 

**`raid_c6z3b`** — Warden of the Eternal Stair
> Warden of the Eternal Stair — monster or boss portrait. 

**`raid_c6z4b`** — Guardian of the Throne of Ancients
> Guardian of the Throne of Ancients — monster or boss portrait. 

**`raid_ironcolossus`** — The Iron Colossus
> The Iron Colossus — monster or boss portrait. 

**`raid_lastwatch_lamp`** — The Last Lamp of Arveth
> The Last Lamp of Arveth — monster or boss portrait. 

**`raid_lastwatch_relay`** — The Relay of Sable Glass
> The Relay of Sable Glass — monster or boss portrait. 

**`raid_lastwatch_vault`** — The Quiet Wind Vault
> The Quiet Wind Vault — monster or boss portrait. 

**`raid_malachar`** — Lord Malachar
> Lord Malachar — monster or boss portrait. 

---

