# ROTA — Game Design Reference & Unity UI Blueprint
**Rise of the Ancients | Inspired by Dawn of the Dragons**
*Authored for developer handoff and Unity UI implementation*
---
## PART 1 — DAWN OF THE DRAGONS: MECHANICS ANALYSIS
*What made it work, what broke it, and what ROTA does differently*
---
### 1.1 Core Game Loop
DotD's loop was deceptively simple and deeply addictive:

```
Login → Spend Energy on Quests → Earn XP/Gold/Loot
     → Spend Stamina on Raids  → Earn better Loot/Generals
     → Equip Loot              → Deal more Raid damage
     → Join Guild              → Spend Honor on Guild Raids
     → Earn Guild Rewards      → Repeat at higher power
```

Everything fed everything else. Quests unlocked raids. Raids dropped gear. Gear improved raid damage. Guild raids required guild membership. The loop had no dead ends — if you were out of one resource, another was ready.
**ROTA adopts this loop.** The critical difference: ROTA enforces hard power caps at every layer. DotD's loop eventually broke because there was no ceiling.
---
### 1.2 Resource System
DotD used four resource bars. ROTA maps directly to these with one addition:
| DotD Resource | Used For | ROTA Equivalent | ROTA Name |
|---|---|---|---|
| Energy | Quests | Energy | Energy |
| Stamina | World Raids, PvP | Stamina | Stamina |
| Honor | Guild Raids, Jousting | GuildStamina | Guild Stamina |
| Health | Taking damage in raids/PvP | Derived stat | CurrentHealth (PlayerStats) |
**Key DotD mechanic ROTA keeps:** Resources regenerate on a server-side timer. They fill completely on level-up. This creates the "level-up rush" — players push for the next level specifically to refill all bars.
**Key DotD mechanic ROTA fixes:** In DotD, players could invest skill points into Energy and Stamina caps indefinitely — LSI (Large Stamina Increment) had a soft cap but the grind was brutal. ROTA enforces a hard server-side cap on all resource pools. Max pool size scales with level via a formula, not open-ended investment.
**ROTA Resource Regen Formula (server-authoritative):**

```
CurrentValue = MIN(
    StoredCheckpoint + (MinutesSinceLastRegenAt × RegenPerMinute),
    MaxValue
)
```

The client never computes this. It requests the current value from the server on every screen load.
---
### 1.3 Quest System
DotD's quest structure was a **chapter-based zone map**. Each zone was a geographic region of West Kruna rendered as a painted backdrop with node icons overlaid.
**Zone structure:**
- Each zone had 10–20 quest nodes
- Nodes unlocked sequentially (complete node 3 to unlock node 4)
- Each node had: Normal / Hard / Legendary / Nightmare difficulty tiers
  Colors: Normal=Green, Hard=Yellow, Legendary=Red, Nightmare=Purple
- Harder difficulties cost more Energy but gave better loot
- Boss nodes at the end of each zone triggered a **Raid summon** — the boss appeared as a world raid others could join
- Completing a zone unlocked the next zone/chapter
**What made it compelling:**
- The painted fantasy art gave each zone a strong sense of place
- Boss nodes created a social moment — summon it, share the link, watch others pile in
- Nightmare/Legendary gave veterans a reason to revisit old content
- Perception stat added RNG reward variance (rare drops during questing)
**ROTA Quest Implementation Plan:**

```
Quest Map Structure:
  Chapter 1: "The Ruined Frontier"    (Zones 1–3, tutorial pacing)
  Chapter 2: "The Dragon's Shadow"    (Zones 4–7, first guild content)
  Chapter 3: "The Ancient Keep"       (Zones 8–12, raid-centric)
  ...
Each Zone:
  - 8–15 quest nodes
  - Node types: Story, Battle, Exploration, Boss (raid trigger)
  - Difficulty: Normal (Green) → Hard (Yellow) → Legendary (Red) → Nightmare (Purple)
  - Prerequisites: previous node completion
  - Rewards: Gold, XP, Gems (rare), Equipment (random from loot table), Raid Key (boss nodes)
Quest Definition (content/quests.json — static, loaded at startup):
{
  "id": "z1_q01",
  "zoneId": "zone_01",
  "chapterId": "chapter_01",
  "name": "The Fallen Gate",
  "nodeType": "Battle",
  "baseEnergyCost": 5,
  "baseXpRatio": 1.25,
  "lootTableId": "lt_zone1_common",
  "sigilDropChance": 0,
  "sigils": null,
  "prerequisiteQuestId": null,
  "triggersRaidId": null,
  "artKey": "zone1_gate"
}
Note: difficulty is NOT stored per-definition. It is passed at attempt
time (Normal/Hard/Legendary/Nightmare). The server applies energy and
reward multipliers based on the requested difficulty.
```

