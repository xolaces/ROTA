# ROTA — Visual Asset Inventory (for image generation)

Exhaustive list of every visual asset the game references, swept from the content JSON
(`items/gear/magics/units/legions/raids/guild_raids/gauntlet_*/quests/achievements/masteries`) and the
Unity client UI. **~194 individual images across 15 groups.** Keys map to the `artKey`/`iconPath`/`id`
the code expects (or a descriptive slug for UI chrome).

Rarity ladder: **Grey · White · Green · Blue · Purple · Orange** (Orange = ceiling).

---

## How to use this (reusable families — make these ONCE)
Most of the 194 surfaces are driven by a small number of shared treatments. Lock these style families
first and the rest is tinting/recoloring:

- **Rarity border family (×6)** — one item/gear/magic frame, tinted per rarity. Reused by Items, Gear,
  Magics, Sigils, Trophies, Unit cards. The single biggest dedupe.
- **Rarity swatch family (×6)** — small colored dots for drop overlays + every rarity list.
- **Generic Zone Guardian** — one templated "Guardian of <Zone>" boss recolored per biome, covering the
  ~23 un-named quest-zone bosses (`c1z1b`..`c6z4b`).
- **Zone biome tiles (~8)** — 25 named zones collapse to ruins/ash/ember/gloom-spire/water/ice/magma/void.
- **Formula-extended Gauntlet dragon** — one reusable dragon covers ladder stages 7–250 (1–6 are bespoke).
- **Named quest bosses reuse World-raid art** — Iron Colossus & Malachar share one splash between the raid
  list and their quest-boss nodes (no separate art).
- **Pano's Orange set (8 slots)** — one cohesive "lost vanguard" gold-trimmed dark-steel family.
- **Conscript starter set (8 slots)** — one "battered recruit" Grey family.
- **Contribution badges (×6)** + **difficulty chips (×4)** — each one shape recolored/numbered per tier.
- **Bottom-nav glyphs (×6)** + **class-tier icons (×7)** + **pinnacle/aura magics (Orange)** — coherent families.

### Suggested generation order (biggest payoff first)
1. **Chrome families** — 6 rarity borders + 6 rarity swatches + 6 resource/currency icons + 6 nav glyphs
   (these touch *every* screen via the persistent header/nav and every rarity item).
2. **Showpieces** — 5 World/Guild raid bosses (+ Iron Colossus/Malachar double as quest bosses), 4 Mastery
   Ancients, 3 Gauntlet trophies, the matched 8-piece Pano's + 8-piece Conscript sets.
3. **Map** — 6 chapter plates + ~8 biome zone tiles + the generic zone-guardian.
4. **Defer** — 8 collectible boss-sigils, achievement badges, one-off overlay/panel chrome.

≈ **36 "family-anchor" images unlock the great majority of the ~194 surfaces.**

---

## 1. Items & Gear (23)
| key | name | rarity | × | depicts |
|-----|------|--------|---|---------|
| mat_iron_shard | Iron Shard | Grey | 1 | Crafting fragment of the Iron Colossus |
| mat_arcane_dust | Arcane Dust | White | 1 | Residual arcane energy crafting material |
| statbag_minor | Minor Stat Bag | Green | 1 | Pouch granting 5 skill points |
| statbag_major | Major Stat Bag | Blue | 1 | Pouch granting 15 skill points |
| sigil_ironcolossus | Iron Sigil (summon item) | — | 4 | Inventory summon-sigil, 4 difficulty variants (White/Green/Blue/Purple) |
| sigil_malachar | Malachar's Sigil (summon item) | — | 4 | Inventory summon-sigil, 4 difficulty variants |
| gear_conscript_helm | Conscript Helm | Grey | 1 | Head — battered recruit helm |
| gear_conscript_collar | Conscript Collar | Grey | 1 | Neck — crude neck guard |
| gear_conscript_chest | Conscript Chest | Grey | 1 | Torso — padded leather |
| gear_iron_ring | Iron Ring | Grey | 1 | Ring1 — plain iron ring |
| gear_worn_band | Worn Band | Grey | 1 | Ring2 — scratched powerless band |
| gear_draft_horse | Draft Horse | Grey | 1 | Mount — sturdy workhorse |
| gear_conscript_boots | Conscript Boots | Grey | 1 | Boots — worn leather |
| gear_conscript_gloves | Conscript Gloves | Grey | 1 | Gloves — rough cloth |
| gear_pano_helm | Pano's War Helm | Orange | 1 | Head — legendary questing helm |
| gear_pano_amulet | Pano's Amulet | Orange | 1 | Neck — questing amulet |
| gear_pano_cuirass | Pano's Cuirass | Orange | 1 | Torso — famed breastplate (also Gauntlet shop) |
| gear_pano_signet | Pano's Signet | Orange | 1 | Ring1 — strike-sharpening signet |
| gear_pano_band | Pano's Band | Orange | 1 | Ring2 — companion band |
| gear_pano_steed | Pano's Steed | Orange | 1 | Mount — tireless warhorse (also Gauntlet shop) |
| gear_pano_greaves | Pano's Greaves | Orange | 1 | Boots — surefooted greaves |
| gear_pano_gauntlets | Pano's Gauntlets | Orange | 1 | Gloves — crushing gauntlets |

