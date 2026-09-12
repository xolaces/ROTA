# ROTA — art brief

**383 icons, in 52 batches.** Generated from the shipped content, so it cannot drift from what the game actually contains.

## Progress

**127 of 383 icons have real art. 15 of 52 batches are done; 37 remain (256 icons).**

A batch is done when every file it names in `assets/icons/<family>/` is delivered art (512px). Everything not yet drawn ships the generated placeholder — a 64px glyph tile with the rarity colour and a slot mark — so nothing is blank in the game; the placeholders are exactly what the remaining batches replace. This section is read from disk, not maintained by hand: land a sheet with `split_sheet.py`, re-run `python tools/art/gen_art_brief.py`, and the batch moves itself to done.

**Next up: Batch 16 — items — Material (2 of 9).** Remaining, in order: 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52.

| Batch | Group | Icons | Status |
|---|---|---|---|
| 1 | set_conscript | 8 | done — real art |
| 2 | set_pano | 8 | done — real art |
| 3 | gear (no set) | 7 | done — real art |
| 4 | set_weir | 8 | done — real art |
| 5 | set_relay | 8 | done — real art |
| 6 | set_sable_vein | 8 | done — real art |
| 7 | set_warrens | 8 | done — real art |
| 8 | set_marchwatch | 8 | done — real art |
| 9 | set_gravewarden | 8 | done — real art |
| 10 | set_drowned | 8 | done — real art |
| 11 | set_wroughtbreaker | 8 | done — real art |
| 12 | set_choir | 8 | done — real art |
| 13 | set_sovereign | 8 | done — real art |
| 14 | set_stoned_devil | 8 | done — real art |
| 15 | items — Material (1 of 9) | 8 | done — real art |
| 16 | items — Material (2 of 9) | 8 | **partial** — still placeholders: `mat_choir_scrap`, `mat_choir_tack` |
| 17 | items — Material (3 of 9) | 8 | **partial** — still placeholders: `mat_drowned_scrap`, `mat_drowned_tack`, `mat_glutbound_core`, `mat_gravesalt`, `mat_gravewarden_scrap`, `mat_gravewarden_tack` |
| 18 | items — Material (4 of 9) | 8 | **to do** — placeholders in game |
| 19 | items — Material (5 of 9) | 8 | **to do** — placeholders in game |
| 20 | items — Material (6 of 9) | 8 | **to do** — placeholders in game |
| 21 | items — Material (7 of 9) | 8 | **to do** — placeholders in game |
| 22 | items — Material (8 of 9) | 8 | **to do** — placeholders in game |
| 23 | items — Material (9 of 9) | 7 | **to do** — placeholders in game |
| 24 | items — StatBag | 6 | **to do** — placeholders in game |
| 25 | items — Sigil (one per raid, shared by all four tiers) (1 of 4) | 8 | **to do** — placeholders in game |
| 26 | items — Sigil (one per raid, shared by all four tiers) (2 of 4) | 8 | **to do** — placeholders in game |
| 27 | items — Sigil (one per raid, shared by all four tiers) (3 of 4) | 8 | **to do** — placeholders in game |
| 28 | items — Sigil (one per raid, shared by all four tiers) (4 of 4) | 2 | **to do** — placeholders in game |
| 29 | items — Consumable (1 of 3) | 8 | **to do** — placeholders in game |
| 30 | items — Consumable (2 of 3) | 8 | **to do** — placeholders in game |
| 31 | items — Consumable (3 of 3) | 3 | **to do** — placeholders in game |
| 32 | magics (1 of 6) | 8 | **to do** — placeholders in game |
| 33 | magics (2 of 6) | 8 | **to do** — placeholders in game |
| 34 | magics (3 of 6) | 8 | **to do** — placeholders in game |
| 35 | magics (4 of 6) | 8 | **to do** — placeholders in game |
| 36 | magics (5 of 6) | 8 | **to do** — placeholders in game |
| 37 | magics (6 of 6) | 3 | **to do** — placeholders in game |
| 38 | units (1 of 5) | 8 | **to do** — placeholders in game |
| 39 | units (2 of 5) | 8 | **to do** — placeholders in game |
| 40 | units (3 of 5) | 8 | **to do** — placeholders in game |
| 41 | units (4 of 5) | 8 | **to do** — placeholders in game |
| 42 | units (5 of 5) | 5 | **to do** — placeholders in game |
| 43 | legions (1 of 2) | 8 | **to do** — placeholders in game |
| 44 | legions (2 of 2) | 2 | **to do** — placeholders in game |
| 45 | crafting recipes (1 of 3) | 8 | **to do** — placeholders in game |
| 46 | crafting recipes (2 of 3) | 8 | **to do** — placeholders in game |
| 47 | crafting recipes (3 of 3) | 7 | **to do** — placeholders in game |
| 48 | raid bosses (1 of 5) | 8 | **to do** — placeholders in game |
| 49 | raid bosses (2 of 5) | 8 | **to do** — placeholders in game |
| 50 | raid bosses (3 of 5) | 8 | **to do** — placeholders in game |
| 51 | raid bosses (4 of 5) | 8 | **to do** — placeholders in game |
| 52 | raid bosses (5 of 5) | 5 | **to do** — placeholders in game |

---

## How to use this

Work one batch at a time. Paste the **style block** first, then the batch's items. Generating a whole set in one sitting is what makes the set look like a set.

## The style block — paste this before every batch

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

{budget} It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.
```

**Negative prompt:**

```
thick black outline, heavy keyline, sticker cutout, white halo, gradient shading, airbrush, gloss highlight, specular, glossy, bevel, emboss, drop shadow, three-quarter perspective, pixel art, photorealistic, hyperdetailed, intricate, ornate, filigree, painterly, stitching, rivets, scratches, text, watermark, border, frame, card layout, background scene, multiple objects, collage, checkerboard, transparency grid, grey and white squares
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

## Batch 1 — set_conscript · 8 icons · DONE

Real art is on disk for all 8. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Severely simplified: two materials, three or four shapes, no decoration at all. This is issued kit — plain, unadorned, slightly shabby. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

This set's signature, visible in every piece: Undyed brown leather and bare grey steel. No insignia, no colour, no decoration whatsoever — this is the kit a recruit is handed.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Conscript Boots — boots. Worn leather boots. Better than bare feet.
Conscript Chest — chest armour. Padded leather chest armour. Stops the smallest of blows.
Conscript Collar — amulet or pendant. A crude neck guard offering minimal protection.
Conscript Gloves — gauntlet or glove. Rough cloth gloves. Barely break-in.
Conscript Helm — helmet or headgear. A battered iron helm worn by new recruits.
Draft Horse — mount, shown as the animal alone in profile. A sturdy workhorse. Occasionally charges into the fray with surprising force.
Iron Ring — ring. A plain iron ring. Somehow sharpens the wearer's strikes.
Worn Band — ring. A scratched metal band. No discernible power remains.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/gear --size 512 --names gear_conscript_boots,gear_conscript_chest,gear_conscript_collar,gear_conscript_gloves,gear_conscript_helm,gear_draft_horse,gear_iron_ring,gear_worn_band
```

---

## Batch 2 — set_pano · 8 icons · DONE

Real art is on disk for all 8. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four or five materials, about nine shapes — the most detailed tier, and still no filigree. One distinctive silhouette flourish, and one precious or glowing inlay. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

This set's signature, visible in every piece: White enamel, deep blue and gold, carrying a four-pointed star.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Pano's Amulet — amulet or pendant. An amulet warm to the touch, part of Pano's questing regalia.
Pano's Band — ring. The companion band to Pano's signet. Steadies the hand in battle.
Pano's Cuirass — chest armour. The famed breastplate of Pano. Turns aside blows that would fell a lesser fighter.
Pano's Gauntlets — gauntlet or glove. Gauntlets that lend crushing strength to every strike.
Pano's Greaves — boots. Greaves that carry the wearer surefooted across any field.
Pano's War Helm — helmet or headgear. Legendary questing helm of the lost vanguard Pano. Hums with old power.
Pano's Signet — ring. A signet ring that sharpens the wearer's strikes.
Pano's Steed — mount, shown as the animal alone in profile. Pano's tireless warhorse. Charges with devastating force.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/gear --size 512 --names gear_pano_amulet,gear_pano_band,gear_pano_cuirass,gear_pano_gauntlets,gear_pano_greaves,gear_pano_helm,gear_pano_signet,gear_pano_steed
```

---

## Batch 3 — gear (no set) · 7 icons · DONE

Real art is on disk for all 7. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four or five materials, about nine shapes — the most detailed tier, and still no filigree. One distinctive silhouette flourish, and one precious or glowing inlay. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

There are 7 objects and 8 cells, so leave the last 1 cell of the bottom row completely empty. Do not spread the objects out to fill the canvas — keep every cell the same size and leave the spare one blank.

Draw these 7, in this order:

The Cinder-Cuff — gauntlet or glove. Wrath-slag, cooled and cuffed. It makes the wearer stronger and angrier in the same motion, and does not distinguish between the two.
The Vanguard's Cold Token — amulet or pendant. Kin to the lost signet: the same unknown script worn nearly smooth, the same cold that does not warm in the hand. Nobody alive can read it. That is the whole of what is known.
Colossus-Core Shard — chest armour. Pried from a war-construct that never stood down. It still pulses with the last order it was given, and there is no one left to amend it: hold.
Oathsteel Helm — helmet or headgear. Forged from oathsteel and the shards of a hundred conscript helms.
The Sealwright's Stylus — ring. The instrument that inscribed null-sigil bindings, the script that holds a thing closed. The Old Guard made few and accounted for every one. This one is not on the ledger.
Sovereign's Tithe-Mark — ring. Granted by the Gauntlet in the name of the founding pact. It marks a fighter as having paid the tithe. What the Sovereign was promised is recorded nowhere a fighter may read.
The Unworn Crown — helmet or headgear. A circlet of a dark metal no living smith can name, made for a head no carving depicts. It has never, in any account, been worn. The Old Guard kept it because they were afraid to be the…
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/gear --size 512 --names gear_cinder_cuff,gear_cold_token,gear_colossus_core,gear_oathsteel_helm,gear_sealwright_stylus,gear_sovereign_tithe,gear_unworn_crown
```

---

## Batch 4 — set_weir · 8 icons · DONE

Real art is on disk for all 8. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Simple: three materials, about five shapes, and ONE functional detail such as a strap, a buckle or a stamped mark. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

This set's signature, visible in every piece: Riveted iron bands over olive-green canvas, and a stamped square tower mark. Frontier issue: functional, squared-off, no curves. No lamps.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Causeway Seal Ring — ring. Ash-pitted signet of a road warden. The Ashen Causeway still has wardens. They are just not paid.
Marchwarden's Band — ring. Worn thin at one edge, where a thumb rubbed through forty years of standing still.
Weir Brigandine — chest armour. Riveted from the scrap of three older coats. Every plate in it has already survived something.
Weir Courser — mount, shown as the animal alone in profile. Bred for the courier roads. Not fast. Tireless, which on the frontier is the same as fast.
Weir Gorget — amulet or pendant. Plate at the throat and nowhere else. The Weir learned which wounds end a watch.
Weir Handguards — gauntlet or glove. Cut long at the wrist. A frontier warden reaches into places they cannot see.
Weir Kettle Helm — helmet or headgear. Frontier issue. The brim is wide because rain ruins a bowstring faster than an enemy does.
Weir Marchboots — boots. Resoled more times than made. The Weir counts a boot's age in roads, not years.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/gear --size 512 --names gear_causeway_seal,gear_marchwarden_band,gear_weir_brigandine,gear_weir_courser,gear_weir_gorget,gear_weir_handguards,gear_weir_kettle_helm,gear_weir_marchboots
```

