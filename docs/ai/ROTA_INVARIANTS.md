# ROTA Invariants

## CURRENT IMPLEMENTATION

These are the invariants that can be established from the implementation, configuration, and tests in the current worktree.

## 1. Server authority

The server owns the definitive state. The client sends intent only; it does not decide the result of combat, drops, progression, or rewards.

Verified by:

- `Program.cs` authentication and validation setup
- `README.md` and architecture docs
- service-layer logic in `RaidService`, `QuestService`, and `StatService`

## 2. Domain mutation is method-based

Entities do not rely on raw field replacement from controllers or services. Domain methods mutate state and update timestamps.

Verified examples:

- `Player.AddExperience` in `Player.cs`
- `ActiveRaid.TakeDamage` and `ActiveRaid.MarkDefeated` in `ActiveRaid.cs`
- `RaidParticipant.RecordHit` in `RaidParticipant.cs`

## 3. World raids are timer-only when `baseHp == 0`

This is a critical runtime invariant.

Verified in:

- `RaidDefinitionProvider.Validate`
- `WorldRaidTimerTests`
- `RaidDTOs` comments
- `RaidService` hit guard logic

Relation:

- non-World raids must have positive `baseHp`
- World raids must have `baseHp == 0`
- a zero-HP raid is not treated as a regular health-pool raid

## 4. Expired raids are rejected before any spend

The current hit path explicitly returns `RaidExpired` before spending stamina or resolving a kill.

This is a verified rule in `RaidService.HitRaidAsync`.

## 5. Hit mutations are serialized per raid

Raid hits are protected by a PostgreSQL advisory lock in `ActiveRaidRepository.AtomicApplyHitAsync`.

This prevents duplicate or out-of-order concurrent hit processing from corrupting the raid state.

## 6. Reward claims are distinct from combat resolution

The implementation separates hit resolution from loot claiming. A defeated raid becomes `Lootable`; claim actions then grant pending rewards to the participant.

Verified in:

- `ActiveRaid.MarkDefeated`
- `ActiveRaid.Loot`
- `RaidService.LootRaidAsync`
- `RaidParticipant.RecordPendingRewards`

## 7. Gem balance is derived, not stored

The project explicitly treats gems as an append-only ledger with aggregate balance computed from transactions.

This is a meaningful gameplay and accounting invariant.

## 8. Resource and stat caps are enforced server-side

The server validates and recalculates max values and current values on stat allocation and level-up. The client cannot trust its local bars.

## 9. Idempotency matters for replay safety

The current implementation protects key actions using Redis or SQL-level idempotency patterns, especially for:

- raid hit requests
- pending reward claims
- currency or strike spends
- challenge counters and achievement references

## UNVERIFIED INTENT

Some rules are important to the game design but not explicitly codified as a strict invariant in the current implementation. Those remain design intent rather than verified runtime law until they are backed by code, tests, or active content validation.

Examples include some future balancing choices that are documented in spec files but not enforced in the current live runtime behavior.

## Summary

The key invariant is simple: the implementation is authoritative, data-driven, and guarded by transactions and server-side validation. Where a game mechanic is not directly enforced by the code, it should not be treated as a guaranteed runtime rule.
