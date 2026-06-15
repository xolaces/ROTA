namespace ROTA.Domain.Entities;
using ROTA.Domain.Enums;

/// <summary>
/// A player's level (1→5) in one Ancient's Mastery (System 22 Phase A). One row per
/// (player, ancient); lazily created at level 1 on first read/activity. Levels are
/// <b>monotonic</b> — they only ever rise (re-spec is lossless and never lowers a level).
/// </summary>
public class PlayerMastery
{
    public const int MaxLevel = 5;

    private PlayerMastery() { }

    public static PlayerMastery Create(Guid playerId, MasteryAncient ancient)
    {
        var now = DateTimeOffset.UtcNow;
        return new PlayerMastery
        {
            Id        = Guid.NewGuid(),
            PlayerId  = playerId,
            Ancient   = ancient,
            Level     = 1,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = false,
        };
    }

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public MasteryAncient Ancient { get; private set; }
    public int Level { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    /// <summary>Advances one level toward <see cref="MaxLevel"/>. No-op at the cap.</summary>
    public void LevelUp()
    {
        if (Level >= MaxLevel) return;
        Level++;
        Touch();
    }

    /// <summary>
    /// Sets the level (1..<see cref="MaxLevel"/>). Monotonic — throws on a decrease.
    /// Used by the tier-up evaluation (Slice 4) which may advance several levels at once.
    /// </summary>
    public void SetLevel(int level)
    {
        if (level < 1 || level > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level), $"Mastery level must be 1..{MaxLevel}.");
        if (level < Level)
            throw new InvalidOperationException("Mastery levels are monotonic — cannot decrease.");
        if (level == Level) return;
        Level = level;
        Touch();
    }

    /// <summary>
    /// ADMIN/DEV ONLY — sets the level to any value in 1..<see cref="MaxLevel"/>, bypassing the
    /// monotonic guard (so dev tooling can force a level up OR down for testing). Never call from
    /// gameplay; <see cref="SetLevel"/> / <see cref="LevelUp"/> are the player-facing paths.
    /// </summary>
    public void ForceSetLevel(int level)
    {
        if (level < 1 || level > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level), $"Mastery level must be 1..{MaxLevel}.");
        if (level == Level) return;
        Level = level;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