---

## Batch 5 — set_relay · 8 icons · DONE

Real art is on disk for all 8. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

This set's signature, visible in every piece: Cream canvas with blue piping and brass fittings. Where a lamp appears it is a CLEAR SIGNAL LAMP — tall, glass-sided, amber lens. Never caged.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Lamplighter's Seal — ring. Proof the bearer may enter a sealed relay. Nine in ten of those relays no longer answer.
Relay Charger — mount, shown as the animal alone in profile. Trained to run a route with no rider. Several still do, on roads no one has walked in an Age.
Relay Coat — chest armour. Long, grey, unremarkable, which is the point. A courier who is looked at twice is a dead courier.
Lampwright's Grips — gauntlet or glove. Scorched across the palms. The lamps of the Watch were never meant to be handled cold.
Relaykeeper's Hood — helmet or headgear. Oiled against frost, hemmed in lamp-black. Worn by the ones who kept the dead lamps burning.
Torc of the Quiet Wind — amulet or pendant. Cold iron, warm at the throat. The Watch says it hums a half-beat before a relay fails.
Frostmere Treads — boots. Nailed for ice. Frostmere takes a careless step and keeps it.
Vaultkeeper's Band — ring. Taken from the Sunken Vaults, which the Watch marked as sealed and the water did not.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/gear --size 512 --names gear_lamp_seal,gear_relay_charger,gear_relay_coat,gear_relay_grips,gear_relay_hood,gear_relay_torc,gear_relay_treads,gear_vaultkeeper_band
```

---

## Batch 6 — set_sable_vein · 8 icons · DONE

Real art is on disk for all 8. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

This set's signature, visible in every piece: Matte black with a single gold lozenge and sable-thread edging. Severe, narrow, aristocratic.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Vein-Cut Band — ring. Split by a hairline fracture that has not widened in two hundred years of being watched.
Sable Vein Circlet — helmet or headgear. Archive work. The script around the band is a catalogue number, and the thing catalogued is you.
Threnody Collar — amulet or pendant. Worn by a scribe who recorded a Manifestation from close enough to be corrected by it.
Sable Courser — mount, shown as the animal alone in profile. Archive stock. It will not cross running water, and the Houses have never said why.
Archivist's Grips — gauntlet or glove. Thin, precise, reinforced at the fingertips. Some things in an archive must be held down.
Mantle of the Sable Vein — chest armour. Black on black, and the inner black is older. The Houses do not explain their dyes.
House Sable Seal — ring. Opens an archive hall that appears on no map the Houses admit to holding.
Stair-Worn Boots — boots. Taken off the Eternal Stair. The wear is at the toe, not the heel. Whoever wore them was climbing.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/gear --size 512 --names gear_sable_band,gear_sable_circlet,gear_sable_collar,gear_sable_courser,gear_sable_grips,gear_sable_mantle,gear_sable_seal,gear_sable_treads
```

---

## Batch 7 — set_warrens · 8 icons · DONE

Real art is on disk for all 8. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Simple: three materials, about five shapes, and ONE functional detail such as a strap, a buckle or a stamped mark. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

This set's signature, visible in every piece: Soot-blackened leather with brass fittings, and tusk or tooth accents. Where a lamp appears it is a CAGED PIT LAMP — squat, barred, underground.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Ratter's Choker — amulet or pendant. Leather, doubled. Goblins go for the throat because it works, so the Weir stopped leaving it bare.
Ratter's Grips — gauntlet or glove. Reinforced across the back of the hand. You will be hitting things that are already biting you.
Warrens Jack — chest armour. Quilted and short-cut. Long coats catch on everything down there, and everything down there catches back.
Knuckle-Ring of the Weir — ring. Worn on the outside of the glove. Frontier smiths call this an ornament and frontier wardens do not.
Warrens Lamp-Hood — helmet or headgear. A hood with a lamp bracket sewn at the temple. Both hands stay free, which in a warren is the whole argument.
Warrens Pit-Pony — mount, shown as the animal alone in profile. Small, foul-tempered, and unbothered by the dark. It has walked out of places its riders did not.
Tunnel-Warden's Seal — ring. Marks the bearer as the one who counts everyone back out. It is not a promotion.
Warrens Treads — boots. Nailed flat for wet stone. A warren floor is never dry and never once been level.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/gear --size 512 --names gear_warrens_choker,gear_warrens_grips,gear_warrens_jack,gear_warrens_knuckle,gear_warrens_lamp_hood,gear_warrens_pitpony,gear_warrens_seal,gear_warrens_treads
```

---

## Batch 8 — set_marchwatch · 8 icons · DONE

Real art is on disk for all 8. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Simple: three materials, about five shapes, and ONE functional detail such as a strap, a buckle or a stamped mark. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

This set's signature, visible in every piece: Long oiled wool in slate grey and waxed storm-cloth. Draped, caped, weatherproof silhouettes. Almost no metal beyond a single pin. No lamps.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Marchwatch Boots — boots. Heavy, and worn through at the heel rather than the toe — the wear pattern of a man who stands.
Long Marchwatch Coat — chest armour. Cut to the knee and lined against the wind. Most of the job is weather.
Watchman's Gorget — amulet or pendant. Plain steel, no device. A marchwatch is not a house and does not want to be mistaken for one.
Marchwatch Helm — helmet or headgear. Open-faced, because a watch that cannot see is a wall with a man behind it.
Watch Mitts — gauntlet or glove. Split at the fingertips so a bowstring can still be felt. Frostbite is a slower enemy but it is patient.
Ring of the Standing Watch — ring. Given at the end of a first full winter on the line. Most are given posthumously; this one was not.
Marchwatch Rounder — mount, shown as the animal alone in profile. Trained to walk a circuit and stop at every marker without being asked. It knows the route better than the rider.
Tally-Ring — ring. Notched once a season. A warden with a smooth ring is new; one with a worn ring is rare.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/gear --size 512 --names gear_marchwatch_boots,gear_marchwatch_coat,gear_marchwatch_gorget,gear_marchwatch_helm,gear_marchwatch_mitts,gear_marchwatch_ring,gear_marchwatch_rounder,gear_marchwatch_tally
```

---

## Batch 9 — set_gravewarden · 8 icons · DONE

Real art is on disk for all 8. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

This set's signature, visible in every piece: Heavy dark canvas over barrow-iron plate, black pitch seals, and one chalk-white line of gravesalt. Sombre, buried, weighted.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Binding Band — ring. Old sigil-work, worn smooth. It does not hold anything closed any more. The Watch wears it anyway.
Gravewarden's Coat — chest armour. Heavy canvas with iron at the forearms, because the thing you are moving sometimes moves back.
Gravewarden's Dray — mount, shown as the animal alone in profile. Bred to stand still while unpleasant work happens behind it. The rarest quality a horse can have.
Gravewarden's Gauntlets — gauntlet or glove. Iron over leather over iron. Whatever is down there does not get to hold your hand.
Gravewarden's Hood — helmet or headgear. Waxed against the smell. The Watch is direct about what the job involves.
Seal of the Quiet Ground — amulet or pendant. Worn so a warden can be identified if they do not come back up. It has been needed.
Warden's Signet — ring. Authorises the bearer to open a marked grave. There is no ring that authorises closing one.
Marsh Wades — boots. Thigh-high and pitch-sealed. The Hollow Marches are where the Watch does most of this.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/gear --size 512 --names gear_gravewarden_band,gear_gravewarden_coat,gear_gravewarden_dray,gear_gravewarden_gauntlets,gear_gravewarden_hood,gear_gravewarden_seal,gear_gravewarden_signet,gear_gravewarden_wades
```

---

## Batch 10 — set_drowned · 8 icons · DONE

Real art is on disk for all 8. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

This set's signature, visible in every piece: Sealed collars, thick glass plate, cork-and-iron soles and green verdigris copper. Everything looks watertight.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Barnacle Band — ring. Recovered encrusted and left that way. Coast salvagers say a clean ring means a short career.
Shalewalkers — boots. Soled in cork and iron. The Drowned Coast is loose all the way down.
Coastwise Courser — mount, shown as the animal alone in profile. Sure-footed on wet shale and entirely unwilling to enter water above the knee. It has its reasons.
Salvager's Grips — gauntlet or glove. Webbed, tarred, and cut short at the thumb so a knot can still be tied blind.
Salvage Harness — chest armour. Rings and line, no plate. Down there weight is not protection, it is a decision.
Salvager's Helm — helmet or headgear. Sealed at the collar with a glass plate. The Vaults are dark before they are deep.
Vault-Seal Ring — ring. Old Guard work. It opens one door in the Sunken Vaults and nobody has found which.
Tidewatch Torc — amulet or pendant. Cold in cold water, warm in water that is not. Salvagers do not agree on what the warm kind means.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/gear --size 512 --names gear_drowned_band,gear_drowned_boots,gear_drowned_courser,gear_drowned_grips,gear_drowned_harness,gear_drowned_helm,gear_drowned_seal,gear_drowned_torc
```

---

## Batch 11 — set_wroughtbreaker · 8 icons · DONE

Real art is on disk for all 8. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

This set's signature, visible in every piece: Blunt lead-grey slabs with an orange cracked-core glow in the seams. Industrial, heavy, siege equipment rather than armour.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Breaker's Band — ring. Cast from the melt of a construct that finally stopped. The Weir keeps the melt and the tally.
Siege Collar — amulet or pendant. Braced to the shoulders. It exists so the head stays on when the arm stops something heavy.
Siege Destrier — mount, shown as the animal alone in profile. Trained to stand under a falling thing. Horses are not built for this and it is taught anyway.
Wroughtbreaker Gauntlets — gauntlet or glove. Built to hold a bar against a moving core until the order stops. Most pairs are used once.
Sigil-Key Ring — ring. Reads a commanding sigil well enough to guess at its order. Guessing is the whole trade.
Wroughtbreaker Plate — chest armour. Layered against impact rather than edge. Nothing made by the Old Guard bothers with edges.
Breaker's Sabatons — boots. Weighted so a shove does not become a fall. A Wrought's first move is almost always a shove.
Breaker's Visor — helmet or headgear. Slit-narrow and backed in lead. A Wrought does not aim, which makes it worse, not better.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/gear --size 512 --names gear_wrought_band,gear_wrought_collar,gear_wrought_destrier,gear_wrought_gauntlets,gear_wrought_keyring,gear_wrought_plate,gear_wrought_sabatons,gear_wrought_visor
```

---

## Batch 12 — set_choir · 8 icons · DONE

Real art is on disk for all 8. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

This set's signature, visible in every piece: Resonant brass and pale bone-white, in bell and tuning-fork shapes. Deliberately open at the ears and throat.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Attendant's Band — ring. Given to those who stand and do not sing. Somebody has to be listening to the listeners.
Listener's Circlet — helmet or headgear. Thin, and open at the ears by design. The Choir does not cover what it uses.
Choir Palfrey — mount, shown as the animal alone in profile. Trained to a whisper and unshod, so a procession arrives without announcing itself.
Ring of the First Answer — ring. Worn by whoever spoke when something answered. The Choir has four. It will not say to what.
Vigil Slippers — boots. Soft-soled. The Choir keeps its vigils barefoot where the ground permits and these where it does not.
Resonant Stole — amulet or pendant. It hums a half-tone under any sung note. The Choir considers this agreement.
Dawnward Vestment — chest armour. Undyed, unadorned, and cut so it hangs still in wind. Movement is noise and noise is interference.
Cantor's Wraps — gauntlet or glove. Linen, wound to the second knuckle. A Cantor's hands are for counting time, not for holding.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/gear --size 512 --names gear_choir_band,gear_choir_crown,gear_choir_palfrey,gear_choir_ring,gear_choir_slippers,gear_choir_stole,gear_choir_vestment,gear_choir_wraps
```

