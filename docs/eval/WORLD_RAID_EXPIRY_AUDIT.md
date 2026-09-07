# World raid expiry settlement -- audit

## 1. Verdict
No. There is no code path that settles an expired World raid and pays the ladder. The repo only rejects post-expiry hits and filters expired raids out of active lists; the only state transition that makes a raid lootable is a kill path, and that path still assumes a single killer.

## 2. Evidence
### Claim: a raid that has passed `ExpiresAt` is rejected before any hit is processed.
`src/ROTA.Application/Services/RaidService.cs:706-710`
```csharp
        // 2. Timer expired (pre-spend, no cost to player).
        if (raid.ExpiresAt < DateTimeOffset.UtcNow)
            return HitFail(RaidHitFailureCode.RaidExpired,
                "The raid has faded into the void — no rewards, no stamina spent.");
```

### Claim: expired raids are not treated as active and are therefore not listed for normal play.
`src/ROTA.Infrastructure/Persistence/Repositories/ActiveRaidRepository.cs:29-35`
```csharp
    public async Task<IReadOnlyList<ActiveRaid>> GetAllActiveAsync(CancellationToken ct = default)
        => await _db.ActiveRaids
            .Include(r => r.SummonedByPlayer)
            .Where(r => !r.IsDefeated && !r.IsDeleted && r.ExpiresAt > DateTimeOffset.UtcNow)
            // Deterministic order so the public list doesn't reshuffle every load (low-HP-first =
            // closest-to-death surfaces first). Client sort controls reorder client-side.
            .OrderBy(r => r.CurrentHp)
            .ToListAsync(ct);
```

### Claim: there is no expiry settlement sweep in the background services; the pattern that exists is a different job for Gauntlet rank recomputation.
`src/ROTA.Api/Program.cs:214-219`
```csharp
// Phase 2 (T39): out-of-band sender that drains the email queue without blocking requests.
builder.Services.AddHostedService<EmailSendBackgroundService>();

// System 16 Slice 3: periodic per-league rank snapshot for the active Gauntlet event.
builder.Services.AddHostedService<GauntletRankSnapshotService>();
```
`src/ROTA.Api/BackgroundServices/GauntletRankSnapshotService.cs:31-60`
```csharp
    private async Task SnapshotOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var events  = scope.ServiceProvider.GetRequiredService<IGauntletEventRepository>();
            var scoring = scope.ServiceProvider.GetRequiredService<IGauntletScoringService>();

            var active = await events.GetActiveAsync(ct);
            if (active is null)
                return;

            await scoring.RecomputeRanksAsync(active.Id, ct);
        }
```
This is explicitly a leaderboard recomputation sweep for an active Gauntlet event. It is not a raid expiry settlement task, and there is no analogous sweeper for raids in `Program.cs` or the API background service folder.

### Claim: the only state transition that makes a raid `Lootable` is `MarkDefeated()`, and `MarkDefeated()` is called only inside the kill branch.
`src/ROTA.Domain/Entities/ActiveRaid.cs:87-92`
```csharp
    public void MarkDefeated()
    {
        IsDefeated     = true;
        // Ticket 50 — a defeated raid becomes Lootable. T57: rewards are DEFERRED to each participant's
        // Loot claim, so Lootable means "rewards are waiting to be claimed" (not merely awaiting dismissal).
        LifecycleState = RaidLifecycleState.Lootable;
        UpdatedAt      = DateTimeOffset.UtcNow;
    }
```
`src/ROTA.Application/Services/RaidService.cs:1291-1307`
```csharp
            // A raid with NO HEALTH POOL can never be killed by damage. World raids carry MaxHp 0 as
            // the timer-only marker (owner 2026-08-29), and CurrentHp 0 is their RESTING state rather
            // than a death — so the bare `CurrentHp == 0` this used to be was already true before any
            // damage landed, and the first hit ended a seven-day event instantly, paying out as though
            // one player had soloed it.
            //
            // Keyed on MaxHp, not on the raid's tier: MaxHp is what the kill actually depends on, and
            // RaidDefinitionProvider.Validate already guarantees only World raids reach zero.
            bool isKill = lockedRaid.MaxHp > 0 && lockedRaid.CurrentHp == 0;
            if (isKill)
            {
                lockedRaid.MarkDefeated();
```

