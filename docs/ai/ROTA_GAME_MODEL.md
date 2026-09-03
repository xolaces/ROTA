# ROTA Game Model

## CURRENT IMPLEMENTATION

ROTA is a server-authoritative fantasy RPG backend built around repeated action loops: login, resource management, quests, raids, progression, and claimable loot.

The game loop is not a single battle loop; it is a chain of server-resolved actions:

1. The player authenticates and receives a JWT.
2. The client sends intent (summon, hit, use item, allocate stat, accept guild invite, etc.).
3. The server validates the request, checks authorization, enforces rules, and mutates the authoritative state.
4. The server returns the updated resource values, reward summary, progression, and any claimable outcomes.

This is visible in the API startup and controller/service stack, and it is consistent with the project README and the application service layer.

## What the player actually does

The gameplay loop centers on a few repeatable actions:

- Fight raids and complete quest nodes for rewards and progression.
- Spend stamina/energy and maintain resource pools while the game recharges them over time.
- Earn gold, gems, stat points, items, sigils, gear, and achievement progress.
- Allocate stats and level up through the stat service and experience flow.
- Join guilds and participate in guild-specific activities.
- Manage social and moderation state via friends, blocks, chat, and reports.

The project is explicitly designed around long-form progression rather than a single-session win condition.

## Core progression model

The progression model is built from several layers:

- Experience and level progression via `Player.AddExperience` and `IStatService.XpToNextLevel`.
- Resource pools: Energy, Stamina, GuildStamina, and Health.
- Stat allocation with investment caps and server-enforced constraints.
- Loot and reward systems that grant gold, gems, inventory items, gear, and progression resources.
- Class progression and mastery progression layered on top of base growth.

The server does not trust client-side calculations. Effective values are recomputed server-side and persisted only where needed.

## Combat and raid model

Combat is a shared server-side raid engine. The core mechanics include:

- hit-size-based damage
- RNG with a server-seeded damage formula
- crits and proc bonuses
- stat contributions and equipment-derived combat modifiers
- contribution-tier rewards based on damage share
- raid-specific reward processing and loot claim semantics

A major rule in the current implementation is that World raids are timer-only and not collective-health raids. This is enforced in both the raid definition validation and the hit logic.

## World raids

CURRENT IMPLEMENTATION:

- `RaidDefinitionProvider.Validate` requires `baseHp == 0` for World raids and rejects non-World raids with zero HP.
- `HitRaidAsync` rejects expired raids before spending resources.
- `HitRaidAsync` does not kill a World raid merely because `CurrentHp == 0`; the guard is `lockedRaid.MaxHp > 0 && lockedRaid.CurrentHp == 0`.
- World raids are treated as seven-day timer-driven content with a damage ladder instead of a health pool.

This is a verified gameplay rule in the current worktree and was specifically fixed after the earlier bug in which the first hit instantly killed a World raid.

## Economy and rewards

The economy is intentionally ledger-based:

- Gem balance is computed from append-only `gem_transactions` rows rather than a stored balance field.
- Gold is tracked as a persisted value and is increased through gameplay actions.
- Inventory, gear, sigils, and rewards are granted via service logic and persisted in the database.
- Reward distribution is tied to raid contribution, quest completion, and gated progression.

## Social and persistence

ROTA also includes:

- friend and block flows
- private message flows and chat hubs
- guild membership, guild chat, and guild economy actions
- moderation, audit logging, and operator tooling

The core rule is that all state-changing actions are server-authoritative and are recorded in the audit model when relevant.

## UNVERIFIED INTENT

Some game-design intent is clearly documented in design notes and specs, but not every player-facing objective is enforced as a rigid runtime rule. Where the runtime implementation is clear, it is treated as authoritative. Where the design mentions a future or aspirational mechanic, it is not described as a current gameplay contract.

## Summary

ROTA is best understood as a long-lived, server-authoritative RPG backend whose fun loop is:

- earn resources
- clear content
- gain power
- unlock higher-tier content
- repeat with better stats, gear, and guild progression

The actual source of truth is the server implementation, not the design notes alone.