---

## Batch 13 — set_sovereign · 8 icons · DONE

Real art is on disk for all 8. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four or five materials, about nine shapes — the most detailed tier, and still no filigree. One distinctive silhouette flourish, and one precious or glowing inlay. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

This set's signature, visible in every piece: Deep crimson dragon scale and antique gold, with scale-plate edges.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Tithe-Band — ring. One notch per climb paid. Nobody has found the ring where the notches run out.
Collar of the Founding Pact — amulet or pendant. Names the Gauntlet's first tithe in a script the tournament no longer teaches.
Regalia Cuirass — chest armour. Ceremonial in cut and emphatically not in construction. The Gauntlet has always been honest about that.
Sovereign's Diadem — helmet or headgear. Worn by whoever currently holds the Dragon bracket. It is returned, always, and never willingly.
Sovereign's Gauntlets — gauntlet or glove. The Gauntlet's namesake, and the only pair the tournament has ever formally issued.
Regalia Greaves — boots. Made to be seen on a stair. The Eternal Stair, specifically, and the Gauntlet knows it.
Sovereign's Signet — ring. Opens the Gauntlet's inner registry. What is written there is the pact's other half.
Sovereign's Wyrm — mount, shown as the animal alone in profile. Not a gift. A loan, from an institution that has never once explained its terms.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/gear --size 512 --names gear_sovereign_band,gear_sovereign_collar,gear_sovereign_cuirass,gear_sovereign_diadem,gear_sovereign_gauntlets,gear_sovereign_greaves,gear_sovereign_signet,gear_sovereign_wyrm
```

---

## Batch 14 — set_stoned_devil · 8 icons · DONE

Real art is on disk for all 8. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four or five materials, about nine shapes — the most detailed tier, and still no filigree. One distinctive silhouette flourish, and one precious or glowing inlay. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

This set's signature, visible in every piece: Deep maroon and cream with aged brass. Soft, draped, unhurried shapes — nothing sharp anywhere in the set.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Band of the Fourth Reconsideration — ring. There were three earlier ones. He is not looking for them. He is fairly certain one of them is in the garden.
The Perpetual Censer — amulet or pendant. A vessel lit some time during the Age of Dawn and never once refilled. There is still some left. There has always been some left. Those who come to kneel do not always remember, afterwards,…
Extremely Relaxed Hell-Goat — mount, shown as the animal alone in profile. It will carry you anywhere at exactly one speed. Attempts to hurry it have never once succeeded and, by every account, have never once been forgiven.
The Sundown Ember — helmet or headgear. Draw a single hand-rolled cigarette — a slim tapered paper roll, cream coloured, with a glowing ember at one lit end and a thin curl of smoke. It is the whole object; there is no circlet, no crown and no headband. Lay it horizontally across the cell.
Mitts of Amiable Menace — gauntlet or glove. He shakes hands. It is worse than the alternative and takes considerably longer. He will ask after your family, and he will remember the answer, which is the genuinely frightening part.
Robe of the Long Sabbatical — chest armour. Cut for a being who intended to sit down and has now been sitting down for an Age. Remarkably comfortable. Alarmingly hard to damage. It smells, faintly and permanently, of the room.
Signet of Declined Ascendancy — ring. He was offered a throne. He read the terms, asked two questions nobody could answer, and went back inside. He says he will look at it again. He has been saying so for four hundred years.
Slippers of the Unwalked Path — boots. Immaculate. Not a scuff on them. He means to go out. He means to go out most evenings.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/gear --size 512 --names gear_stoned_band,gear_stoned_censer,gear_stoned_goat,gear_stoned_horns,gear_stoned_mitts,gear_stoned_robe,gear_stoned_signet,gear_stoned_slippers
```

---

## Batch 15 — items — Material (1 of 9) · 8 icons · DONE

Real art is on disk for all 8. The prompt stays for re-rolls.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Amber Rot — crafting material. The Awakening's own residue. It is warm, it is slightly wrong, and it keeps indefinitely in a sealed jar that nobody wants in their pack.
Arcane Dust — crafting material. Residual arcane energy left behind by Malachar's servants.
Barrow Iron — crafting material. Grave-goods iron, buried long enough to take on the habit. It does not rust and it does not hold an edge — the Watch considers the trade fair.
Bog Cotton — crafting material. Grows in standing water and pulls out in handfuls. Wadding, tinder, bandage — a Weir pack carries it because it is the cheapest thing that is ever useful.
Brimstone Slag — crafting material. Cools where a Swollen thing stood too long. Emberpan is paved in it, mostly.
Brood Carapace — crafting material. Plate off something that outgrew three of these before anyone got close enough to measure.
Mire-Brood Chitin — crafting material. Plate from a thing that grows a new one each season and abandons the old where it stood.
Causeway Ash — crafting material. Grey, weightless, and everywhere on the Ashen Causeway. Smiths pack it around a quench because it takes heat without ever giving it back.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/item --size 512 --names mat_amber_rot,mat_arcane_dust,mat_barrow_iron,mat_bog_cotton,mat_brimstone_slag,mat_brood_carapace,mat_brood_chitin,mat_causeway_ash
```

---

## Batch 16 — items — Material (2 of 9) · 8 icons · PARTIAL

Still placeholders: `mat_choir_scrap`, `mat_choir_tack`. Re-run the whole sheet — a set drawn in one sitting matches itself.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Perpetual Censer Resin — crafting material. Scraped from the inside of a censer lit during the Age of Dawn. There is still some left. There has always been some left.
Choir Scrap — crafting material. Resonant brass and pale bone. Strike it and it holds a note for too long.
Choir Tack — crafting material. Palfrey tack in brass and bone, cut open at the ears so the animal can hear the choir.
Cinder-Salt — crafting material. Scraped off the Ashen Throne, where the heat drove everything out of the stone but this. Tastes of iron. Nobody tastes it twice.
Coarse Thread — crafting material. Spun thick enough to sew canvas and cheap enough to waste. The frontier repairs more than it replaces, and this is what it repairs with.
Colossus Filament — crafting material. Sigil-wire from the Iron Colossus's commanding core. Still carrying an order. Still trying to deliver it to a chain of command four hundred years dead.
Cracked Command Sigil — crafting material. Prised off a Wrought mid-order. It is still trying to finish the sentence.
Cork-and-Iron Sole — crafting material. Salvager's stock. Buoyant enough to float a boot and heavy enough to keep it down.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/item --size 512 --names mat_censer_resin,mat_choir_scrap,mat_choir_tack,mat_cinder_salt,mat_coarse_thread,mat_colossus_filament,mat_command_sigil,mat_cork_iron
```

---

## Batch 17 — items — Material (3 of 9) · 8 icons · PARTIAL

Still placeholders: `mat_drowned_scrap`, `mat_drowned_tack`, `mat_glutbound_core`, `mat_gravesalt`, `mat_gravewarden_scrap`, `mat_gravewarden_tack`. Re-run the whole sheet — a set drawn in one sitting matches itself.

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Deepwood Heart — crafting material. Cut from an apex that had been growing since before the Sundering. Still warm four days out.
Drowned Scrap — crafting material. Verdigris copper and sealed cork. It came up out of the water and is still wet.
Drowned Tack — crafting material. Tack from a drowned courser: glass-beaded, copper-buckled, and it does not rust.
Emberfall Slag — crafting material. A cold clinker, crusted over in pale ash-grey and chalky white, its surface matte and porous like pumice. It is NOT black rock and it has NO bright orange cracks. Only a single deep fracture shows any heat at all, and there the colour is a dull banked red, the darkest red on the icon — a fire remembered, not a fire burning.
Glutbound Core — crafting material. The dense part of a thing that had nearly finished becoming something else.
Gravesalt — crafting material. The Watch packs it around anything it has to move twice. It works, and nobody has asked how.
Gravewarden Scrap — crafting material. Barrow-iron plate and pitch-sealed canvas, with a line of gravesalt in every fold.
Gravewarden Tack — crafting material. Dray harness, black pitch on iron. It was made to pull weight out of the ground.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/item --size 512 --names mat_deepwood_heart,mat_drowned_scrap,mat_drowned_tack,mat_emberfall_slag,mat_glutbound_core,mat_gravesalt,mat_gravewarden_scrap,mat_gravewarden_tack
```

---

## Batch 18 — items — Material (4 of 9) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Haft of Gravewend — crafting material. A farm tool from a village whose name is not written down, carried by a man whose name is not written down, who used it to kill a thing that should have killed him. The village is gone. The…
Hollow Marches Reed — crafting material. Grows only where the ground is too wet to bury anything. The Marches are full of them.
Iron Shard — crafting material. A fragment of the Iron Colossus. Used in crafting.
Keepwall Mortar — crafting material. Prised from a wall Malachar's masons raised in one night. Nobody has explained the speed.
Kronarch's Broken Seal — crafting material. From the muster-rolls of the army that won against nothing. Most of the names are legible.
Lamp-Black — crafting material. Soot off a relay lamp, ground fine. The Watch hems its hoods with it so a courier does not shine in a doorway.
Leviathan Baleen — crafting material. Cut from a Primordial that was never killed, only out-waited. It filters things out of air that air was not known to contain.
Leviathan Tooth — crafting material. One of very many, and still the largest object most people will ever hold.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/item --size 512 --names mat_gravewend_haft,mat_hollow_reed,mat_iron_shard,mat_keepwall_mortar,mat_kronarch_seal,mat_lamp_black,mat_leviathan_baleen,mat_leviathan_tooth
```

---