### Claim: `LootRaidAsync` explicitly blocks any raid that is not already `Lootable`; an expired raid never transitions there automatically.
`src/ROTA.Application/Services/RaidService.cs:569-586`
```csharp
        // Must be defeated (Lootable) to claim — a still-Active raid can't be looted.
        if (raid.LifecycleState != RaidLifecycleState.Lootable)
            return new LootRaidResult
            {
                FailureCode   = LootRaidFailureCode.NotLootable,
                FailureReason = "This raid is still active — defeat it before looting.",
            };
```
`src/ROTA.Application/Services/RaidService.cs:574-581`
```csharp
        if (raid is null || raid.IsDeleted || raid.LifecycleState == RaidLifecycleState.Looted)
            return new LootRaidResult
            {
                FailureCode   = LootRaidFailureCode.NotFound,
                FailureReason = "Raid not found.",
            };
```
The codebase does not have an expiry-settlement branch that flips a World raid from `Active` to `Lootable`. If it expires, the service treats it as expired and disallows further play, not as a rewardable kill.

### Claim: the reward path is built around a single killer and a single caller, not a caller-less expiry settlement.
`src/ROTA.Application/Services/RaidService.cs:1514-1522`
```csharp
    private async Task<RaidRewards> DistributeKillRewardsAsync(
        Guid callerPlayerId,
        Player callerPlayer,
        ActiveRaid raid,
        RaidDefinition definition,
        IReadOnlyList<RaidParticipant> allParticipants,
        double callerHoardDropMultiplier,
        CancellationToken ct)
```
This method requires all of the following from the kill event: the winning player id, the winning player record, the raid record, the raid definition, the participant list, and the killer-specific Hoard multiplier.

### Claim: the banked damage is stored on each participant row, not in an aggregate reward table.
`src/ROTA.Domain/Entities/RaidParticipant.cs:20-38`
```csharp
    public Guid Id { get; private set; }
    public Guid ActiveRaidId { get; private set; }
    public Guid PlayerId { get; private set; }
    public long TotalDamageDealt { get; private set; }
    public int HitCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
```
`src/ROTA.Domain/Entities/RaidParticipant.cs:41-55`
```csharp
    public void RecordHit(long damage)
    {
        TotalDamageDealt += damage;
        HitCount++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
```
The ladder settlement would need to read these per-participant totals from `raid_participants` and then compare each total against the ladder thresholds defined by the loot table.

## 3. What DistributeKillRewardsAsync assumes
The exact signature:
```csharp
private async Task<RaidRewards> DistributeKillRewardsAsync(
    Guid callerPlayerId,
    Player callerPlayer,
    ActiveRaid raid,
    RaidDefinition definition,
    IReadOnlyList<RaidParticipant> allParticipants,
    double callerHoardDropMultiplier,
    CancellationToken ct)
```
`src/ROTA.Application/Services/RaidService.cs:1514-1522`