## 2. Magics (18)
| key | name | rarity | depicts |
|-----|------|--------|---------|
| magic_whetstone | Whetstone | White | Damage — sharpening proc |
| magic_lesser_poison | Lesser Poison | White | Damage — weak venom |
| magic_poison | Poison | Green | Damage — reliable venom |
| magic_greater_poison | Greater Poison | Blue | Damage — potent venom |
| magic_smite | Smite | Blue | Damage — divine strike |
| magic_blessing_of_might | Blessing of Might | Blue | Damage — empowering boon |
| magic_impending_doom | Impending Doom | Purple | Damage — catastrophic curse (Nightmare) |
| magic_expose_weakness | Expose Weakness | Green | Crit — flat crit chance |
| magic_midas_touch | Midas Touch | Green | Gold — +gold proc |
| magic_kindling | Kindling | White | Leveling — 2× XP proc |
| magic_pinnacle_5000 | Luminary's Echo | Orange | Inert pinnacle placeholder (L5000) |
| magic_pinnacle_7500 | Archon's Decree | Orange | Inert pinnacle placeholder (L7500) |
| magic_pinnacle_10000 | Ancient's Wrath | Orange | Inert pinnacle placeholder (L10000) |
| magic_pinnacle_15000 | Elder Resonance | Orange | Inert pinnacle placeholder (L15000) |
| magic_pinnacle_25000 | Eternal Aspect | Orange | Inert pinnacle placeholder (L25000) |
| magic_wrath_of_the_ancients | Wrath of the Ancients | Orange | Gauntlet rank-1 off-cap aura |
| magic_blessing_of_the_ancients | Blessing of the Ancients | Orange | Gauntlet ranks 2–10 off-cap aura |
| magic-icon-slot | Magic Cast Slot Frame | — | 46×46 in-raid magic-slot frame (chrome) |

## 3. Units & Legions (11)
| key | name | rarity | depicts |
|-----|------|--------|---------|
| gen_ironward | Ironward the Steadfast | Green | General portrait — Human Tank |
| gen_ashblade | Ashblade | Blue | General portrait — Human Melee |
| gen_sylvaire | Sylvaire | Blue | General portrait — Elf Ranged |
| gen_morvath | Morvath the Unliving | Purple | General portrait — Undead Special (also Gauntlet shop) |
| troop_militia | Conscript Militia | White | Troop portrait — Human foot soldiers |
| troop_archers | Wood Archers | Green | Troop portrait — Elf ranged |
| troop_pikemen | Iron Pikemen | Green | Troop portrait — Human tank line |
| troop_acolytes | Shadow Acolytes | Blue | Troop portrait — Undead casters |
| legion_warband | Free Warband | White | Legion banner — starting legion |
| legion_vanguard | Dawn Vanguard | Blue | Legion banner — disciplined formation |
| legion_ironlegion | The Iron Legion | Purple | Legion banner — specialist (also Gauntlet shop) |