## Batch 19 — items — Material (5 of 9) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Simple: three materials, about five shapes, and ONE functional detail such as a strap, a buckle or a stamped mark. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Marchwatch Scrap — crafting material. Oiled wool and storm-cloth cut from Marchwatch kit, still smelling of rain.
Marchwatch Tack — crafting material. Harness leather from a Marchwatch rounder, waxed against a weather that never let up.
Mire-Ichor — crafting material. Pale, luminous, foul. Bled from the Brood-things of the drowned shallows. Useless alone; the basis of half the alchemy on the Drowned Coast.
Null-Sigil Ink — crafting material. The medium the Sealwrights wrote closure in. It does not dry so much as decide to stop.
Oathsteel Ingot — crafting material. Steel quenched in a spoken oath. The forge remembers what was promised.
Vanguard Scrap — crafting material. White enamel, deep blue and gold, four-pointed star. Pano's line wore this and did not come back.
Vanguard Tack — crafting material. Barding from Pano's own steed, star still bright on the chamfron.
Marsh Pitch — crafting material. Boiled down over three days. Gravewardens seal their wades with it and their coffins too.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/item --size 512 --names mat_marchwatch_scrap,mat_marchwatch_tack,mat_mire_ichor,mat_null_sigil_ink,mat_oathsteel,mat_pano_scrap,mat_pano_tack,mat_pitch_seal
```

---

## Batch 20 — items — Material (6 of 9) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Quiet Lamp Oil — crafting material. Drawn from a Last Watch relay lamp that has burned unattended since the fall. The Watch does not know what it burns and has stopped asking.
Relay Scrap — crafting material. Cream canvas, blue piping, brass. Last Watch relay kit, taken off a relay runner.
Relay Tack — crafting material. A relay charger's tack, signal-lamp bracket still on the saddle.
Resonant Brass — crafting material. Cast to hum at one note and no other. The Choir orders it by the tone, never the weight.
Rime-Glass — crafting material. Frostmere water frozen so slowly it set clear. It does not melt in the hand. It does not melt in a forge either, which is the difficulty.
Road Flint — crafting material. Picked off any causeway by anyone who bothers to look down. It has started every fire the frontier has ever needed and it has never once been remarkable.
Sable Vein Thread — crafting material. The Houses dye it twice and will not discuss the second dye.
Sable Vein Scrap — crafting material. Matte black cloth with a single gold lozenge. House work; the seam is invisible.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/item --size 512 --names mat_quiet_lamp_oil,mat_relay_scrap,mat_relay_tack,mat_resonant_brass,mat_rime_glass,mat_road_flint,mat_sable_thread,mat_sable_vein_scrap
```

---

## Batch 21 — items — Material (7 of 9) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four or five materials, about nine shapes — the most detailed tier, and still no filigree. One distinctive silhouette flourish, and one precious or glowing inlay. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Sable Vein Tack — crafting material. A Sable courser's tack, narrow and severe, gold at one point only.
Siege Lead — crafting material. Backing for a breaker's visor. A Wrought does not aim, which makes shielding a guess everywhere at once.
Sounding-Horn of the Sunken Leviathan — crafting material. It still holds one note. The Old Guard who took it never agreed on what the note does, only that the sea answered the one time it was sounded.
Sovereign's Shed Scale — crafting material. The Gauntlet collects them. It has never said what for and has never been asked twice.
Sovereign Scrap — crafting material. Crimson scale on antique gold, edge-plated. Regalia; it wants a throne under it.
Sovereign Tack — crafting material. Wyrm tack. There is no word for what it is made of that anyone will say aloud.
Ashen Stag Ash — crafting material. What is left where one lay down. The Heartmarch hunters do not collect it and will not say why.
Ashen Stag Heart-Tendon — crafting material. The Heartmarch hunters string bows with it and say an arrow loosed from one arrives before the sound does.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/item --size 512 --names mat_sable_vein_tack,mat_siege_lead,mat_sounding_horn,mat_sovereign_scale,mat_sovereign_scrap,mat_sovereign_tack,mat_stag_ash,mat_stag_tendon
```

---

## Batch 22 — items — Material (8 of 9) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Simple: three materials, about five shapes, and ONE functional detail such as a strap, a buckle or a stamped mark. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Star Hollow Dust — crafting material. Collected where the sky is closest and least reliable. It settles upward if left alone, so it is never left alone.
Stoned Devil Scrap — crafting material. Maroon and cream with aged brass, soft to the touch. Nothing about it is sharp.
Stoned Devil Tack — crafting material. A goat's tack. Someone hung a small brass censer off it and it is still smoking.
Rendered Tallow — crafting material. Every relay lamp in the Watch burns it, every boot in the Weir is greased with it, and nobody has ever written a sentence about it before this one.
Unwalked Leather — crafting material. Cut for slippers that were never worn outdoors. Immaculate, and faintly reproachful.
Vanguard Banner — crafting material. Carried at the front until the front moved. Proof of a line that held.
Warren Teeth — crafting material. Goblins replace them constantly and leave the old ones where they fall. A tunnel floor is half gravel and half this.
Warrens Scrap — crafting material. Soot-black leather and brass fittings out of the pits. Every piece was worn down there.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/item --size 512 --names mat_starhollow_dust,mat_stoned_devil_scrap,mat_stoned_devil_tack,mat_tallow,mat_unwalked_leather,mat_vanguard_banner,mat_warren_teeth,mat_warrens_scrap
```

---

## Batch 23 — items — Material (9 of 9) · 7 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

There are 7 objects and 8 cells, so leave the last 1 cell of the bottom row completely empty. Do not spread the objects out to fill the canvas — keep every cell the same size and leave the spare one blank.

Draw these 7, in this order:

Warrens Tack — crafting material. A pit pony's tack — squat brass rings and a lamp-hook, black to the core.
Weir Scrap — crafting material. Rivets and strap-iron off Iron Weir issue. Enough of it reforges a piece.
Weir Tack — crafting material. Bit, buckle and shoe-iron from a Weir courser's tack. They do not come off easily.
Wrath-Slag — crafting material. The cooled residue of a Manifestation. Still faintly warm an Age later, and still, very slightly, trying to qualify whoever holds it.
Wroughtbreaker Scrap — crafting material. Lead-grey slab iron with a cracked orange core. Siege metal, not armour.
Wroughtbreaker Tack — crafting material. A destrier's barding off a siege line — heavy enough to need two hands.
Wyrm Scale — crafting material. Prised from a raid boss that did not part with it willingly.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/item --size 512 --names mat_warrens_tack,mat_weir_scrap,mat_weir_tack,mat_wrathslag,mat_wroughtbreaker_scrap,mat_wroughtbreaker_tack,mat_wyrm_scale
```

---

## Batch 24 — items — StatBag · 6 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 3 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 6, in this order:

Ancient's Reliquary — pouch or cache. Contains 100 unassigned skill points. What the Dawnward Choir calls an answer.
Greater Stat Bag — pouch or cache. Contains 40 unassigned skill points. Old Guard requisition, unopened since the fall.
Major Stat Bag — pouch or cache. Contains 15 unassigned skill points. Use to add to your stat pool.
Minor Stat Bag — pouch or cache. Contains 5 unassigned skill points. Use to add to your stat pool.
Oathsworn Cache — pouch or cache. Contains 65 unassigned skill points. Left by a company that did not need them after all.
A Moment's Reconsideration — pouch or cache. Grants 25 unassigned skill points. He suggests you think about where you put them. He is not going to elaborate and he is not going to stop looking at you.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 3x2 --out assets/icons/item --size 512 --names statbag_ancient,statbag_greater,statbag_major,statbag_minor,statbag_oathsworn,statbag_reconsideration
```

---

## Batch 25 — items — Sigil (one per raid, shared by all four tiers) (1 of 4) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Simple: three materials, about five shapes, and ONE functional detail such as a strap, a buckle or a stamped mark. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Ashen Causeway Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Ashen Causeway on Normal difficulty.
Hollow Marches Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Hollow Marches on Normal difficulty.
Emberfall Reach Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Emberfall Reach on Normal difficulty.
Cinderwood Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Cinderwood on Normal difficulty.
Gloomspire Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Gloomspire on Normal difficulty.
Keepwall Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Keepwall on Normal difficulty.
Sunken Vaults Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Sunken Vaults on Normal difficulty.
Throne Approach Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Throne Approach on Normal difficulty.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/item --size 512 --names sigil_c1z1b,sigil_c1z2b,sigil_c2z1b,sigil_c2z2b,sigil_c2z3b,sigil_c3z0b,sigil_c3z1b,sigil_c3z2b
```

---

## Batch 26 — items — Sigil (one per raid, shared by all four tiers) (2 of 4) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Simple: three materials, about five shapes, and ONE functional detail such as a strap, a buckle or a stamped mark. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Shattered Spire Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Shattered Spire on Normal difficulty.
Rimewood Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Rimewood on Normal difficulty.
Frostmere Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Frostmere on Normal difficulty.
Glacier Maw Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Glacier Maw on Normal difficulty.
Pale Citadel Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Pale Citadel on Normal difficulty.
Dustfall Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Dustfall on Normal difficulty.
Emberpan Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Emberpan on Normal difficulty.
Magma Rift Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Magma Rift on Normal difficulty.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/item --size 512 --names sigil_c3z3b,sigil_c4z0b,sigil_c4z1b,sigil_c4z2b,sigil_c4z3b,sigil_c5z0b,sigil_c5z1b,sigil_c5z2b
```

---

## Batch 27 — items — Sigil (one per raid, shared by all four tiers) (3 of 4) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Simple: three materials, about five shapes, and ONE functional detail such as a strap, a buckle or a stamped mark. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Ashen Throne Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Ashen Throne on Normal difficulty.
Cinder Crown Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Cinder Crown on Normal difficulty.
Twilight Gate Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Twilight Gate on Normal difficulty.
Star Hollow Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Star Hollow on Normal difficulty.
Void Threshold Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Void Threshold on Normal difficulty.
Eternal Stair Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Eternal Stair on Normal difficulty.
Throne of Ancients Sigil — summoning sigil or seal. A binding sigil used to summon Guardian of Throne of Ancients on Normal difficulty.
Iron Sigil — summoning sigil or seal. A binding sigil used to summon the Iron Colossus on Normal difficulty.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/item --size 512 --names sigil_c5z3b,sigil_c5z4b,sigil_c6z0b,sigil_c6z1b,sigil_c6z2b,sigil_c6z3b,sigil_c6z4b,sigil_ironcolossus
```

---

## Batch 28 — items — Sigil (one per raid, shared by all four tiers) (4 of 4) · 2 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Simple: three materials, about five shapes, and ONE functional detail such as a strap, a buckle or a stamped mark. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 2 x 1 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 2, in this order:

Sigil of the Last Lamp — summoning sigil or seal. The name of a lamp that has not gone out. Speak it and the vigil resumes.
Malachar's Sigil — summoning sigil or seal. A dark sigil bound to Malachar's essence. Summons him on Normal difficulty.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 2x1 --out assets/icons/item --size 512 --names sigil_lastwatch_lamp,sigil_malachar
```

---

## Batch 29 — items — Consumable (1 of 3) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Ancient's Mending — flask, vial or potion. Refills your Health completely. Found, never sold.
Ancient's Restorative — flask, vial or potion. Refills your Energy completely. Found, never sold.
Ancient's Draught — flask, vial or potion. Refills your Stamina completely. Found, never sold.
The Devil's Tea — flask, vial or potion. Restores 260 Energy. He offers it to everyone who comes to kill him. Several have stayed for a second cup and one is reportedly still there.
Grand Energy Draught — flask, vial or potion. Restores 400 Energy. The Weir issues these to couriers who are not expected back soon.
Greater Energy Draught — flask, vial or potion. Restores 150 Energy. Brewed at the relays, for a road that does not end at dusk.
Energy Draught — flask, vial or potion. Restores 60 Energy. Brewed for those who refuse to stop.
Minor Energy Draught — flask, vial or potion. Restores 25 Energy. The road does not walk itself.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/item --size 512 --names elixir_health_restoration,elixir_restoration,elixir_stamina_restoration,potion_devils_tea,potion_energy_grand,potion_energy_greater,potion_energy_major,potion_energy_minor
```

---

## Batch 30 — items — Consumable (2 of 3) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Relay Draught — flask, vial or potion. Restores 900 Energy. Issued to a courier who is not expected to stop.
Grand Healing Poultice — flask, vial or potion. Restores 400 Health. Field-standard for anything the Weir calls a bad week.
Greater Healing Poultice — flask, vial or potion. Restores 150 Health. Closes what a company opened.
Healing Poultice — flask, vial or potion. Restores 60 Health. Closes what the raid opened.
Minor Healing Poultice — flask, vial or potion. Restores 25 Health. Crude, bitter, and enough.
The Long Afternoon — flask, vial or potion. Restores 260 Stamina. Bottled from a nap of genuinely historic proportions.
Grand Stamina Draught — flask, vial or potion. Restores 400 Stamina. Drunk before a company commits to something it cannot walk away from.
Greater Stamina Draught — flask, vial or potion. Restores 150 Stamina. The arm gives out before the will does. This is for the arm.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/item --size 512 --names potion_energy_relay,potion_health_grand,potion_health_greater,potion_health_major,potion_health_minor,potion_long_afternoon,potion_stamina_grand,potion_stamina_greater
```