The caller-dependent assumptions inside it are:
- `callerPlayerId` identifies the killer; the method uses it to select the kill winner in the participant loop: `participantPlayer = p.PlayerId == callerPlayerId ? callerPlayer : await _players.FindByIdAsync(p.PlayerId, ct);` `src/ROTA.Application/Services/RaidService.cs:1553-1560`
```csharp
            Player? participantPlayer = p.PlayerId == callerPlayerId
                ? callerPlayer
                : await _players.FindByIdAsync(p.PlayerId, ct);
```
- `callerPlayer` is required to load the winner’s record and to issue the winner-only reward branch. `src/ROTA.Application/Services/RaidService.cs:1553-1560`
- `raid` supplies the difficulty and the loot table context used for tiering and threshold logic. `src/ROTA.Application/Services/RaidService.cs:1529-1548`
```csharp
        var sorted = allParticipants.OrderByDescending(p => p.TotalDamageDealt).ToList();
        long totalDamage = sorted.Sum(p => p.TotalDamageDealt);

        // Assign tiers
        var tierAssignments = new Dictionary<Guid, (string tier, decimal multiplier)>();
        int epicCutoff = Math.Max(1, (int)Math.Ceiling(sorted.Count * 0.10));
```
`src/ROTA.Application/Services/RaidService.cs:1548-1558`
```csharp
                double pct = totalDamage > 0 ? (double)p.TotalDamageDealt / totalDamage * 100.0 : 0;
                // Load loot table to get minContributionPercent
                var lt = _lootTables.GetById(definition.LootTableId);
                double minPct = lt?.Difficulties?.GetValueOrDefault(raid.Difficulty.ToString())
                    ?.MinContributionPercent ?? 0.1;
```
- `definition` is required to obtain the raid’s loot table and base gold values. `src/ROTA.Application/Services/RaidService.cs:1548-1558` and `src/ROTA.Application/Services/RaidService.cs:1581-1587`
```csharp
            long gold = (long)Math.Round(definition.BaseGoldReward * diffMult * (double)multiplier);
```
- `allParticipants` is the full damage ranking and is used to build contribution tiers from `TotalDamageDealt`; there is no concept here of an absent caller or an expiry-driven sweep. `src/ROTA.Application/Services/RaidService.cs:1529-1548`
- `callerHoardDropMultiplier` is only meaningful for the killer’s bonus drop scaling and is therefore a killer-only bonus. `src/ROTA.Application/Services/RaidService.cs:1524-1529`
```csharp
        // System 22 Phase A follow-up — Hoard scales CHANCE-based threshold drops, mirroring
        // ProcessQuestLootAsync.Scale: chance × Hoard, clamped at MaxThresholdDropChance, where the
        // clamp never *lowers* an already-higher base (a base ≥ the cap, e.g. 1.0, is unchanged).
        // MINIMAL SAFE SLICE (per the ticket): only the KILLER's drops are Hoard-scaled — their mastery
        // multiplier is already in hand from the hit's single GetModifiersAsync read. Scaling every
        // participant's drops would need a mastery read per participant INSIDE the advisory-lock tx
        // (the exact per-participant kill-loop cost System 22 deferred), so non-killer participants keep
        // their base chance (HoardDropMultiplier 1.0 → no-op).
```
That is exactly the kind of reward rule that only makes sense when a single player is the winner/owner of the kill event.

## 4. What a settlement path would need
- A trigger when `ExpiresAt` passes. This does not currently exist in the repo. The closest analogous pattern is a hosted background task that sweeps and recomputes state, but it is specifically for Gauntlet ranking, not raids: `src/ROTA.Api/Program.cs:214-219`, `src/ROTA.Api/BackgroundServices/GauntletRankSnapshotService.cs:31-60`.
- A settlement routine that can run without a current hit or a “killer” player. There is no existing function for this. The only reward engine is `DistributeKillRewardsAsync`, and it is built around a `callerPlayerId` and `callerPlayer`: `src/ROTA.Application/Services/RaidService.cs:1514-1522`.
- A way to read the ladder state for all participants. The repo already stores each participant’s banked damage in `TotalDamageDealt` on `RaidParticipant`: `src/ROTA.Domain/Entities/RaidParticipant.cs:20-38`, `src/ROTA.Domain/Entities/RaidParticipant.cs:41-55`.
- A loot-table ladder evaluator for the World raid thresholds. The repo already has the threshold model and the ladder sorting logic in the loot preview path: `src/ROTA.Application/Services/RaidCatalogueService.cs:30-68` and `src/ROTA.Application/Services/RaidService.cs:1529-1548`.
- A way to mark settlement as done and to avoid double-paying. The repo has idempotency patterns around reward claim and reward refs, but not a matching expiry-settlement latch for World raids. Examples of similar patterns already present are the participant claim latch in `TryClaimRewardsAsync`: `src/ROTA.Infrastructure/Persistence/Repositories/RaidParticipantRepository.cs:64-74`, and the cached hit dedupe in `RaidHitCache`: `src/ROTA.Infrastructure/Services/RaidHitCache.cs:13-35`.

## 5. Open questions
- I could not determine from the code what the intended reward semantics for an expired World raid are at the business level: whether the ladder pays all participants who crossed a rung once, or only the highest rung per player, or some other combiner. I would need to read the World-raid content definition and the relevant test fixture for the ladder, especially the loot table JSON and the dedicated expiry/timer tests.
- I could not determine whether a settlement job is supposed to create a synthetic “killer” or to distribute directly by participant. I would need the intended contract from the World raid design docs and the timer test file.
- I could not determine whether the system expects expired World raids to be closed with `Lootable`/`Looted` state transitions or remain in an expired-but-open state for admin review. I would need to read the timer model and the actual design note for the World raid lifecycle.

To answer those, the next reads I would request are: the World raid content definitions in the `content/` JSON used by the loot ladder, the dedicated timer tests (the file mentioned in the task background), and any design doc or issue note that defines “what happens when a World raid expires after seven days.”
