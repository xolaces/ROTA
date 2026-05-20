namespace ROTA.Domain.Entities;

/// <summary>
/// Core player identity, progression, and economy state.
/// Effective stats (with bonuses) are computed server-side — never stored here.
/// </summary>
public class Player
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Username { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;

    // Progression
    public int Level { get; private set; } = 1;
    public long Experience { get; private set; } = 0;

    // Economy
    public long Gold { get; private set; } = 0;
    // Gem balance is never stored here — computed from gem_transactions ledger

    // Guild
    public Guid? GuildId { get; private set; }
    public string? GuildRank { get; private set; }

    // Status
    public bool IsBanned { get; private set; } = false;
    public string? BanReason { get; private set; }

    // Soft delete + audit timestamps
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; private set; } = false;

    // Navigation properties (EF Core)
    public PlayerStats? Stats { get; private set; }
    public ICollection<PlayerResource> Resources { get; private set; } = new List<PlayerResource>();
}