---

## Batch 31 — items — Consumable (3 of 3) · 3 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 2 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

There are 3 objects and 4 cells, so leave the last 1 cell of the bottom row completely empty. Do not spread the objects out to fill the canvas — keep every cell the same size and leave the spare one blank.

Draw these 3, in this order:

Stamina Draught — flask, vial or potion. Restores 60 Stamina. The long fight favours the prepared.
Minor Stamina Draught — flask, vial or potion. Restores 25 Stamina. Steadies the arm for one more strike.
Warhorn Draught — flask, vial or potion. Restores 900 Stamina. Drunk by a company that has decided the thing in front of it is going down today.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 2x2 --out assets/icons/item --size 512 --names potion_stamina_major,potion_stamina_minor,potion_stamina_relay
```

---

## Batch 32 — magics (1 of 6) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four or five materials, about nine shapes — the most detailed tier, and still no filigree. One distinctive silhouette flourish, and one precious or glowing inlay. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Fragment of the Age of Dawn — arcane rune, sigil or talisman representing a spell effect. A piece of a lesson from before the Sundering. The rest of it is not recoverable.
Blessing of Might — arcane rune, sigil or talisman representing a spell effect. The pinnacle boon. Sustained, reliable, and never a gamble.
Blessing of the Ancients — arcane rune, sigil or talisman representing a spell effect. The Gauntlet ranks 2–10 aura. An off-cap proc applied to the Gauntlet raid, outside the five-magic slot cap. Rank-acquired only (per-event consumable).
Coin-Sense — arcane rune, sigil or talisman representing a spell effect. A caravan lord's habit, distilled. It notices what a room is worth on the way in.
Considered Inaction — arcane rune, sigil or talisman representing a spell effect. The archdevil's contribution to the war effort. He thought about it for an Age and then, on balance, contributed this. It is genuinely quite good, which nobody has forgiven.
Drillmaster's Cant — arcane rune, sigil or talisman representing a spell effect. The Weir's marching count. Nothing about it is magical and it works anyway.
Expose Weakness — arcane rune, sigil or talisman representing a spell effect. Reveals a flaw in the target's defence, increasing crit chance.
Field Commission — arcane rune, sigil or talisman representing a spell effect. Promotion granted where it was earned, by whoever was still standing to grant it.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/magic --size 512 --names magic_age_of_dawn_fragment,magic_blessing_of_might,magic_blessing_of_the_ancients,magic_coinsense,magic_considered_inaction,magic_drillmasters_cant,magic_expose_weakness,magic_field_commission
```

---

## Batch 33 — magics (2 of 6) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Simple: three materials, about five shapes, and ONE functional detail such as a strap, a buckle or a stamped mark. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Gambler's Sigil — arcane rune, sigil or talisman representing a spell effect. Old Guard camp-work, technically forbidden. Enforcement was reportedly inconsistent.
Greater Poison — arcane rune, sigil or talisman representing a spell effect. A potent venom with meaningful proc damage.
Hoarder's Eye — arcane rune, sigil or talisman representing a spell effect. Hoard's dominion is not petty. It is the principle of accumulation itself, and it is contagious.
Hollow Point — arcane rune, sigil or talisman representing a spell effect. A round bored out and left empty. What fills it on the way in is the argument.
Impending Doom — arcane rune, sigil or talisman representing a spell effect. A catastrophic curse that rarely fires but devastates when it does.
Keen Eye — arcane rune, sigil or talisman representing a spell effect. The Watch teaches it before it teaches anything else: look at the seam, not the shield.
Kindling — arcane rune, sigil or talisman representing a spell effect. Occasionally doubles the experience earned from a hit.
Kronarch's Last Order — arcane rune, sigil or talisman representing a spell effect. The command he gave when the tide went out and nothing answered. It still lands.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/magic --size 512 --names magic_gamblers_sigil,magic_greater_poison,magic_hoarders_eye,magic_hollowpoint,magic_impending_doom,magic_keen_eye,magic_kindling,magic_kronarchs_last_order
```

---

## Batch 34 — magics (3 of 6) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four or five materials, about nine shapes — the most detailed tier, and still no filigree. One distinctive silhouette flourish, and one precious or glowing inlay. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Wick of the Last Lamp — arcane rune, sigil or talisman representing a spell effect. It burns very slowly and it has never gone out. Arveth's lamp is still lit on the same principle.
Lesser Poison — arcane rune, sigil or talisman representing a spell effect. A weak venom that occasionally laces attacks.
Wardens' Metronome — arcane rune, sigil or talisman representing a spell effect. It keeps time, and every beat lands. Small, certain, endless.
Midas Touch — arcane rune, sigil or talisman representing a spell effect. Every hit has a chance to yield bonus gold.
The One True Swing — arcane rune, sigil or talisman representing a spell effect. Gravewend's peasant got exactly one. The pitchfork remembers the shape of it — and has learned to repeat it.
Ascendant's Banner — arcane rune, sigil or talisman representing a spell effect. The first standard planted past the thousandth mark. It does not make its bearer stronger so much as it makes everyone within sight of it harder to discourage — and the less a soldier…
Ancient's Wrath — arcane rune, sigil or talisman representing a spell effect. PLACEHOLDER. A pinnacle magic awarded at level 10,000. Its entire effect — proc, drop, or something stranger — is designed by the first player to reach this level; everyone who arrives…
Elder Resonance — arcane rune, sigil or talisman representing a spell effect. PLACEHOLDER. A pinnacle magic awarded at level 15,000. Its entire effect — proc, drop, or something stranger — is designed by the first player to reach this level; everyone who arrives…
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/magic --size 512 --names magic_last_lamp_wick,magic_lesser_poison,magic_metronome,magic_midas_touch,magic_one_true_swing,magic_pinnacle_1000,magic_pinnacle_10000,magic_pinnacle_15000
```

---

## Batch 35 — magics (4 of 6) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four or five materials, about nine shapes — the most detailed tier, and still no filigree. One distinctive silhouette flourish, and one precious or glowing inlay. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Luminary's Vow — arcane rune, sigil or talisman representing a spell effect. PLACEHOLDER. A pinnacle magic awarded at level 2,500. Its entire effect — proc, drop, or something stranger — is designed by the first player to reach this level; everyone who arrives…
Eternal Aspect — arcane rune, sigil or talisman representing a spell effect. PLACEHOLDER. A pinnacle magic awarded at level 25,000. Its entire effect — proc, drop, or something stranger — is designed by the first player to reach this level; everyone who arrives…
Luminary's Echo — arcane rune, sigil or talisman representing a spell effect. PLACEHOLDER. A pinnacle magic awarded at level 5,000. Its entire effect — proc, drop, or something stranger — is designed by the first player to reach this level; everyone who arrives…
Archon's Decree — arcane rune, sigil or talisman representing a spell effect. PLACEHOLDER. A pinnacle magic awarded at level 7,500. Its entire effect — proc, drop, or something stranger — is designed by the first player to reach this level; everyone who arrives…
Poison — arcane rune, sigil or talisman representing a spell effect. A reliable venom that procs with moderate frequency.
Reader's Mark — arcane rune, sigil or talisman representing a spell effect. House Sable Vein trains a scribe to find the one line that matters. It transfers.
Rimefang — arcane rune, sigil or talisman representing a spell effect. Frostmere ice that never gave up being water. It finds the gap in a thing and then widens it.
The Sealwright's Measure — arcane rune, sigil or talisman representing a spell effect. Before you can close a thing you must know exactly where it opens.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/magic --size 512 --names magic_pinnacle_2500,magic_pinnacle_25000,magic_pinnacle_5000,magic_pinnacle_7500,magic_poison,magic_readers_mark,magic_rimefang,magic_sealwrights_measure
```

---

## Batch 36 — magics (5 of 6) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Smite — arcane rune, sigil or talisman representing a spell effect. The pinnacle strike. It lands often enough that you plan around it.
The Sovereign's Cut — arcane rune, sigil or talisman representing a spell effect. Every climb pays a tithe. This is the arrangement that decides which way it flows.
Spoils of the March — arcane rune, sigil or talisman representing a spell effect. What an army leaves is worth more than what it carried. The Weir has always known this.
Steady Hand — arcane rune, sigil or talisman representing a spell effect. Never misses, never surprises. The Weir issues it to anyone who has been startled once too often.
Sunder — arcane rune, sigil or talisman representing a spell effect. Old Guard siege doctrine, reduced to one word and one motion.
The Long Watch — arcane rune, sigil or talisman representing a spell effect. Four hundred years of standing somewhere, compressed into the part that teaches.
Tithe Ledger — arcane rune, sigil or talisman representing a spell effect. The Gauntlet has kept one since its founding. Nobody has audited it and nobody has offered.
The Veiled Eye — arcane rune, sigil or talisman representing a spell effect. Discernment's own nature, briefly lent. It does not see everything. It sees the thing that matters.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/magic --size 512 --names magic_smite,magic_sovereigns_cut,magic_spoils_of_the_march,magic_steady_hand,magic_sunder,magic_the_long_watch,magic_tithe_ledger,magic_veiled_eye
```

---

## Batch 37 — magics (6 of 6) · 3 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Severely simplified: two materials, four shapes. One small functional detail at most. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 2 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

There are 3 objects and 4 cells, so leave the last 1 cell of the bottom row completely empty. Do not spread the objects out to fill the canvas — keep every cell the same size and leave the spare one blank.

Draw these 3, in this order:

Whetstone — arcane rune, sigil or talisman representing a spell effect. Sharpens every blow; a guaranteed small bonus on every hit.
Wrath of the Ancients — arcane rune, sigil or talisman representing a spell effect. The Gauntlet rank-1 aura. An off-cap proc applied to the Gauntlet raid, outside the five-magic slot cap. Rank-acquired only (per-event consumable).
Wrathslag Ember — arcane rune, sigil or talisman representing a spell effect. A coal off a Manifestation. Still warm, still trying, still not quite a Herald.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 2x2 --out assets/icons/magic --size 512 --names magic_whetstone,magic_wrath_of_the_ancients,magic_wrathslag_ember
```

---

## Batch 38 — units (1 of 5) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Ashblade — character portrait bust of a Human Melee. A swift Human Melee general who strikes with burning precision.
Ashblade, Emberborn — character portrait bust of a Human Melee. The blade drank the pyre and came back hungrier.
The Ashen Stag — character portrait bust of a Beast Special. Not tamed. Accompanying. The Heartmarch hunters are precise about the distinction and have been since the first one tried the other word.
Brannoc Deepvein — character portrait bust of a Dwarf Tank. The Weir's siege-master. Has taken down four Wrought and will discuss none of them.
The Dawnward Choirmaster — character portrait bust of a Human Healer. Keeps a company standing by counting time at them. It should not work.
Durn Anvilkeep — character portrait bust of a Dwarf Special. Reads a construct's commanding sigil the way other people read weather. Wrong twice; he keeps both notes.
Gorruk Stonejaw — character portrait bust of a Oroc Tank. An Oroc line-holder who has never once been moved off a position he agreed to hold. The agreeing is the hard part.
Ironward the Steadfast — character portrait bust of a Human Tank. A stalwart Human Tank general whose shield stance can deflect devastating strikes.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/unit --size 512 --names gen_ashblade,gen_ashblade_ii,gen_ashen_stag,gen_brannoc,gen_choirmaster,gen_durn,gen_gorruk,gen_ironward
```