## 4. Raids & Bosses (7)
| key | name | rarity | depicts |
|-----|------|--------|---------|
| raid_ironcolossus | The Iron Colossus | Orange | World boss / Ch1 quest boss (same art) — metal colossus |
| raid_malachar | Lord Malachar | Orange | World boss / Ch2 quest boss (same art) — dark sorcerer |
| guild_raid_warlord | Gorehowl the Warlord | Orange | Guild boss — bloodied warlord (500k HP) |
| guild_raid_leviathan | The Sunken Leviathan | Orange | Guild boss — aquatic horror (1.5M HP) |
| guild_raid_titan | Kronarch, World-Ender | Orange | Guild boss — apocalyptic titan (5M HP) |
| zone_guardian_generic | Generic Zone Guardian | — | Templated boss for ~23 un-named quest-zone bosses (recolor per zone) |
| combat-emblem | Boss Combat Emblem Frame | — | Circular 150×150 boss emblem chrome in RaidCombatView |

## 5. Gauntlet (13)
| key | name | rarity | depicts |
|-----|------|--------|---------|
| gauntlet_stage_1 | Whelp Warden | Orange | Ladder boss 1 — young dragon warden |
| gauntlet_stage_2 | Drake Sentinel | Orange | Ladder boss 2 |
| gauntlet_stage_3 | Wyrm Vanguard | Orange | Ladder boss 3 |
| gauntlet_stage_4 | Elder Drake | Orange | Ladder boss 4 |
| gauntlet_stage_5 | Ancient Wyrm | Orange | Ladder boss 5 |
| gauntlet_stage_6 | Dragon Sovereign | Orange | Ladder boss 6 |
| gauntlet_generic_mob | Formula-Extended Opponent | Orange | Reusable dragon for stages 7–250 |
| trophy_aureate | Aureate Trophy | Orange | Top trophy (+25% Legion Power) |
| trophy_argent | Argent Trophy | Blue | Mid trophy (+10% Legion Power) |
| trophy_bronzed | Bronzed Trophy | White | Entry trophy (+5% Legion Power) |
| currency_token | Gauntlet Token | — | Primary Gauntlet shop currency |
| currency_strike | Strike | — | Gauntlet attack-action currency |
| currency_pitchfork | Pitchfork | — | Premium Gauntlet currency |

## 6. Masteries / Ancients (4)
| key | name | rarity | depicts |
|-----|------|--------|---------|
| mastery_wrath | Wrath, the Wrathfire | Orange | Rage/war Ancient (+% legion power) |
| mastery_bulwark | Bulwark, the Mountain | Orange | Guild-defense Ancient (+% guild-raid dmg) |
| mastery_hoard | Hoard, the Greed | Orange | Plunder Ancient (+% drop/gold) |
| mastery_discernment | Discernment, the Veiled Eye | Orange | Sight Ancient (+% drop quality/sigil find) |

## 7. Quest Map & Zones (16)
| key | name | depicts |
|-----|------|---------|
| chapter_1 | Chapter 1 Plate | Ruined-keep theme (Old Guard Ruins, Ashen Causeway, Hollow Marches) |
| chapter_2 | Chapter 2 Plate | Ember/forest (Vanguard Approach, Emberfall Reach, Cinderwood, Gloomspire) |
| chapter_3 | Chapter 3 Plate | Fortress/vault (Keepwall, Sunken Vaults, Throne Approach, Shattered Spire) |
| chapter_4 | Chapter 4 Plate | Frozen (Rimewood, Frostmere, Glacier Maw, Pale Citadel) |
| chapter_5 | Chapter 5 Plate | Volcanic (Dustfall, Emberpan, Magma Rift, Ashen Throne, Cinder Crown) |
| chapter_6 | Chapter 6 Plate | Cosmic/void (Twilight Gate, Star Hollow, Void Threshold, Eternal Stair, Throne of Ancients) |
| zone_tile_ruins | Zone Tile: Ruins/Keep | Biome tile (ruined stone) |
| zone_tile_ash | Zone Tile: Ash/Hollow | Biome tile (grey ash) |
| zone_tile_ember | Zone Tile: Ember/Cinder | Biome tile (smoldering) |
| zone_tile_gloom | Zone Tile: Gloom/Spire | Biome tile (dark spires) |
| zone_tile_water | Zone Tile: Sunken/Water | Biome tile (flooded) |
| zone_tile_ice | Zone Tile: Frost/Ice | Biome tile (frozen) |
| zone_tile_magma | Zone Tile: Magma/Dust | Biome tile (molten) |
| zone_tile_void | Zone Tile: Void/Cosmic | Biome tile (cosmic) |
| boss-node-indicator | Boss Node Marker | Dragon-glyph boss marker on the quest node map |
| screen-bg-quest | Quest/Campaign Background | Zone-map navigator backdrop |