**Adding quests = adding JSON entries.** No schema changes, no code changes, no redeployment required. The content pipeline is file-based.
---
### 1.4 Raid System
This was DotD's defining feature and social engine.
**How World Raids worked:**
1. Player completes a Boss quest node → Boss spawns as a World Raid
2. Raid has an HP pool (scales with difficulty, 1M–100M+ HP)
3. Raid has a timer (24–72 hours) — if not killed in time, it expires
4. Any player can join an active raid using Stamina
5. Hit sizes: ×1, ×5, ×20 Stamina — bigger hits deal more damage
6. Damage contribution determined loot tier: threshold buckets (e.g., deal >1% total = Tier A loot)
7. Loot awarded on kill — better contribution = better loot table roll
8. Raid boss could "counter-attack" and damage player Health
9. World raid link was shareable — social virality built in
**How Guild Raids worked:**
- Guild officer summons a Guild Raid Boss (costs Guild Stamina / Honor)
- Only guild members can participate
- Uses Honor resource instead of Stamina
- Guild-exclusive loot pool — gear not available in world raids
- Guild XP and Guild Reputation earned alongside personal rewards
- Contributed to guild level progression
**ROTA Raid Design (Phase 1 Beta = solo summoning; Phase 2 = full async co-op):**

```
Phase 1 Beta Raid:
  - Player summons from Raid Screen (costs Stamina or Raid Key from quest)
  - Solo encounter — player attacks until boss dead or timer expires
  - Server resolves all damage (never client)
  - Loot determined server-side from weighted table
  - Tests the full loot/XP/damage/timer pipeline
Phase 2 Full Raid:
  - Async co-op (players attack independently, not simultaneously)
  - Boss HP shared across all participants
  - Contribution tracking per player
  - Loot tier buckets: Legendary (top 3 contributors), Epic (top 10%), Rare (threshold), Participant
    Multipliers: Legendary Rank 1=×1.50, Rank 2=×1.25, Rank 3=×1.10,
                 Epic=×1.00, Rare=×0.75, Participant=×0.25
  - 48-hour timer
  - Guild Raids: honor-gated, guild-only, guild XP rewards
  - Raid feed: live updates of who hit and for how much
```

**Anti-exploit rules on raids (enforced server-side):**
- Damage computed from player's server-stored stats + equipped items only
- No client damage values ever accepted
- Rate limit: max 1 raid hit per 500ms per player
- Idempotency: each hit has a UUID — duplicate submissions ignored
- Timer checked server-side — expired raids return 410 Gone
---
### 1.5 Legion System
DotD's Legion was a formation of Generals and Troops that multiplied raid damage.
**How it worked:**
- Player had one active Legion at a time (later could switch between saved legions)
- Legion had formation slots: General slots (e.g., 6–12) + Troop slots (e.g., 48–96)
- Each General had a class (Warrior, Mage, Healer, Rogue) and stats
- Formation type determined slot layout and bonus multipliers
- Legion Power = base stats × formation bonus × general/troop bonuses
- Legion Power directly multiplied raid damage per stamina hit
**ROTA's implementation** (Phase 2 — referenced here for architecture planning):

```
Legion Power feeds into:
  - World Raid damage per stamina hit
  - Guild Raid damage per honor hit
  - (Phase 3) PvP combat power
Server computes: EffectiveLegionPower = BasePower × Σ(GeneralBonuses) × FormationMultiplier
Hard cap: LegionPower cap enforced per tier. Stacking generals of same type 
          triggers diminishing returns after 3 of the same class.
```

---
### 1.6 Guild System
DotD's guild system was its retention engine. Players stayed for their guild, not just the content.
**Guild features:**
- Guild Hall: roster, officer ranks, guild level, guild XP
- Guild Chat: real-time (SignalR in ROTA)
- Guild Raids: summoned by officers, participated by members using Honor
- Guild Reputation (GRep): personal currency earned in guild raids, spent in Guild Shop
- Guild XP (GXP): guild-level currency, advances guild level, unlocks perks
- Guild Perks: passive bonuses (e.g., +5% raid damage, +10% gold from quests)
- Member ranks: Recruit → Member → Veteran → Officer → Guild Master
- Max members: typically 30–50 per guild
**ROTA Guild System (Phase 2):**