---

## Batch 39 — units (2 of 5) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Ironward the Unbroken — character portrait bust of a Human Tank. Ironward reforged. The shield that deflected a wyrm now turns aside armies.
Makh the Unhurried — character portrait bust of a Oroc Melee. Fights at exactly one speed. Opponents consistently mistake this for an opening.
Morvath the Unliving — character portrait bust of a Undead Special. An Undead Special general whose cursed wisdom amplifies every strike of his legion.
Pano, the Lost Vanguard — character portrait bust of a Human Melee. The banner came back. Nobody has ever explained the rest of it, and the Watch has stopped asking in writing.
Sentinel Prime — character portrait bust of a Construct Tank. Given a new order by someone with no authority to give it. It has not noticed, or it has and does not care, and the Weir has stopped asking which.
Sister Arveth of the Lamp — character portrait bust of a Human Healer. The lamp is named for her, not the other way round. She would like that corrected and the Watch has declined for two hundred years.
Sylvaire — character portrait bust of a Elf Ranged. An Elf Ranged general whose arrows seek vital points with unerring accuracy.
The Reconsidered — character portrait bust of a Demon Special. An archdevil's aide, sent along on the understanding that he would 'have a look'. He has had a look. He is still here. Nobody has raised it.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/unit --size 512 --names gen_ironward_ii,gen_makh,gen_morvath,gen_pano,gen_sentinel_prime,gen_sister_arveth,gen_sylvaire,gen_the_reconsidered
```

---

## Batch 40 — units (3 of 5) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Vaskarr the Bargained — character portrait bust of a Demon Special. Bound by an agreement the Choir drafted and the Threnody Houses will not read aloud.
Shadow Acolytes — character portrait bust of a Undead Special. Undead Special troops channeling dark wisdom into devastating blasts.
Wood Archers — character portrait bust of a Elf Ranged. Elf Ranged troops who pepper enemies from a distance.
Choir Attendants — character portrait bust of a Human Healer. They stand and do not sing. Somebody has to be listening to the listeners.
Anvilkeep Hammers — character portrait bust of a Dwarf Melee. Issued one hammer and one instruction, both heavy.
Weir Sappers — character portrait bust of a Dwarf Special. They go under the thing. Frontier doctrine has never improved on this.
Emberpan Imps — character portrait bust of a Demon Ranged. Malicious, tireless, and extremely literal about instructions.
Field Chirurgeons — character portrait bust of a Human Healer. Weir-trained, which means fast, unsentimental, and usually right.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/unit --size 512 --names gen_vaskarr,troop_acolytes,troop_archers,troop_choir_attendants,troop_dwarf_hammers,troop_dwarf_sappers,troop_emberpan_imps,troop_field_chirurgeons
```

---

## Batch 41 — units (4 of 5) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Glacier-Maw Bears — character portrait bust of a Beast Tank. Taken as cubs from a den nobody has found twice.
Lamp-Walkers — character portrait bust of a Construct Ranged. Relay-work that kept walking its route after the relay fell. The Watch marches beside them now.
Conscript Militia — character portrait bust of a Human Melee. Untrained Human foot soldiers — numerous but unremarkable.
Mire-Brood Swarm — character portrait bust of a Beast Melee. Bled for reagent, herded for war. Neither use was the Brood's idea.
Oroc Maulers — character portrait bust of a Oroc Melee. Recruited by the Weir at rates it does not put in writing.
Oroc Shieldline — character portrait bust of a Oroc Tank. They do not advance. That is not a limitation, it is the entire service being offered.
Iron Pikemen — character portrait bust of a Human Tank. Stalwart Human Tank troops who hold the line against any charge.
Oathsteel Pikemen — character portrait bust of a Human Tank. Iron pikes re-forged in oathsteel; the line does not break.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/unit --size 512 --names troop_glacier_bears,troop_lamp_walkers,troop_militia,troop_mire_brood,troop_oroc_maulers,troop_oroc_shieldline,troop_pikemen,troop_pikemen_ii
```

---

## Batch 42 — units (5 of 5) · 5 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 3 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

There are 5 objects and 6 cells, so leave the last 1 cell of the bottom row completely empty. Do not spread the objects out to fill the canvas — keep every cell the same size and leave the spare one blank.

Draw these 5, in this order:

Rimewood Wolves — character portrait bust of a Beast Ranged. They hunt the cold better than anything the Watch has ever fielded, and they know it.
Sable Vein Witnesses — character portrait bust of a Human Special. House scribes who record a fight from inside it. Their accounts are the only ones that agree.
Slagborn — character portrait bust of a Demon Melee. What cools where a Manifestation stood, if it cools into legs.
Vanguard Oathsworn — character portrait bust of a Human Melee. They swore to a man who did not come back, and have not considered that a release.
Salvaged Automata — character portrait bust of a Construct Melee. Restarted, roughly. They execute the order and nothing else, including stopping.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 3x2 --out assets/icons/unit --size 512 --names troop_rime_wolves,troop_sable_witnesses,troop_slagborn,troop_vanguard_oathsworn,troop_wrought_automata
```

---

## Batch 43 — legions (1 of 2) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

The Anvilkeep Siege — military banner or standard. Dwarven engineers and the heaviest thing they could get to the site. It is slow, it is loud, and Wrought do not get back up.
The Bargained Company — military banner or standard. Demons under written agreement, fielded by people who have read the agreement very carefully. It answers its own kind better than anything else will.
The Deepwatch — military banner or standard. The Last Watch's answer to the things that were here first. Everyone in it has seen one and elected to come back, which is the only entry requirement.
The Houndsmen — military banner or standard. A hunting company, not an army. Built around beasts and the people who can stand near them, it goes where a formation cannot and arrives sooner.
The Iron Legion — military banner or standard. A specialist formation demanding Tank, Melee, and Ranged generals alongside Strength troops — its type-locked slots carry higher power bonus.
The Sovereign's Climb — military banner or standard. A Gauntlet formation, fielded under the founding pact. What it costs to raise is written in the inner registry, which fighters may not read.
Dawn Vanguard — military banner or standard. A disciplined vanguard formation with three general and three troop slots, open to any unit.
Dawn Vanguard II — military banner or standard. The Vanguard rebuilt around a core of oathsteel — the same banner, twice the weight behind it.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/legion --size 512 --names legion_anvilkeep,legion_bargained,legion_deepwatch,legion_houndsmen,legion_ironlegion,legion_sovereigns_climb,legion_vanguard,legion_vanguard_ii
```

---

## Batch 44 — legions (2 of 2) · 2 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Severely simplified: two materials, four shapes. One small functional detail at most. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 2 x 1 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 2, in this order:

Free Warband — military banner or standard. An undisciplined but adaptable warband with no unit-type restrictions.
The Warrenguard — military banner or standard. Iron Weir tunnel work. Short ranks, low ceilings, and a doctrine that assumes the enemy is already inside the line.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 2x1 --out assets/icons/legion --size 512 --names legion_warband,legion_warrenguard
```

---

## Batch 45 — crafting recipes (1 of 3) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Ashblade, Emberborn — crafting or forging emblem for the item it produces. Quench Ashblade in wyrm-fire. Costly, and there is no way back to the blade you had.
The Perpetual Censer — crafting or forging emblem for the item it produces. Lit some time during the Age of Dawn and never once since. Whatever is in it, there is still some left. The Choir has a theory. He has never confirmed or denied it and appears to find the…
Listener's Circlet — crafting or forging emblem for the item it produces. Thin, and open at the ears by design. The Choir does not cover what it uses.
Resonant Stole — crafting or forging emblem for the item it produces. It hums a half-tone under any sung note. The Choir considers this agreement.
Gravewarden's Gauntlets — crafting or forging emblem for the item it produces. Iron over leather over iron. Whatever is down there does not get to hold your hand.
Salvager's Helm — crafting or forging emblem for the item it produces. Sealed at the collar with a glass plate. The Vaults are dark before they are deep.
Durn, Sigil-Wise — crafting or forging emblem for the item it produces. He was wrong twice and kept both notes. This is what the notes were for.
Gorruk, Oathbound — crafting or forging emblem for the item it produces. The Oroc agreed to hold a position. Oathsteel is how the Weir makes an agreement heavier.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/recipe --size 512 --names craft_ashblade_ii,craft_censer,craft_choir_crown,craft_choir_stole,craft_deepwatch_gauntlets,craft_drowned_helm,craft_durn_sigilwise,craft_gorruk_oathbound
```

---

## Batch 46 — crafting recipes (2 of 3) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

A Warden's Measure of Gravesalt — crafting or forging emblem for the item it produces. The Watch packs it around anything it has to move twice. Six jars is a season.
Gravewarden's Coat — crafting or forging emblem for the item it produces. Heavy canvas with iron at the forearms, because the thing you are moving sometimes moves back.
Gravewarden's Dray — crafting or forging emblem for the item it produces. Bred to stand still while unpleasant work happens behind it. The rarest quality a horse can have.
Extremely Relaxed Hell-Goat — crafting or forging emblem for the item it produces. It will carry you anywhere at exactly one speed. Attempts to hurry it have never once succeeded and, by every account, have never once been forgiven.
Ironward the Unbroken — crafting or forging emblem for the item it produces. Reforge Ironward around a core of oathsteel. The general is consumed; what walks out is not the same man.
Long Marchwatch Coat — crafting or forging emblem for the item it produces. Cut to the knee and lined against the wind, because most of the job is weather.
Oathsteel Helm — crafting or forging emblem for the item it produces. A hundred conscript helms, melted down and made to mean something.
Oathsteel Pikemen — crafting or forging emblem for the item it produces. Re-forge the pikes. The men are the same; the line no longer bends.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/recipe --size 512 --names craft_gravesalt_batch,craft_gravewarden_coat,craft_gravewarden_dray,craft_hell_goat,craft_ironward_ii,craft_marchwatch_coat,craft_oathsteel_helm,craft_pikemen_ii
```

---

## Batch 47 — crafting recipes (3 of 3) · 7 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

There are 7 objects and 8 cells, so leave the last 1 cell of the bottom row completely empty. Do not spread the objects out to fill the canvas — keep every cell the same size and leave the spare one blank.

Draw these 7, in this order:

Sovereign's Signet — crafting or forging emblem for the item it produces. Opens the Gauntlet's inner registry. What is written there is the pact's other half.
Slippers of the Unwalked Path — crafting or forging emblem for the item it produces. Immaculate. Not a scuff on them. Making a pair requires leather that has never been walked on, which is harder to source than it sounds and much harder to explain.
Dawn Vanguard II — crafting or forging emblem for the item it produces. Rebuild the Vanguard around a veteran core — the banner survives, the legion under it does not.
Warrens Lamp-Hood — crafting or forging emblem for the item it produces. A hood, a bracket, and a lamp. The Weir has never improved on it and has stopped trying.
Warrens Pit-Pony — crafting or forging emblem for the item it produces. Small, foul-tempered, and unbothered by the dark. Nobody has ever bred one on purpose twice.
Breaker's Visor — crafting or forging emblem for the item it produces. Slit-narrow and backed in lead. A Wrought does not aim, which makes it worse, not better.
Wroughtbreaker Gauntlets — crafting or forging emblem for the item it produces. Built to hold a bar against a moving core until the order stops. Most pairs are used once.
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/recipe --size 512 --names craft_sovereign_signet,craft_stoned_slippers,craft_vanguard_ii,craft_warrens_helm,craft_warrens_pitpony,craft_wrought_visor,craft_wroughtbreaker_gauntlets
```

---

## Batch 48 — raid bosses (1 of 5) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Gauntlet — Whelp Warden — monster or boss portrait. 
Gauntlet — Drake Sentinel — monster or boss portrait. 
Gauntlet — Wyrm Vanguard — monster or boss portrait. 
Gauntlet — Elder Drake — monster or boss portrait. 
Gauntlet — Ancient Wyrm — monster or boss portrait. 
Gauntlet — Dragon Sovereign — monster or boss portrait. 
The Sunken Leviathan — monster or boss portrait. 
Kronarch, World-Ender — monster or boss portrait. 
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/raid --size 512 --names gauntlet_stage_1,gauntlet_stage_2,gauntlet_stage_3,gauntlet_stage_4,gauntlet_stage_5,gauntlet_stage_6,guild_raid_leviathan,guild_raid_titan
```

---

## Batch 49 — raid bosses (2 of 5) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Gorehowl the Warlord — monster or boss portrait. 
Guardian of Ashen Causeway — monster or boss portrait. 
The Hollow Marcher — monster or boss portrait. 
Warden of Emberfall — monster or boss portrait. 
Guardian of Cinderwood — monster or boss portrait. 
The Gloomspire Sentinel — monster or boss portrait. 
Guardian of Keepwall — monster or boss portrait. 
The Drowned Custodian — monster or boss portrait. 
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/raid --size 512 --names guild_raid_warlord,raid_c1z1b,raid_c1z2b,raid_c2z1b,raid_c2z2b,raid_c2z3b,raid_c3z0b,raid_c3z1b
```

---

## Batch 50 — raid bosses (3 of 5) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Herald of the Approach — monster or boss portrait. 
Spirebreaker — monster or boss portrait. 
The Rimewood Stalker — monster or boss portrait. 
Guardian of Frostmere — monster or boss portrait. 
Maw of the Glacier — monster or boss portrait. 
Warden of the Pale Citadel — monster or boss portrait. 
The Dustfall Colossus — monster or boss portrait. 
Tyrant of Emberpan — monster or boss portrait. 
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/raid --size 512 --names raid_c3z2b,raid_c3z3b,raid_c4z0b,raid_c4z1b,raid_c4z2b,raid_c4z3b,raid_c5z0b,raid_c5z1b
```

---

## Batch 51 — raid bosses (4 of 5) · 8 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 4 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

Draw these 8, in this order:

Guardian of Magma Rift — monster or boss portrait. 
The Ashen Throne — monster or boss portrait. 
The Cinder Crown — monster or boss portrait. 
Guardian of Twilight Gate — monster or boss portrait. 
Devourer of Star Hollow — monster or boss portrait. 
The Void Threshold — monster or boss portrait. 
Warden of the Eternal Stair — monster or boss portrait. 
Guardian of the Throne of Ancients — monster or boss portrait. 
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 4x2 --out assets/icons/raid --size 512 --names raid_c5z2b,raid_c5z3b,raid_c5z4b,raid_c6z0b,raid_c6z1b,raid_c6z2b,raid_c6z3b,raid_c6z4b
```

---

## Batch 52 — raid bosses (5 of 5) · 5 icons · TO DO

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at 64 pixels, so anything smaller than a fingernail is left out entirely:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One object, centred, filling the frame. Straight-on or clean profile view. Flat even light with no
implied direction. Square, with no halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Arrange them on ONE landscape image, 1536 x 1024, as a strict 3 x 2 grid.

Every object sits centred in its own cell and stays entirely inside it, with equal margins all
round. Nothing touches or overlaps a neighbour, and nothing — no smoke, no trailing strap, no tail —
may cross a cell boundary. Leave clear empty space between cells.

Reading order is left to right, top to bottom, matching the list below.

There are 5 objects and 6 cells, so leave the last 1 cell of the bottom row completely empty. Do not spread the objects out to fill the canvas — keep every cell the same size and leave the spare one blank.

Draw these 5, in this order:

The Iron Colossus — monster or boss portrait. 
The Last Lamp of Arveth — monster or boss portrait. 
The Relay of Sable Glass — monster or boss portrait. 
The Quiet Wind Vault — monster or boss portrait. 
Lord Malachar — monster or boss portrait. 
```

Then cut it up:

```bash
python tools/art/split_sheet.py SHEET.png --grid 3x2 --out assets/icons/raid --size 512 --names raid_ironcolossus,raid_lastwatch_lamp,raid_lastwatch_relay,raid_lastwatch_vault,raid_malachar
```

---

## The body — the paper doll's figure

The Profile shows a figure wearing the gear. Today it is a generated mannequin (`assets/icons/body/mannequin.png`) with each worn piece's icon docked on the part of the body it belongs to; the dock points live in `mannequin.json` beside it. The real figure replaces that file at the same 2:3 framing — **keep the pose**, the docks are placed on it.

### The mannequin — 1 image

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Severely simplified: two materials, four shapes. One small functional detail at most. It will be viewed at about 350 pixels tall, so keep detail at the scale of a buckle, never a stitch:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One figure, centred. Straight-on. Flat even light with no
implied direction. No halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Portrait format, 2:3 (1024 x 1536). One standing human figure, front-facing, weight even on both
feet, feet a little apart, arms held slightly away from the body with the palms turned in. The
figure fills the height with a small margin above the head and below the feet. No face — a smooth
featureless head. No weapon, no ground, no shadow under the feet, transparent background.

The figure is BARE: bare chest, bare arms, bare legs, bare hands and feet, no hair, no ornament, no jewellery. The ONLY garment is a pair of plain close-fitting shorts to mid-thigh in undyed linen — the armour will cover them. No shirt, no tunic, no sleeves, no sash, no belt, no trousers. Neutral skin tone and cream, nothing that reads as a rarity colour. It exists to be dressed.

Negative prompt: thick black outline, heavy keyline, sticker cutout, white halo, gradient shading, airbrush, gloss highlight, specular, glossy, bevel, emboss, drop shadow, three-quarter perspective, pixel art, photorealistic, hyperdetailed, intricate, ornate, filigree, painterly, stitching, rivets, scratches, text, watermark, border, frame, card layout, background scene, multiple objects, collage, checkerboard, transparency grid, grey and white squares, tunic, shirt, sleeves, sash, belt, robe, trousers, boots, gloves, hat, hair, jewellery, clothing
```

Save as `assets/icons/body/mannequin.png`.

### Phase 2 — one figure per set, same pose · 13 images

Not wired yet; generate when convenient. Each is the mannequin above wearing the complete set, and the pose and framing must match the mannequin exactly. Save as `assets/icons/body/<setId>.png`.

**Conscript** — `assets/icons/body/set_conscript.png`

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Severely simplified: two materials, three or four shapes, no decoration at all. This is issued kit — plain, unadorned, slightly shabby. It will be viewed at about 350 pixels tall, so keep detail at the scale of a buckle, never a stitch:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One figure, centred. Straight-on. Flat even light with no
implied direction. No halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Portrait format, 2:3 (1024 x 1536). One standing human figure, front-facing, weight even on both
feet, feet a little apart, arms held slightly away from the body with the palms turned in. The
figure fills the height with a small margin above the head and below the feet. No face — a smooth
featureless head. No weapon, no ground, no shadow under the feet, transparent background.

The same figure, same pose, same framing, now wearing the complete set: Conscript Helm (Head); Conscript Collar (Neck); Conscript Chest (Torso); Conscript Gloves (Gloves); Conscript Boots (Boots); Iron Ring (Ring1); Worn Band (Ring2). The mount, Draft Horse, is NOT in this image — it is drawn separately.

This set's signature, visible in every piece: Undyed brown leather and bare grey steel. No insignia, no colour, no decoration whatsoever — this is the kit a recruit is handed.
```

**Weir** — `assets/icons/body/set_weir.png`

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Simple: three materials, about five shapes, and ONE functional detail such as a strap, a buckle or a stamped mark. It will be viewed at about 350 pixels tall, so keep detail at the scale of a buckle, never a stitch:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One figure, centred. Straight-on. Flat even light with no
implied direction. No halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Portrait format, 2:3 (1024 x 1536). One standing human figure, front-facing, weight even on both
feet, feet a little apart, arms held slightly away from the body with the palms turned in. The
figure fills the height with a small margin above the head and below the feet. No face — a smooth
featureless head. No weapon, no ground, no shadow under the feet, transparent background.

The same figure, same pose, same framing, now wearing the complete set: Weir Kettle Helm (Head); Weir Gorget (Neck); Weir Brigandine (Torso); Weir Handguards (Gloves); Weir Marchboots (Boots); Causeway Seal Ring (Ring1); Marchwarden's Band (Ring2). The mount, Weir Courser, is NOT in this image — it is drawn separately.

This set's signature, visible in every piece: Riveted iron bands over olive-green canvas, and a stamped square tower mark. Frontier issue: functional, squared-off, no curves. No lamps.
```

**Warrens** — `assets/icons/body/set_warrens.png`

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Simple: three materials, about five shapes, and ONE functional detail such as a strap, a buckle or a stamped mark. It will be viewed at about 350 pixels tall, so keep detail at the scale of a buckle, never a stitch:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One figure, centred. Straight-on. Flat even light with no
implied direction. No halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Portrait format, 2:3 (1024 x 1536). One standing human figure, front-facing, weight even on both
feet, feet a little apart, arms held slightly away from the body with the palms turned in. The
figure fills the height with a small margin above the head and below the feet. No face — a smooth
featureless head. No weapon, no ground, no shadow under the feet, transparent background.

The same figure, same pose, same framing, now wearing the complete set: Warrens Lamp-Hood (Head); Ratter's Choker (Neck); Warrens Jack (Torso); Ratter's Grips (Gloves); Warrens Treads (Boots); Tunnel-Warden's Seal (Ring1); Knuckle-Ring of the Weir (Ring2). The mount, Warrens Pit-Pony, is NOT in this image — it is drawn separately.

This set's signature, visible in every piece: Soot-blackened leather with brass fittings, and tusk or tooth accents. Where a lamp appears it is a CAGED PIT LAMP — squat, barred, underground.
```

**Marchwatch** — `assets/icons/body/set_marchwatch.png`

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Simple: three materials, about five shapes, and ONE functional detail such as a strap, a buckle or a stamped mark. It will be viewed at about 350 pixels tall, so keep detail at the scale of a buckle, never a stitch:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One figure, centred. Straight-on. Flat even light with no
implied direction. No halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Portrait format, 2:3 (1024 x 1536). One standing human figure, front-facing, weight even on both
feet, feet a little apart, arms held slightly away from the body with the palms turned in. The
figure fills the height with a small margin above the head and below the feet. No face — a smooth
featureless head. No weapon, no ground, no shadow under the feet, transparent background.