## 8. Boss Sigils — Collectibles (8)
*(Distinct from the inventory summon-sigils in §1 — these are the trophy/collection variants, rarity tracks difficulty.)*
| key | name | rarity |
|-----|------|--------|
| sigil_ironcolossus_normal | Iron Colossus Sigil (Normal) | White |
| sigil_ironcolossus_hard | Iron Colossus Sigil (Hard) | Green |
| sigil_ironcolossus_legendary | Iron Colossus Sigil (Legendary) | Blue |
| sigil_ironcolossus_nightmare | Iron Colossus Sigil (Nightmare) | Purple |
| sigil_malachar_normal | Malachar Sigil (Normal) | White |
| sigil_malachar_hard | Malachar Sigil (Hard) | Green |
| sigil_malachar_legendary | Malachar Sigil (Legendary) | Blue |
| sigil_malachar_nightmare | Malachar Sigil (Nightmare) | Purple |

## 9. Achievements & Trophies (6 badges)
| key | name | depicts |
|-----|------|---------|
| ach_raid | Raid Achievement Badge | Raid-completion family (Slayer, Raid Veteran) |
| ach_quest | Quest Achievement Badge | Quest-node clearance (Pathfinder) |
| ach_boss | Boss Achievement Badge | Zone-boss clearance (Boss Breaker) |
| ach_gear | Gear Achievement Badge | Equipment ownership (Well Equipped) |
| ach_calendar | Calendar Achievement Badge | Days-played (Devoted) |
| ach_sigil | Sigil Achievement Badge | Sigil collector (Sigil Hoarder) |

## 10. UI Frames & Borders (18)
| key | name | × | depicts |
|-----|------|---|---------|
| rarity-border-family | Rarity Border Family | 6 | The shared item/gear/magic frame, tinted Grey..Orange |
| rarity-swatch-family | Rarity Swatch Family | 6 | 8×8 rarity dots for lists/drop overlays |
| panel-border-standard | Panel Border: Standard | 1 | Dark-brown card container frame |
| panel-border-accents | Home Tile Accent Borders | 1 | Recolorable per-tile left-border accent strip |
| button-primary | Button: Primary + Small | 1 | Gold primary action button + compact variant |
| button-tab | Button: Tab | 1 | Tab nav button (inactive/active gold) |
| button-nav | Button: Bottom Nav | 1 | Bottom-nav tab button frame |
| crown-fab | Crown FAB | 1 | Gold circular leaderboard FAB (also styles the Gauntlet CTA) |

## 11. Resource & Currency Icons (6)
| key | name | depicts |
|-----|------|---------|
| resource-bar-energy | Energy Icon | Quest energy (crimson) — header |
| resource-bar-stamina | Stamina Icon | Raid stamina (orange/gold) — header |
| resource-bar-guild | Guild Stamina Icon | Guild-raid stamina (purple) — header |
| resource-bar-health | Health Icon | Health (crimson) — raids/Gauntlet (T56) |
| currency-gold | Gold Icon | Gold currency — header |
| currency-gem | Gem Icon | Premium gem currency — header |

## 12. Class Icons (7 confirmed)
*(Convergence/pinnacle tiers. Tier 1–5 path/spec class icons were not enumerated in content — see Gaps.)*
| key | name | tier |
|-----|------|------|
| class-icon-conscript | Conscript | Tier-0 default |
| class-icon-luminary | Luminary | L2000 |
| class-icon-immortal | Immortal | L5000 |
| class-icon-archon | Archon | L7500 |
| class-icon-ancient | Ancient | L10000 |
| class-icon-elderancient | ElderAncient | L15000 |
| class-icon-eternal | Eternal | L25000 |

