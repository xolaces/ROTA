# ROTA Design Questions

## World Raid expiry settlement

### Status

Open design decision — product intent is not enforced by the current repository.

### Question

When a World raid reaches its expiration time, should the raid:

1. auto-settle and pay a leaderboard ladder reward,
2. simply expire and disappear without settlement,
3. become lootable for a final claim window,
4. or use some other lifecycle not yet implemented in the server?

### Evidence currently in the codebase

- `RaidDefinitionProvider.Validate` marks World raids as timer-only and requires `baseHp == 0`.
- `ActiveRaidRepository.GetAllActiveAsync` filters out any raid whose `ExpiresAt <= UtcNow`.
- `HitRaidAsync` rejects expired raids before spending stamina.
- There is no settlement job, no settlement repository, and no ladder-payout logic attached to expiry in the current code path.
- The current server behavior does not create a persistent expiry-settlement record or reward ledger on expiry.

### Why it matters

This is the one essential unresolved behavior for World raids: the repo proves the timer-only model, but not the reward/settlement contract after the timer ends.

### Current stance

Do not guess.

The authoritative position for the current codebase is:

- World raids are valid timer-only raids with `BaseHp == 0`.
- They remain active while `ExpiresAt > now`.
- Once expired, the code path currently filters them out rather than settling them.
- A product decision is still required before implementing a payout-and-rank settlement flow.

### Recommendation

Treat expiry settlement as a product-design gap until the owner confirms the intended lifecycle. Once confirmed, implement the smallest explicit settlement pipeline with:

- one-time idempotent settlement
- durable settlement ledger
- ranked payout calculation
- audit record
- replay-safe handling

Until then, preserve the current timer-only semantics and do not invent payout behavior.