The same figure, same pose, same framing, now wearing the complete set: Marchwatch Helm (Head); Watchman's Gorget (Neck); Long Marchwatch Coat (Torso); Watch Mitts (Gloves); Marchwatch Boots (Boots); Ring of the Standing Watch (Ring1); Tally-Ring (Ring2). The mount, Marchwatch Rounder, is NOT in this image — it is drawn separately.

This set's signature, visible in every piece: Long oiled wool in slate grey and waxed storm-cloth. Draped, caped, weatherproof silhouettes. Almost no metal beyond a single pin. No lamps.
```

**Relay** — `assets/icons/body/set_relay.png`

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at about 350 pixels tall, so keep detail at the scale of a buckle, never a stitch:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One figure, centred. Straight-on. Flat even light with no
implied direction. No halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Portrait format, 2:3 (1024 x 1536). One standing human figure, front-facing, weight even on both
feet, feet a little apart, arms held slightly away from the body with the palms turned in. The
figure fills the height with a small margin above the head and below the feet. No face — a smooth
featureless head. No weapon, no ground, no shadow under the feet, transparent background.

The same figure, same pose, same framing, now wearing the complete set: Relaykeeper's Hood (Head); Torc of the Quiet Wind (Neck); Relay Coat (Torso); Lampwright's Grips (Gloves); Frostmere Treads (Boots); Lamplighter's Seal (Ring1); Vaultkeeper's Band (Ring2). The mount, Relay Charger, is NOT in this image — it is drawn separately.

This set's signature, visible in every piece: Cream canvas with blue piping and brass fittings. Where a lamp appears it is a CLEAR SIGNAL LAMP — tall, glass-sided, amber lens. Never caged.
```

**Gravewarden** — `assets/icons/body/set_gravewarden.png`

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at about 350 pixels tall, so keep detail at the scale of a buckle, never a stitch:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One figure, centred. Straight-on. Flat even light with no
implied direction. No halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Portrait format, 2:3 (1024 x 1536). One standing human figure, front-facing, weight even on both
feet, feet a little apart, arms held slightly away from the body with the palms turned in. The
figure fills the height with a small margin above the head and below the feet. No face — a smooth
featureless head. No weapon, no ground, no shadow under the feet, transparent background.

The same figure, same pose, same framing, now wearing the complete set: Gravewarden's Hood (Head); Seal of the Quiet Ground (Neck); Gravewarden's Coat (Torso); Gravewarden's Gauntlets (Gloves); Marsh Wades (Boots); Binding Band (Ring1); Warden's Signet (Ring2). The mount, Gravewarden's Dray, is NOT in this image — it is drawn separately.

This set's signature, visible in every piece: Heavy dark canvas over barrow-iron plate, black pitch seals, and one chalk-white line of gravesalt. Sombre, buried, weighted.
```

**Drowned** — `assets/icons/body/set_drowned.png`

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Three materials, about six shapes. One decorative element beyond pure function. It will be viewed at about 350 pixels tall, so keep detail at the scale of a buckle, never a stitch:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One figure, centred. Straight-on. Flat even light with no
implied direction. No halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Portrait format, 2:3 (1024 x 1536). One standing human figure, front-facing, weight even on both
feet, feet a little apart, arms held slightly away from the body with the palms turned in. The
figure fills the height with a small margin above the head and below the feet. No face — a smooth
featureless head. No weapon, no ground, no shadow under the feet, transparent background.

The same figure, same pose, same framing, now wearing the complete set: Salvager's Helm (Head); Tidewatch Torc (Neck); Salvage Harness (Torso); Salvager's Grips (Gloves); Shalewalkers (Boots); Vault-Seal Ring (Ring1); Barnacle Band (Ring2). The mount, Coastwise Courser, is NOT in this image — it is drawn separately.

This set's signature, visible in every piece: Sealed collars, thick glass plate, cork-and-iron soles and green verdigris copper. Everything looks watertight.
```

**Sable Vein** — `assets/icons/body/set_sable_vein.png`

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at about 350 pixels tall, so keep detail at the scale of a buckle, never a stitch:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One figure, centred. Straight-on. Flat even light with no
implied direction. No halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Portrait format, 2:3 (1024 x 1536). One standing human figure, front-facing, weight even on both
feet, feet a little apart, arms held slightly away from the body with the palms turned in. The
figure fills the height with a small margin above the head and below the feet. No face — a smooth
featureless head. No weapon, no ground, no shadow under the feet, transparent background.

The same figure, same pose, same framing, now wearing the complete set: Sable Vein Circlet (Head); Threnody Collar (Neck); Mantle of the Sable Vein (Torso); Archivist's Grips (Gloves); Stair-Worn Boots (Boots); House Sable Seal (Ring1); Vein-Cut Band (Ring2). The mount, Sable Courser, is NOT in this image — it is drawn separately.

This set's signature, visible in every piece: Matte black with a single gold lozenge and sable-thread edging. Severe, narrow, aristocratic.
```

**Wroughtbreaker** — `assets/icons/body/set_wroughtbreaker.png`

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at about 350 pixels tall, so keep detail at the scale of a buckle, never a stitch:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One figure, centred. Straight-on. Flat even light with no
implied direction. No halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Portrait format, 2:3 (1024 x 1536). One standing human figure, front-facing, weight even on both
feet, feet a little apart, arms held slightly away from the body with the palms turned in. The
figure fills the height with a small margin above the head and below the feet. No face — a smooth
featureless head. No weapon, no ground, no shadow under the feet, transparent background.

The same figure, same pose, same framing, now wearing the complete set: Breaker's Visor (Head); Siege Collar (Neck); Wroughtbreaker Plate (Torso); Wroughtbreaker Gauntlets (Gloves); Breaker's Sabatons (Boots); Sigil-Key Ring (Ring1); Breaker's Band (Ring2). The mount, Siege Destrier, is NOT in this image — it is drawn separately.

This set's signature, visible in every piece: Blunt lead-grey slabs with an orange cracked-core glow in the seams. Industrial, heavy, siege equipment rather than armour.
```

**Choir** — `assets/icons/body/set_choir.png`

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four materials, about seven shapes. A simple repeating motif, and one inlay or set stone. Still flat and readable, never filigree. It will be viewed at about 350 pixels tall, so keep detail at the scale of a buckle, never a stitch:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One figure, centred. Straight-on. Flat even light with no
implied direction. No halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Portrait format, 2:3 (1024 x 1536). One standing human figure, front-facing, weight even on both
feet, feet a little apart, arms held slightly away from the body with the palms turned in. The
figure fills the height with a small margin above the head and below the feet. No face — a smooth
featureless head. No weapon, no ground, no shadow under the feet, transparent background.

The same figure, same pose, same framing, now wearing the complete set: Listener's Circlet (Head); Resonant Stole (Neck); Dawnward Vestment (Torso); Cantor's Wraps (Gloves); Vigil Slippers (Boots); Ring of the First Answer (Ring1); Attendant's Band (Ring2). The mount, Choir Palfrey, is NOT in this image — it is drawn separately.

This set's signature, visible in every piece: Resonant brass and pale bone-white, in bell and tuning-fork shapes. Deliberately open at the ears and throat.
```

**Pano** — `assets/icons/body/set_pano.png`

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four or five materials, about nine shapes — the most detailed tier, and still no filigree. One distinctive silhouette flourish, and one precious or glowing inlay. It will be viewed at about 350 pixels tall, so keep detail at the scale of a buckle, never a stitch:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One figure, centred. Straight-on. Flat even light with no
implied direction. No halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Portrait format, 2:3 (1024 x 1536). One standing human figure, front-facing, weight even on both
feet, feet a little apart, arms held slightly away from the body with the palms turned in. The
figure fills the height with a small margin above the head and below the feet. No face — a smooth
featureless head. No weapon, no ground, no shadow under the feet, transparent background.

The same figure, same pose, same framing, now wearing the complete set: Pano's War Helm (Head); Pano's Amulet (Neck); Pano's Cuirass (Torso); Pano's Gauntlets (Gloves); Pano's Greaves (Boots); Pano's Signet (Ring1); Pano's Band (Ring2). The mount, Pano's Steed, is NOT in this image — it is drawn separately.

This set's signature, visible in every piece: White enamel, deep blue and gold, carrying a four-pointed star.
```

**Sovereign** — `assets/icons/body/set_sovereign.png`

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four or five materials, about nine shapes — the most detailed tier, and still no filigree. One distinctive silhouette flourish, and one precious or glowing inlay. It will be viewed at about 350 pixels tall, so keep detail at the scale of a buckle, never a stitch:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One figure, centred. Straight-on. Flat even light with no
implied direction. No halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Portrait format, 2:3 (1024 x 1536). One standing human figure, front-facing, weight even on both
feet, feet a little apart, arms held slightly away from the body with the palms turned in. The
figure fills the height with a small margin above the head and below the feet. No face — a smooth
featureless head. No weapon, no ground, no shadow under the feet, transparent background.

The same figure, same pose, same framing, now wearing the complete set: Sovereign's Diadem (Head); Collar of the Founding Pact (Neck); Regalia Cuirass (Torso); Sovereign's Gauntlets (Gloves); Regalia Greaves (Boots); Sovereign's Signet (Ring1); Tithe-Band (Ring2). The mount, Sovereign's Wyrm, is NOT in this image — it is drawn separately.

This set's signature, visible in every piece: Deep crimson dragon scale and antique gold, with scale-plate edges.
```

**Stoned Devil** — `assets/icons/body/set_stoned_devil.png`

```
Flat vector game icon. Solid colour fills only. Where a form needs shading, use ONE darker flat
shape with a hard edge — never a gradient, never an airbrush, never a gloss or specular highlight.
No black keyline; where an edge is needed use a thin line in a darker tone of the object's own
colour.

Four or five materials, about nine shapes — the most detailed tier, and still no filigree. One distinctive silhouette flourish, and one precious or glowing inlay. It will be viewed at about 350 pixels tall, so keep detail at the scale of a buckle, never a stitch:
no stitching, no rivets, no scratches, no wood grain, no hairline engraving.

One figure, centred. Straight-on. Flat even light with no
implied direction. No halo or glow around the cutout.

Save with a genuinely transparent background — real alpha in the PNG. Do not DRAW a checkerboard;
the grey-and-white chequer is how an editor displays transparency, not something to paint.

Portrait format, 2:3 (1024 x 1536). One standing human figure, front-facing, weight even on both
feet, feet a little apart, arms held slightly away from the body with the palms turned in. The
figure fills the height with a small margin above the head and below the feet. No face — a smooth
featureless head. No weapon, no ground, no shadow under the feet, transparent background.

The same figure, same pose, same framing, now wearing the complete set: The Sundown Ember (Head); The Perpetual Censer (Neck); Robe of the Long Sabbatical (Torso); Mitts of Amiable Menace (Gloves); Slippers of the Unwalked Path (Boots); Signet of Declined Ascendancy (Ring1); Band of the Fourth Reconsideration (Ring2). The mount, Extremely Relaxed Hell-Goat, is NOT in this image — it is drawn separately.

This set's signature, visible in every piece: Deep maroon and cream with aged brass. Soft, draped, unhurried shapes — nothing sharp anywhere in the set.
```

---