## 13. Screen Backgrounds (11)
| key | screen |
|-----|--------|
| screen-bg-home | Home landing hero band |
| screen-bg-quest | Quest/campaign zone-map (cross-listed §7) |
| screen-bg-raid | Raid screen menu |
| screen-bg-gauntlet | Gauntlet event screen |
| screen-bg-profile | Profile/equipment/inventory |
| screen-bg-bazaar | Bazaar/shop |
| screen-bg-guild | Guild roster/perks/shop/chat |
| screen-bg-masteries | Masteries (4 Ancients) |
| screen-bg-leaderboard | Leaderboard hub |
| header-bar-background | Persistent top-bar plate |
| nav-bar-background | Persistent bottom-nav plate |

## 14. Overlays & FX (10)
| key | name | depicts |
|-----|------|---------|
| overlay-levelup | Level Up Overlay | Full-screen congrats, tap-to-dismiss |
| overlay-levelup-pinnacle | Level Up Pinnacle Overlay | Milestone variant with "+gems!" callout |
| overlay-itemdrop | Item Drop Overlay | Loot card with rarity swatches (T58) |
| overlay-milestone-banner | Milestone Banner | Auto-sweep banner at key levels |
| overlay-class-gate | Class Gate Overlay | Mandatory class-selection blocker |
| gauntlet-cta-glow | Gauntlet CTA Glow | Pulsing gold glow on Home Gauntlet CTA |
| raid-hp-bar | Raid HP Bar Fill | Boss HP bar fill texture (dark red) |
| world-chat-panel | World Chat Panel | Header 💬 chat overlay chrome |
| feedback-panel | Feedback Panel | Header 🐞 bug/feedback overlay chrome |
| dev-tools-screen | Dev Tools Overlay | Header 🛠 debug overlay (debug builds) |

## 15. Profile & Badges (16)
| key | name | rarity | × | depicts |
|-----|------|--------|---|---------|
| portrait-frame-header | Portrait Frame: Header | — | 1 | 32×32 gold-bordered avatar frame |
| portrait-frame-profile | Portrait Frame: Profile | — | 1 | 64×64 gold-bordered avatar frame |
| badge-contribution-legendary1 | Contribution: Legendary 1 | Orange | 1 | Top contributor (×1.50) |
| badge-contribution-legendary2 | Contribution: Legendary 2 | Orange | 1 | ×1.25 contributor |
| badge-contribution-legendary3 | Contribution: Legendary 3 | Orange | 1 | ×1.10 contributor |
| badge-contribution-epic | Contribution: Epic | Purple | 1 | Top 10% (×1.00) |
| badge-contribution-rare | Contribution: Rare | Green | 1 | Threshold (×0.75) |
| badge-contribution-participant | Contribution: Participant | White | 1 | Everyone who hit (×0.25) |
| difficulty-chip-normal | Difficulty Chip: Normal | Green | 1 | Green Normal chip |
| difficulty-chip-hard | Difficulty Chip: Hard | — | 1 | Yellow Hard chip |
| difficulty-chip-legendary | Difficulty Chip: Legendary | — | 1 | Red Legendary chip |
| difficulty-chip-nightmare | Difficulty Chip: Nightmare | Purple | 1 | Purple Nightmare chip |
| nav-glyph-set | Bottom Nav Glyph Set | — | 6 | Home/Quest/Raids/Legion/Profile/Guild glyphs |
| username-context-menu | Username Context Menu | — | 1 | Edit/copy/report menu chrome |

---

## Gaps / follow-ups (not yet in content, will need art later)
- **Tier 1–5 class icons** — Conscript→Tier2 paths→Tier3 specs→Legendary→Ascendant are defined as an enum
  but have no per-class art enumerated. Only the convergence/pinnacle tiers (§12) were found.
- **Mastery tier/title emblems** — beyond the 4 Ancient icons, each Ancient levels 1→5 with derived titles
  (may want small rank pips/title frames).
- **Achievement points / tier-chain icons** — the badges cover categories; tiered achievements may want
  bronze/silver/gold variants.
- **Guild crest system** — guilds have a `crestId`; if crests become art, that's a crest-piece kit.
