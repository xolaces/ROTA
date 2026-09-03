# ROTA Audit Log

## Scope
This audit focuses on the highest-risk execution paths that affect gameplay correctness, progression integrity, and payout safety.

## Verified: Gauntlet battalion path is green
### Evidence
- Controller: [src/ROTA.Api/Controllers/GauntletController.cs](../../src/ROTA.Api/Controllers/GauntletController.cs)
- Service: [src/ROTA.Application/Services/GauntletBattalionService.cs](../../src/ROTA.Application/Services/GauntletBattalionService.cs)
- Combat hook: [src/ROTA.Application/Services/RaidService.cs](../../src/ROTA.Application/Services/RaidService.cs)
- Regression coverage: [tests/ROTA.IntegrationTests/GauntletLadderTests.cs](../../tests/ROTA.IntegrationTests/GauntletLadderTests.cs)

### Verified behavior
- The Gauntlet battalion endpoints are exposed and wired.
- The battalion service resolves owned unit IDs and computes power from the configured formula.
- The raid hit path calls the battalion power computation before damage resolution.
- The hit path spends strikes and updates the Gauntlet score, matching the gameplay contract.

### Fresh proof
Command run:
`dotnet test ROTA.slnx --filter "GauntletBattalionServiceTests|Hit_GauntletRaid_UsesBattalionPower_ForDamageBase|GauntletStageHit_ResolvesEndToEnd_SpendsStrikes_UpdatesScore" --logger "console;verbosity=minimal"`

Result:
- Passed: 9
- Failed: 0
- Skipped: 0

## Audit finding: World raid expiry flow is not proven complete
### Root cause summary
The code does not currently implement a timer-driven settlement path for World raids.

### Evidence
- Expired raids are rejected before processing: [src/ROTA.Application/Services/RaidService.cs](../../src/ROTA.Application/Services/RaidService.cs)
- The active-raid list excludes expired raids: [src/ROTA.Infrastructure/Persistence/Repositories/ActiveRaidRepository.cs](../../src/ROTA.Infrastructure/Persistence/Repositories/ActiveRaidRepository.cs)
- No expiry settlement background service exists in [src/ROTA.Api/Program.cs](../../src/ROTA.Api/Program.cs)
- The only reward engine is the kill path, which assumes a caller/player winner: [src/ROTA.Application/Services/RaidService.cs](../../src/ROTA.Application/Services/RaidService.cs)
- A defeated raid enters the lootable lifecycle through `MarkDefeated()`: [src/ROTA.Domain/Entities/ActiveRaid.cs](../../src/ROTA.Domain/Entities/ActiveRaid.cs)

### What this means
The repo is consistent with the rule: World raids are timer-only and must not be killable by damage, but the expiry settlement branch itself has not been implemented or verified. The lifecycle is therefore not yet in a “complete and proven” state.

## Independent regression proof
A dedicated test suite exists to pin the risk:
- [tests/ROTA.IntegrationTests/WorldRaidTimerTests.cs](../../tests/ROTA.IntegrationTests/WorldRaidTimerTests.cs)

These tests assert that a zero-HP World raid remains alive, keeps accumulating damage, and does not die on the first hit. That is a valid safety check, but it does not replace the missing expiry-settlement contract.

## Current status
### Proven green
- Gauntlet battalion / hit path

### Not yet proven complete
- World raid expiration settlement contract
- Any business rule for automatic reward distribution at expiry

## Recommendation
Before calling the World raid system “complete,” confirm the intended reward semantics at timeout (for example: ladder payout by accumulated damage, highest rung only, or a defined all-participant settlement) and add a matching integration test for that path.
