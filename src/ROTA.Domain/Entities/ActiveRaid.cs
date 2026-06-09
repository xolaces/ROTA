using ROTA.Domain.Enums;
namespace ROTA.Domain.Entities;

public class ActiveRaid
{
    // Required by EF Core
    private ActiveRaid() { }

    public static ActiveRaid Create(
        string raidDefinitionId,
        Guid summonedByPlayerId,
        long maxHp,
        DateTimeOffset expiresAt,
        RaidDifficulty difficulty = RaidDifficulty.Normal,
        RaidSize size = RaidSize.Large,
        RaidVisibility visibility = RaidVisibility.Private)
    {
        return new ActiveRaid
        {
            Id                 = Guid.NewGuid(),
            RaidDefinitionId   = raidDefinitionId,
            SummonedByPlayerId = summonedByPlayerId,
            CurrentHp          = maxHp,
            MaxHp              = maxHp,
            IsDefeated         = false,
            Difficulty         = difficulty,
            Size               = size,
            Visibility         = visibility,
            LifecycleState     = RaidLifecycleState.Active,
            ParticipantCount   = 0,
            ExpiresAt          = expiresAt,
            CreatedAt          = DateTimeOffset.UtcNow,
            UpdatedAt          = DateTimeOffset.UtcNow,
            IsDeleted          = false,
        };
    }

    public Guid Id { get; private set; }
    public string RaidDefinitionId { get; private set; } = string.Empty;
    public Guid SummonedByPlayerId { get; private set; }
    public long CurrentHp { get; private set; }
    public long MaxHp { get; private set; }
    public bool IsDefeated { get; private set; }
    public RaidDifficulty Difficulty { get; private set; }
    public RaidSize Size { get; private set; }

    // Visibility (Ticket 50) — the INDEXING tier: summons start Private. ShareTo() moves the raid into
    // the Public / GuildOnly / FriendsOnly list. DISTINCT from "active": a raid is always joinable by its
    // GUID (the invite token) regardless of Visibility — this only governs which list it shows up in.
    public RaidVisibility Visibility { get; private set; }

    // Lifecycle state (Ticket 50). Active → (on kill) Lootable. T57 REVISES T50's reward timing: gold/
    // gems/stat-points/items are now COMPUTED on the killing hit but GRANTED per-participant when each
    // player presses Loot (RaidService.LootRaidAsync), so a defeated raid stays Lootable until claimed
    // (it is no longer flipped to Looted per claim). The Looted state + Loot() below are retained for the
    // legacy summoner-dismiss path but are NOT used by the per-participant claim flow.
    public RaidLifecycleState LifecycleState { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    // System 16 Slice 2 (additive) — links this raid to a Gauntlet event so scoring + off-cap
    // aura resolution can filter by event without re-inspecting the definition each hit. Null on
    // ordinary raids. Stamped at summon by Slice 4; the setter is provided now.
    public Guid? GauntletEventId { get; private set; }

    // System 21 Slice 3b (additive) — links this raid to a guild. Stamped at summon by the officer-
    // gated guild-raid summon. Null on ordinary/Gauntlet raids. When set, HitRaidAsync gates access to
    // guild members and spends GuildStamina (not Stamina); contribution accrues to the membership.
    public Guid? GuildId { get; private set; }

    // Denormalised for O(1) participant count on every hit response â€” incremented on first hit per player.
    public int ParticipantCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    // Navigation property â€” populated by repository Include() calls only
    public Player? SummonedByPlayer { get; private set; }

    // Domain methods

    public void TakeDamage(long amount)
    {
        CurrentHp  = Math.Max(0, CurrentHp - amount);
        UpdatedAt  = DateTimeOffset.UtcNow;
    }

    public void MarkDefeated()
    {
        IsDefeated     = true;
        // Ticket 50 — a defeated raid becomes Lootable. T57: rewards are DEFERRED to each participant's
        // Loot claim, so Lootable means "rewards are waiting to be claimed" (not merely awaiting dismissal).
        LifecycleState = RaidLifecycleState.Lootable;
        UpdatedAt      = DateTimeOffset.UtcNow;
    }

    public void IncrementParticipantCount()
    {
        ParticipantCount++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    // Ticket 50 — publish this raid to a visibility tier (Public / GuildOnly / FriendsOnly). May move
    // between tiers (e.g. GuildOnly → Public). Idempotent beyond bumping UpdatedAt. Caller (RaidService)
    // enforces summoner-only + non-Personal + guild-membership rules. There is intentionally NO un-share
    // path back to Private (non-goal). ShareTo(Private) is a no-op tier-wise but still bumps UpdatedAt.
    public void ShareTo(RaidVisibility visibility)
    {
        Visibility = visibility;
        UpdatedAt  = DateTimeOffset.UtcNow;
    }

    // Back-compat overload (Ticket 50) — defaults to Public so the currently-shipped client (which calls
    // share with no visibility) and existing callers/tests keep working unchanged.
    public void Share() => ShareTo(RaidVisibility.Public);

    // Ticket 50 — summoner dismisses a defeated raid: Lootable → Looted, removing it from ALL indexes.
    // Guard: only a Lootable raid may be looted (a still-Active raid throws; a re-loot is a no-op so the
    // service can treat it as already-gone). Does NOT soft-delete — IsDeleted stays false so the
    // raid_participants FK and the completed-raid history remain intact.
    public void Loot()
    {
        if (LifecycleState != RaidLifecycleState.Lootable)
            throw new InvalidOperationException(
                $"Cannot loot a raid in {LifecycleState} state — only a Lootable (defeated) raid may be dismissed.");
        LifecycleState = RaidLifecycleState.Looted;
        UpdatedAt      = DateTimeOffset.UtcNow;
    }

    // System 16 Slice 2 (additive) — stamps this raid as belonging to a Gauntlet event. Set once
    // at summon (Slice 4 wires the call site). Does not alter any other raid behaviour.
    public void LinkGauntletEvent(Guid eventId)
    {
        GauntletEventId = eventId;
        UpdatedAt       = DateTimeOffset.UtcNow;
    }

    // System 21 Slice 3b (additive) — stamps this raid as belonging to a guild. Set once at summon.
    public void LinkGuild(Guid guildId)
    {
        GuildId   = guildId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