```
Ranks: Recruit, Member, Veteran, Officer, GuildMaster
GuildStamina (Honor equivalent): regenerates over time, used for Guild Raids
Guild Level: advances via GXP, unlocks new Guild Perks at each tier
Guild Perks: server-computed bonuses applied to all members' effective stats
Guild Shop: spend GRep on exclusive gear, cosmetics, and raid consumables
Real-time guild chat via SignalR hub
```

---
### 1.8 Item & Sigil System

ITEM RARITY (color-coded, Orange is the permanent ceiling):
  Grey → White → Green → Blue → Purple → Orange

ITEM TYPES:
  Material  — crafting ingredient dropped from raids/quests
  StatBag   — consumable granting unassigned SkillPoints
  Sigil     — single-use raid summon item
  Equipment — wearable gear (Phase 2)

SIGIL PIPELINE (replaces DotD's Essence system):
  Boss quest defeated on Normal    → drops Normal Sigil (White)
  Boss quest defeated on Hard      → drops Hard Sigil (Green)
  Boss quest defeated on Legendary → drops Legendary Sigil (Blue)
  Boss quest defeated on Nightmare → drops Nightmare Sigil (Purple)
  First defeat on each difficulty: guaranteed drop
  Subsequent defeats: 15% base chance (configurable per boss)
  Using a Sigil: consumed from inventory, raid summoned at
  matching difficulty with HP = baseHp × difficultyMultiplier

STAT BAG TIERS:
  Adventurer's Stat Bag (White) — 10 unassigned SkillPoints
  Champion's Stat Bag (Blue)    — 50 unassigned SkillPoints
  (Higher tiers added via items.json as content is built)

---
### 1.9 LSI & Skill Point System

SKILL POINTS:
  Granted: 10 per level-up
  Spent via: AllocateStatPointAsync (server-validated)
  Stats: Energy, Stamina, Attack, Defense, Health,
         Discernment (renamed from Perception)

LSI (Leveling Speed Index):
  Formula: (EnergyInvestment + (StaminaInvestment × 2))
           / Player.Level
  Cap: 9.0 (faster than DotD's 7 for modern retention)
  Gear bonuses can exceed the cap (same as DotD)
  At LSI 9.0, Level 100: ~300 base Stamina

DISCERNMENT (ROTA name for DotD's Perception):
  No investment cap — spend freely
  Effects (// PHASE-2): quest drop quality bonus,
  raid critical damage bonus
  Current: investment tracked, bonus effects flagged

BSI (Battle Strength Index, for reference):
  Formula: (Attack + Defense) / Player.Level
  No cap — invest freely into combat stats

---
### 1.7 What DotD Got Wrong (ROTA Fixes These)
| DotD Problem | Root Cause | ROTA Solution |
|---|---|---|
| Unlimited power scaling via Dawniversary stacking | No stack cap on anniversary bonuses | Hard stack cap on every bonus, defined before implementation |
| Pay-to-win Planet Coin generals | Premium generals far exceeded free ones | Gems only earn energy refills in beta; content drives power, not spending |
| Honor cap tied to level (impossible to raise) | Bad design decision | GuildStamina scales with level via formula, not fixed |
| No damage cap in raids = whale domination | Unbounded contribution rewarded exponentially | Contribution tier buckets — diminishing loot returns above threshold |
| Flash performance issues, no mobile-first design | Tech debt from 2010 Flash | Unity standalone PC/Mac, clean from scratch |
| Raid timer pressure = whale spending | Pay to refill and keep attacking | Generous timers (48h+); refill costs capped per day |
---
## PART 2 — ROTA UI SKELETON
*Screen-by-screen layout reference for Unity implementation*
---
### 2.1 Global UI Layout

```
┌─────────────────────────────────────────────────────────┐
│  HEADER BAR (persistent across all screens)              │
│  [Avatar] [Name / Level] [Energy ██░░] [Stamina ████░]  │
│  [GuildStamina ██░] [Gold: 1,240] [Gems: 15]  [⚙ Menu] │
└─────────────────────────────────────────────────────────┘
│                                                          │
│  MAIN CONTENT AREA (changes per tab)                     │
│                                                          │
└─────────────────────────────────────────────────────────┘
│  BOTTOM NAV BAR (persistent)                             │
│  [⚔ Quest] [🐉 Raid] [🏰 Guild] [🎒 Items] [👤 Profile]│
└─────────────────────────────────────────────────────────┘
```

**Header bar rules:**
- Resource bars show live values (fetched from server on screen load)
- Tapping a resource bar opens a tooltip: current/max, regen rate, time to full
- Gold and Gems tap to open the economy screen
- All values read-only on client — never editable, never trusted
---
### 2.2 Quest Screen

```
┌─────────────────────────────────────────────────────────┐
│  CHAPTER SELECTOR                                        │
│  [◀ Ch.1: The Ruined Frontier ▶]                        │
├─────────────────────────────────────────────────────────┤
│  ZONE MAP (painted fantasy art backdrop)                 │
│                                                          │
│     [Node 1 ✓]──[Node 2 ✓]──[Node 3 🔓]                │
│                               │                          │
│                         [Node 4 🔒]──[Boss 🔒]          │
│                                                          │
│  Node states:                                            │
│    ✓ = completed (green glow)                            │
│    🔓 = available (gold highlight, tap to open)          │
│    🔒 = locked (greyed out, shows prerequisite)          │
│    🐉 = boss node (dragon icon, triggers raid on clear)  │
├─────────────────────────────────────────────────────────┤
│  NODE DETAIL PANEL (slides up on tap)                    │
│  "The Fallen Gate"             [Normal ▼]               │
│  Cost: ⚡5 Energy                                        │
│  Rewards: 💰100 Gold  ⭐50 XP  [? Loot]                  │
│                        [⚔ ATTEMPT]                       │
└─────────────────────────────────────────────────────────┘
```

**Difficulty dropdown:** Normal (Green) → Hard (Yellow) → Legendary (Red) → Nightmare (Purple)
Difficulty is passed at attempt time — not stored per definition. Server applies multipliers
(energy ×1.0/1.5/2.0/3.0; rewards ×1.0/1.5/2.0/3.5). Server enforces unlock gates:
Hard requires Normal completion, Legendary requires Hard, Nightmare requires Legendary.
**Attempt flow:**
1. Player taps ATTEMPT
2. Client sends: `POST /api/quests/{questId}/attempt`
3. Server validates energy, prerequisites, player state
4. Server returns: success/failure + rewards granted + new resource values
5. Client animates reward popups, updates header bar from server response
6. Client never computes or predicts the outcome
---
### 2.3 Raid Screen

```
┌─────────────────────────────────────────────────────────┐
│  RAID SCREEN                          [🏰 Guild Raids]   │
├─────────────────────────────────────────────────────────┤
│  ACTIVE RAIDS (raids you summoned or are participating)  │
│  ┌──────────────────────────────────────────────────┐   │
│  │ 🐉 Lord Malachar          HP: ████████░░  82%    │   │
│  │ Summoned by: You          ⏱ 41:22:15 remaining   │   │
│  │ Participants: 7           Your damage: 12,400     │   │
│  │ [×1 Stamina] [×5 Stamina] [×20 Stamina]  [FLEE] │   │
│  └──────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│  WORLD RAIDS (open raids from other players)             │
│  ┌──────────────────────────────────────────────────┐   │
│  │ 🐉 The Iron Colossus      HP: ██████░░░░  61%    │   │
│  │ Summoned by: Thornwood    ⏱ 12:44:02 remaining   │   │
│  │ Participants: 23          [JOIN]                  │   │
│  └──────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│  SUMMON A RAID                                           │
│  [Select from unlocked bosses ▼]    [🔑 Use Raid Key]   │
└─────────────────────────────────────────────────────────┘
```

**Hit button flow:**
1. Player taps ×5 Stamina
2. `POST /api/raids/{raidId}/hit` with body `{ "hitSize": 5 }`
3. Server validates stamina, computes damage from server-stored stats
4. Server returns: damage dealt, new HP%, new stamina value, loot if kill
5. Client animates hit, updates HP bar and stamina from response only
**Guild Raids tab** (same screen, tab switch):

```
┌─────────────────────────────────────────────────────────┐
│  GUILD RAIDS                   Guild: Iron Vanguard      │
├─────────────────────────────────────────────────────────┤
│  ACTIVE GUILD RAID                                       │
│  ┌──────────────────────────────────────────────────┐   │
│  │ 🐉 Vorathix the Corrupted  HP: ███░░░░░  38%     │   │
│  │ Summoned by: Kaelar        ⏱ 23:11:44 remaining  │   │
│  │ Guild damage: 4,820,000    Your damage: 144,000   │   │
│  │ [×1 Honor] [×5 Honor] [×20 Honor]               │   │
│  └──────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────┤
│  [SUMMON GUILD RAID]  (Officer+ rank only)               │
│  Select boss: [Guild Boss Tier 1 ▼]                      │
│  Cost: 50 Guild Stamina (Guild has: 340)                 │
└─────────────────────────────────────────────────────────┘
```

---
### 2.4 Guild Screen

```
┌─────────────────────────────────────────────────────────┐
│  GUILD: Iron Vanguard          Level 7  [GUILD CHAT 💬]  │
│  GXP: ████████░░  4,200 / 5,000                         │
├────────────┬────────────┬───────────┬────────────────────┤
│  ROSTER    │  PERKS     │  SHOP     │  RAIDS             │
├────────────┴────────────┴───────────┴────────────────────┤
│  ROSTER TAB                                              │
│  [GuildMaster] Kaelar        Online  Lv.142              │
│  [Officer]     Thornwood     2h ago  Lv.98               │
│  [Member]      xolaces       Online  Lv.67               │
│  [Recruit]     Daryn         4d ago  Lv.23               │
│                                                          │
│  Members: 24 / 30            [+ Invite] [Leave Guild]   │
├─────────────────────────────────────────────────────────┤
│  PERKS TAB                                               │
│  ✅ Iron Discipline   +5% Stamina regen                  │
│  ✅ Dragon's Hoard    +8% Gold from quests               │
│  🔒 Siege Mastery     +10% Raid damage  (Lv.10 required) │
├─────────────────────────────────────────────────────────┤
│  GUILD CHAT (SignalR real-time)                          │
│  Kaelar: Guild raid up! Vorathix — come hit it           │
│  Thornwood: on it                                        │
│  [Type message...]                          [Send]       │
└─────────────────────────────────────────────────────────┘
```

---
### 2.5 Items / Equipment Screen

```
┌─────────────────────────────────────────────────────────┐
│  EQUIPMENT                                               │
│  ┌───────────────────────────────────────────────────┐  │
│  │     [Head]                                        │  │
│  │  [Shoulder][Chest][Back]                          │  │
│  │  [Hands]  [Legs] [Feet]                           │  │
│  │  [Weapon]        [Off-hand]                       │  │
│  │  [Ring 1] [Ring 2] [Amulet]                       │  │
│  └───────────────────────────────────────────────────┘  │
│  Effective Stats:  ATK: 420  DEF: 318  HP: 2,400         │
│  (computed server-side, displayed read-only)             │
├─────────────────────────────────────────────────────────┤
│  INVENTORY (grid layout, 6 columns)                      │
│  [item][item][item][item][item][item]                    │
│  [item][item][item][item][item][item]                    │
│  ...                                  [Filter ▼][Sort ▼] │
├─────────────────────────────────────────────────────────┤
│  ITEM DETAIL (slide-up panel on tap)                     │
│  "Warblade of the Fallen"    ⚔ Weapon  [Epic]            │
│  ATK: +85  DEF: +20                                      │
│  Special: +12% damage vs Dragon-type raids               │
│  Acquired: Lord Malachar Raid (Tier A loot)              │
│            [EQUIP]  [DISCARD]                            │
└─────────────────────────────────────────────────────────┘
```

---
### 2.6 Player Profile Screen

```
┌─────────────────────────────────────────────────────────┐
│  [Avatar — tap to change cosmetic]                       │
│  xolaces                     Level 67                    │
│  Class: Warlord               Guild: Iron Vanguard       │
├─────────────────────────────────────────────────────────┤
│  STATS                                                   │
│  Attack:  320    Defense: 248    Health: 2,400           │
│  (Base stats only — effective stats shown in equipment)  │
├─────────────────────────────────────────────────────────┤
│  RESOURCES                    Current  /  Max            │
│  ⚡ Energy                       38    /   50            │
│  💪 Stamina                      15    /   20            │
│  🛡 Guild Stamina                 8    /   10            │
│  (all values fetched live from server on screen load)    │
├─────────────────────────────────────────────────────────┤
│  PROGRESSION                                             │
│  XP: ████████░░  8,200 / 10,000  → Level 68             │
│  Gold: 4,820    Gems: 15                                 │
├─────────────────────────────────────────────────────────┤
│  [Edit Username]   [Change Class]   [Settings]           │
└─────────────────────────────────────────────────────────┘
```

---
### 2.7 Screen Navigation Map

```
                        ┌─────────┐
                        │  Login  │
                        └────┬────┘
                             │
                        ┌────▼────┐
                        │  Home   │  (summary dashboard)
                        └────┬────┘
                             │
           ┌─────────────────┼──────────────────┐
           │                 │                  │
      ┌────▼────┐      ┌─────▼─────┐      ┌────▼────┐
      │  Quest  │      │   Raid    │      │  Guild  │
      │  Screen │      │  Screen   │      │  Screen │
      └────┬────┘      └─────┬─────┘      └────┬────┘
           │                 │                  │
      ┌────▼────┐      ┌─────▼─────┐      ┌────▼────┐
      │  Node   │      │  Raid     │      │  Guild  │
      │ Detail  │      │  Detail   │      │  Chat   │
      │ (panel) │      │  (panel)  │      │ (panel) │
      └─────────┘      └─────┬─────┘      └─────────┘
                             │
                        ┌────▼────┐
                        │  Loot   │
                        │ Reward  │
                        │ (popup) │
                        └─────────┘
      ┌──────────────┐   ┌─────────────┐
      │  Items Screen│   │  Profile    │
      │  (inventory/ │   │  Screen     │
      │  equipment)  │   │             │
      └──────────────┘   └─────────────┘
```

---
## PART 3 — UNITY UI IMPLEMENTATION PROMPT
*Paste this into Claude Code when Unity work begins*
---

```
We are building the Unity client for ROTA (Rise of the Ancients).
The backend is a complete ASP.NET Core API. Read CLAUDE.md for 
backend architecture and all API endpoints.
DESIGN REFERENCE: Dawn of the Dragons (5th Planet Games, 2012).
Painted fantasy art backdrops, dark medieval color palette, 
high-contrast UI panels, glowing resource bars.
Reference file: docs/ui/ROTA_GameDesign_UI_Reference.md
TECH REQUIREMENTS:
- Unity 2022 LTS or newer (specify version in use)
- UI Toolkit (not legacy UGUI) for all screen layouts
- TextMeshPro for all text
- DOTween for animations (tween resource bars, popup rewards, 
  slide-up panels)
- Addressables for all art assets (quest backdrops, item icons, 
  raid boss art) — nothing hardcoded in scenes
- All API calls via a singleton ApiClient that attaches the JWT 
  to every request header automatically
ARCHITECTURE RULES:
- MVVM pattern: every screen has a ViewModel that holds state 
  and calls the API — Views only bind and display
- Client NEVER computes game values (damage, energy, stats)
- All displayed values come from the last API response
- On every screen load: fetch live values from server, update 
  UI from response
- JWT stored in encrypted local storage (not PlayerPrefs)
- Expired JWT triggers silent refresh via /api/auth/refresh 
  before retrying the failed request
GLOBAL UI COMPONENTS TO BUILD FIRST:
1. HeaderBar.uxml — persistent across all screens
   - Avatar, Name, Level
   - Resource bars: Energy, Stamina, GuildStamina
   - Gold, Gems (tap to open economy panel)
   - Binds to PlayerStateViewModel (refreshed server-side)
2. BottomNavBar.uxml — persistent across all screens
   - 5 tabs: Quest, Raid, Guild, Items, Profile
   - Active tab highlight
   - Tab switching via ScreenManager (no scene reloads)
3. ResourceBar.uxml — reusable component
   - Animated fill (DOTween on value change)
   - Tooltip on tap: current/max, regen rate, time to full
   - Color coded: Energy=blue, Stamina=green, GuildStamina=gold
4. RewardPopup.uxml — reusable overlay
   - Animated coin/XP/item reward display
   - Plays on quest completion, raid kill, level-up
   - Data driven: server response populates it
5. SlideUpPanel.uxml — reusable bottom sheet
   - Used for: node detail, raid detail, item detail
   - Swipe down or tap backdrop to dismiss
   - Content injected by each screen's ViewModel
SCREENS TO BUILD (in this order):
1. LoginScreen
2. QuestScreen (zone map + node grid)
3. QuestNodeDetailPanel (slide-up)
4. RaidScreen (active raids + world raid list)
5. RaidDetailPanel (HP bar, hit buttons, participant count)
6. GuildScreen (roster + perks + chat tabs)
7. ItemsScreen (equipment slots + inventory grid)
8. ProfileScreen
QUEST MAP IMPLEMENTATION NOTES:
- Zone backdrop: Addressable sprite loaded by zoneId
- Node positions: defined in content/zone_layouts.json 
  (x/y percentages of backdrop dimensions — resolution independent)
- Node state (locked/available/complete/boss) driven by 
  GET /api/quests response
- Node connections: drawn as lines in code (no art required) 
  between node positions
CONTENT PIPELINE (adding new quests — zero code changes):
1. Artist adds zone backdrop to Addressables with key "zone_{id}"
2. Designer adds node entries to content/quests.json on backend
3. Designer adds node positions to content/zone_layouts.json
4. Restart backend (or hot-reload if content service supports it)
5. Unity client picks up new quests from API automatically
No Unity rebuild required for quest content additions.
API CLIENT PATTERN:
public class ApiClient : MonoBehaviour
{
    // Singleton
    // Stores JWT access token + refresh token in encrypted storage
    // Attaches Bearer token to every request
    // On 401 response: silently calls /api/auth/refresh, 
    //   retries original request once
    // On second 401: redirect to LoginScreen
    // Never exposes raw tokens to any other class
}
ANIMATION GUIDELINES:
- Resource bar changes: DOTween lerp over 0.4s
- Reward popup: scale from 0 → 1.1 → 1.0 over 0.3s (bounce)
- Slide-up panel: DOTween Y from off-screen bottom over 0.25s
- Raid HP bar: lerp over 0.6s (feels weighty)
- Level-up: full-screen flash + particle burst + stat increase display
- All animations: complete before next input is accepted on 
  game-state-changing actions (prevent double-tap exploits)
BUILD THIS IN ORDER:
1. ApiClient singleton with JWT management
2. PlayerStateViewModel (fetches and holds player state)
3. HeaderBar + BottomNavBar (layout foundation)
4. RewardPopup + SlideUpPanel (reusable before screens need them)
5. LoginScreen (functional, calls /api/auth/login)
6. QuestScreen (functional, calls /api/quests)
7. RaidScreen (functional, calls /api/raids)
8. Remaining screens
After each screen: test against the live local backend 
(docker-compose up -d, dotnet run --project src/ROTA.Api).
Every screen must pass: load data from server, display correctly,
perform an action, verify server response updates the UI.
```

---
## PART 4 — CONTENT PIPELINE REFERENCE
*How to add new quests, raids, and items with zero code changes*
---
### Adding a New Quest Zone
1. Add backdrop art to Unity Addressables with key `zone_{zoneId}`
2. Add quest entries to `content/quests.json`:

```json
{
  "id": "z3_q01",
  "zoneId": "zone_03",
  "chapterId": "chapter_02",
  "name": "The Bone Wastes",
  "nodeType": "Battle",
  "baseEnergyCost": 8,
  "baseXpRatio": 1.35,
  "lootTableId": "lt_zone3_common",
  "sigilDropChance": 0,
  "sigils": null,
  "prerequisiteQuestId": "z2_q08",
  "triggersRaidId": null,
  "artKey": "zone3_bonewastes"
}
```
Note: difficulty is passed at attempt time. Rewards scale via difficulty multiplier server-side.

3. Add node positions to `content/zone_layouts.json`
4. Restart backend — client picks up automatically
### Adding a New Raid Boss
1. Add boss art to Addressables with key `raid_{raidId}`
2. Add raid definition to `content/raids.json`:

```json
{
  "id": "raid_malachar",
  "name": "Lord Malachar",
  "tier": "World",
  "baseHp": 250000,
  "timerHours": 48,
  "lootTableId": "lt_raid_malachar",
  "hasOnHitDrops": false
}
```
Note: difficulty is passed at summon time. Server computes finalHp = baseHp × multiplier
(Normal×1.0, Hard×1.4, Legendary×2.0, Nightmare×3.6). No per-difficulty HP in JSON.

3. No code changes required
### Adding a New Item
1. Add item icon to Addressables with key `item_{itemId}`
2. Add item definition to `content/items.json`
3. Add item to relevant loot table in `content/loot_tables.json`
4. No code changes, no migration required
---
*Document version: Phase 1 Complete / Phase 1 Extensions In Progress*
*Last updated: 2026-05-25*
*Owner: xolaces*
