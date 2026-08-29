namespace ROTA.Shared.DTOs;

public class ActiveRaidResponse
{
    public Guid ActiveRaidId { get; set; }
    public string RaidDefinitionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long CurrentHp { get; set; }
    public long MaxHp { get; set; }
    public double HpPercent { get; set; }
    public bool IsDefeated { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public long TimerRemainingSeconds { get; set; }
    public string SummonedByUsername { get; set; } = string.Empty;
    public int ParticipantCount { get; set; }
    public long YourTotalDamage { get; set; }
    public int YourHitCount { get; set; }
    public string Tier { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string DifficultyColor { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string YourCurrentTier { get; set; } = string.Empty;
    // Visibility tier (Ticket 50) — "Private" | "Public" | "GuildOnly" | "FriendsOnly".
    public string Visibility { get; set; } = string.Empty;
    // Lifecycle state (Ticket 50) — "Active" | "Lootable" | "Looted".
    public string LifecycleState { get; set; } = string.Empty;
    // Derived convenience (Ticket 50) — IsPublic = (Visibility == Public). KEPT on the wire so the
    // currently-shipped client (which reads IsPublic and shares with no visibility) keeps working.
    public bool IsPublic { get; set; }
}

public class RaidHitResponse
{
    public bool Success { get; set; }
    public long DamageDealt { get; set; }
    public long CurrentHp { get; set; }
    public long MaxHp { get; set; }
    public double HpPercent { get; set; }
    public bool IsDefeated { get; set; }
    public long YourTotalDamage { get; set; }
    public int YourHitCount { get; set; }
    // ParticipantCount is on ActiveRaidResponse (list screen) — not exposed per-hit.
    public int NewStaminaValue { get; set; }
    public int NewStaminaMax { get; set; }
    // T56 — live Health after the per-hit drain, so the client can patch the health bar without a
    // profile re-fetch (otherwise it freezes after the first hit). On guild raids the stamina fields
    // above carry GuildStamina, not regular Stamina.
    public int NewHealthValue { get; set; }
    public int NewHealthMax { get; set; }
    public RaidRewards? Rewards { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Difficulty { get; set; } = string.Empty;
    public string DifficultyColor { get; set; } = string.Empty;
    public List<ItemGrantDTO>? OnHitDrops { get; set; }
    public string YourCurrentTier { get; set; } = string.Empty;
    // On-hit progression — granted every hit regardless of kill outcome
    // int32-overflow-audit Unit 2 — XpGained widened to long (GoldGained already long).
    public long XpGained { get; set; }
    public long GoldGained { get; set; }
    // Running totals after this hit (so the client can update the header/state without a re-fetch).
    public long NewPlayerExperience { get; set; }
    public int  NewPlayerLevel { get; set; }
    public long NewPlayerGold { get; set; }
    // Crit outcome for this hit (IsCrit=false and CritMultiplier=1.0 when not a crit)
    public bool IsCrit { get; set; }
    public double CritMultiplier { get; set; }
    public bool   ProcFired  { get; set; }
    public long   ProcBonus  { get; set; } // raw bonus damage from mount proc (0 if no proc)
    // Magic DamageProc totals for this hit (Slice 4).
    // MagicProcBonus = min(Σ raw per-magic bonuses, cap). MagicProcs = raw per-magic
    // breakdown BEFORE the cap; their sum may exceed MagicProcBonus when capped.
    public long              MagicProcBonus { get; set; }
    public List<MagicProcDTO> MagicProcs   { get; set; } = new();
    // Magic CritChanceFlat total for this hit (Slice 5); 0.0 when no CritChanceFlat magic applied
    public double MagicCritBonus { get; set; }
    // Legion contribution for this hit (Slice 4).
    // LegionPower = legionPower term after PowerScaling (0 when no active legion).
    // UnitProcs = raw per-unit-ability bonuses BEFORE the aggregate cap; their sum may exceed
    // UnitProcBonus when capped (same semantics as MagicProcs vs MagicProcBonus).
    public long              LegionPower   { get; set; }
    public long              UnitProcBonus { get; set; }
    public List<MagicProcDTO> UnitProcs    { get; set; } = new();
    // Commander gear proc (Slice 5 — procs-only; stat bonuses are never applied to charBase).
    public bool CommanderProcFired  { get; set; }
    public long CommanderProcBonus  { get; set; }
    // BETA (System 16 Slice 4) — Gauntlet amplifiers. OffCapAuraBonus = total Wrath/Blessing off-cap
    // proc damage added this hit (0 on non-Gauntlet raids or when no aura fired); it is NOT governed by
    // the MaxAggregateProcBonus magic cap. NewStrikeBalance = the player's Strike balance after this
    // hit's spend (0 on non-Gauntlet raids, which spend Stamina not Strikes).
    public long OffCapAuraBonus  { get; set; }
    public long NewStrikeBalance { get; set; }

    // System 22 Phase A — mastery combat surfacing. WrathLegionBonus = marginal legion power added by
    // the Wrath mastery this hit (0 with no active legion / no Wrath level). BulwarkBonus = marginal
    // damage added by the Bulwark mastery (0 on non-guild raids; hard-capped).
    public long WrathLegionBonus { get; set; }
    public long BulwarkBonus     { get; set; }
}

public class MagicProcDTO
{
    public string Name  { get; set; } = string.Empty;
    public long   Bonus { get; set; }
}

public class RaidRewards
{
    public long GoldGranted { get; set; }
    // int32-overflow-audit Unit 2 — XP/gems widened to long (GoldGranted already long).
    public long ExperienceGranted { get; set; }
    public long GemsGranted { get; set; }
    public long NewPlayerGold { get; set; }
    public long NewPlayerExperience { get; set; }
    public int? NewPlayerLevel { get; set; }
    public string ContributionTier { get; set; } = string.Empty;
    public decimal TierMultiplier { get; set; }
    public int UnassignedStatPointsGranted { get; set; }
    public int AttackPointsGranted { get; set; }
    public int DefensePointsGranted { get; set; }
    public int DiscernmentPointsGranted { get; set; }
    public List<ItemGrantDTO> ItemsGranted { get; set; } = new();

    public int XpToNextLevel { get; set; }
    public long CurrentLevelXp { get; set; }
    public int LevelsGained { get; set; }
}

public class SummonRaidResponse
{
    public Guid ActiveRaidId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long MaxHp { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public long TimerRemainingSeconds { get; set; }
    public string Difficulty { get; set; } = string.Empty;
    public string DifficultyColor { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
}

public class SummonRaidRequest
{
    public string Difficulty { get; set; } = "Normal";
}

public class RaidHitRequest
{
    public int HitSize { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class CompletedRaidResponse
{
    public Guid ActiveRaidId { get; set; }
    public string RaidDefinitionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string DifficultyColor { get; set; } = string.Empty;
    public DateTimeOffset DefeatedAt { get; set; }
    public long YourTotalDamage { get; set; }
    public string ContributionTier { get; set; } = string.Empty;
    public long GoldEarned { get; set; }
    // int32-overflow-audit Unit 2 — earned reward amounts widened to long (mirror RaidParticipant).
    public long XpEarned { get; set; }
    public long GemsEarned { get; set; }
    public long StatPointsEarned { get; set; }
    public List<ItemGrantDTO> ItemsEarned { get; set; } = new();
}

// --- Service result wrappers ---

public class SummonRaidResult
{
    public bool Success { get; set; }
    public SummonRaidFailureCode FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public SummonRaidResponse? Response { get; set; }
}

public class RaidHitResult
{
    public bool Success { get; set; }
    public RaidHitFailureCode FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public RaidHitResponse? Response { get; set; }
}

public class ShareRaidResult
{
    public bool Success { get; set; }
    public ShareRaidFailureCode FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public ActiveRaidResponse? Raid { get; set; }
}

// Ticket 50 — body for POST /api/raids/{id}/share. Optional: omitting it (or sending no body) defaults
// to "Public" for back-compat with the currently-shipped client. Valid: Public | GuildOnly | FriendsOnly.
public class ShareRaidRequest
{
    public string Visibility { get; set; } = "Public";
}

// Ticket 50 + T57 — result of the per-participant loot CLAIM. T57 adds Rewards: the gold/gems/stat-
// points/items GRANTED on this claim (XP/levels were already granted on the killing hit). On an
// idempotent re-press, Rewards still carries the participant's summary but nothing is re-granted.
public class LootRaidResult
{
    public bool Success { get; set; }
    public LootRaidFailureCode FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public ActiveRaidResponse? Raid { get; set; }
    public RaidRewards? Rewards { get; set; }
}

public enum SummonRaidFailureCode
{
    None                = 0,
    DefinitionNotFound  = 1,
    PlayerNotFound      = 2,
}

public enum RaidHitFailureCode
{
    None                = 0,
    RaidNotFound        = 1,
    RaidExpired         = 2,
    RaidAlreadyDefeated = 3,
    InvalidHitSize      = 4,
    InsufficientStamina = 5,
    AccessDenied        = 6,  // Personal raid — only the summoner may strike
    RaidFull            = 7,  // Participant cap reached for this raid size
    InsufficientStrikes = 8,  // BETA (System 16 Slice 4) — Gauntlet raid: not enough Strikes for this hit size
    InsufficientGuildStamina = 9, // System 21 Slice 3b — guild raid: not enough GuildStamina for this hit size (→ 422)
}

public enum ShareRaidFailureCode
{
    None                = 0,
    NotFound            = 1,  // raid missing / deleted / expired
    NotSummoner         = 2,  // caller did not summon this raid
    CannotSharePersonal = 3,  // Personal (solo) raids can't be shared
    NotInGuild          = 4,  // Ticket 50 — GuildOnly target but the summoner is not in a guild
}

// Ticket 50 — loot (dismiss) failure reasons.
public enum LootRaidFailureCode
{
    None        = 0,
    NotFound    = 1,  // raid missing / deleted / already looted
    NotSummoner = 2,  // caller did not summon this raid
    NotLootable = 3,  // raid is still Active (not yet defeated)
}

public class RaidParticipantRankDto
{
    public int    Rank        { get; set; }
    public Guid   PlayerId    { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public long   TotalDamage { get; set; }
    public int    HitCount    { get; set; }
}

// ─── Raid catalogue / summon preview ──────────────────────────────────────────
// Added 2026-08-28. The summon screen could not say how much HP a boss had or what it
// dropped, because none of it reached the client: sigils carry only SummonRaidId,
// SummonDifficulty and Tier. All of this already existed in raids.json and
// loot_tables.json and was simply never sent.

/// <summary>One raid as the summon list needs it. Content only — no player state.</summary>
public class RaidPreviewResponse
{
    public string RaidDefinitionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Standard | World | Event | Guild.</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>
    /// Danger class — Common | Deadly | Elite | Mythic. Separate from the NAME on purpose, so the
    /// UI can badge it and a later re-theme does not become a rewrite.
    /// </summary>
    public string Grade { get; set; } = string.Empty;
    public string ArtKey { get; set; } = string.Empty;

    /// <summary>
    /// Health of a full-size raid. ZERO means the raid has no collective health at all and is
    /// decided by its timer, with rewards paid from a damage ladder — which is how World raids
    /// work as of 2026-08-29. A client must not render a health bar for those.
    /// </summary>
    public long BaseHp { get; set; }

    /// <summary>
    /// Health when a sigil summons a personal-size raid, which is what the summon screen is
    /// actually about. Falls back to <see cref="BaseHp"/> when the definition leaves it at 0.
    /// </summary>
    public long PersonalHp { get; set; }

    /// <summary>Hours the raid stays open once summoned.</summary>
    public int TimerHours { get; set; }

    /// <summary>The difficulties this raid's loot table actually defines.</summary>
    public List<string> Difficulties { get; set; } = new();
}

/// <summary>
/// What one difficulty pays out, by contribution bracket.
///
/// Raid loot is not a flat drop list — it is bracketed on the share of damage you deal, and the
/// brackets are the only part of it a player can influence. Anything rendering this should keep
/// that shape rather than flattening it into "things this boss drops".
/// </summary>
public class RaidLootPreviewResponse
{
    public string RaidDefinitionId { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;

    /// <summary>Ascending by contribution.</summary>
    public List<LootBracketResponse> Brackets { get; set; } = new();
}

public class LootBracketResponse
{
    /// <summary>Share of total damage required to reach this bracket, as a percentage.</summary>
    public double ContributionPercent { get; set; }

    /// <summary>
    /// Absolute damage required, on a timer-only raid. Zero means this rung is percentage-keyed.
    /// A client should render whichever of the two is non-zero — they are never both meaningful.
    /// </summary>
    public long DamageThreshold { get; set; }

    public int StatPoints { get; set; }
    public int AttackPoints { get; set; }
    public int DefensePoints { get; set; }
    public int DiscernmentPoints { get; set; }

    public List<LootDropResponse> Drops { get; set; } = new();
}

public class LootDropResponse
{
    /// <summary>Item | Magic | Unit | Legion | Gear.</summary>
    public string Kind { get; set; } = string.Empty;

    public string DefinitionId { get; set; } = string.Empty;

    /// <summary>Resolved display name; falls back to the id when the definition is unknown.</summary>
    public string Name { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;

    public int Quantity { get; set; } = 1;

    /// <summary>0..1.</summary>
    public double Chance { get; set; } = 1.0;
}
