// Core player identity, progression, and economy state.
// Effective stats are never stored here - computed server-side on demand.
namespace ROTA.Domain.Entities;
using ROTA.Domain.Enums;

public class Player
{
    // Required by EF Core
    private Player() { }

    /// <summary>
    /// Creates a new player with default <see cref="PlayerRoles.Player"/> role.
    /// Seeds Stats and the three resource pools (Energy, Stamina, GuildStamina).
    /// </summary>
    public static Player Create(string username, string email, string passwordHash)
    {
        var player = new Player
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = email.ToLowerInvariant(),
            PasswordHash = passwordHash,
            Level = 1,
            Experience = 0,
            Gold = 0,
            Roles = PlayerRoles.Player,
            DisplayName = username,
            IsBanned = false,
            IsDeleted = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        player.Stats = PlayerStats.Create(player.Id);

        player.Resources = new List<PlayerResource>
        {
            PlayerResource.Create(player.Id, ResourceType.Energy,       maxValue: 25, regenPerMinute: 2),
            PlayerResource.Create(player.Id, ResourceType.Stamina,      maxValue: 5,  regenPerMinute: 1),
            PlayerResource.Create(player.Id, ResourceType.GuildStamina, maxValue: 1,  regenPerMinute: 0),
        };

        return player;
    }

    /// <summary>
    /// Creates a new player with a pre-allocated <paramref name="id"/>.
    /// Used during beta-gated registration where the player ID must be known before the row
    /// is inserted (so the beta key can be linked atomically in one transaction).
    /// </summary>
    public static Player CreateWithId(Guid id, string username, string email, string passwordHash)
    {
        var player = new Player
        {
            Id = id,
            Username = username,
            Email = email.ToLowerInvariant(),
            PasswordHash = passwordHash,
            Level = 1,
            Experience = 0,
            Gold = 0,
            Roles = PlayerRoles.Player,
            DisplayName = username,
            IsBanned = false,
            IsDeleted = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        player.Stats = PlayerStats.Create(id);

        player.Resources = new List<PlayerResource>
        {
            PlayerResource.Create(id, ResourceType.Energy,       maxValue: 25, regenPerMinute: 2),
            PlayerResource.Create(id, ResourceType.Stamina,      maxValue: 5,  regenPerMinute: 1),
            PlayerResource.Create(id, ResourceType.GuildStamina, maxValue: 1,  regenPerMinute: 0),
        };

        return player;
    }

    public Player(string username, string email, string passwordHash)
    {
        Username = username;
        Email = email;
        PasswordHash = passwordHash;
    }

    /// <summary>Unique player identifier.</summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Unique login name (max 32 chars).</summary>
    public string Username { get; private set; } = string.Empty;

    /// <summary>Unique email address (lowercase-normalised).</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>BCrypt(12) password hash.</summary>
    public string PasswordHash { get; private set; } = string.Empty;

    // Progression
    public int Level { get; private set; } = 1;
    public long Experience { get; private set; } = 0;
    public PlayerClass Class { get; private set; } = PlayerClass.Conscript;

    // Economy
    public long Gold { get; private set; } = 0;
    // Gem balance is never stored - computed from gem_transactions ledger

    // Roles & identity
    /// <summary>Bitwise role flags. Defaults to <see cref="PlayerRoles.Player"/>.</summary>
    public PlayerRoles Roles { get; private set; } = PlayerRoles.Player;

    /// <summary>Display name shown in the game UI (max 48 chars).</summary>
    public string DisplayName { get; private set; } = string.Empty;

    // Guild
    public Guid? GuildId { get; private set; }
    public string? GuildRank { get; private set; }

    // Status
    public bool IsBanned { get; private set; } = false;
    public string? BanReason { get; private set; }

    // Timestamps
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; private set; } = false;

    // Navigation properties (EF Core)
    public PlayerStats? Stats { get; private set; }
    public ICollection<PlayerResource> Resources { get; private set; } = new List<PlayerResource>();

    // Domain methods

    /// <summary>Grants the specified role flag. Bumps UpdatedAt.</summary>
    public void GrantRole(PlayerRoles role)
    {
        Roles |= role;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Revokes the specified role flag. Bumps UpdatedAt.
    /// <see cref="PlayerRoles.Player"/> is NEVER removed — it is the base authenticated role.
    /// </summary>
    public void RevokeRole(PlayerRoles role)
    {
        Roles &= ~role;
        Roles |= PlayerRoles.Player; // Player flag is permanent
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Returns true if the player has ALL of the specified role flags.</summary>
    public bool HasRole(PlayerRoles role) => (Roles & role) == role;

    /// <summary>Updates the display name (max 48 chars). Bumps UpdatedAt.</summary>
    public void UpdateDisplayName(string displayName)
    {
        DisplayName = displayName;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Ban(string reason)
    {
        IsBanned = true;
        BanReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateUsername(string username)
    {
        Username = username;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public IReadOnlyList<int> AddExperience(long amount, Func<int, int> xpToNextLevel)
    {
        Experience += amount;
        var levelUps = new List<int>();
        int xpNeeded = xpToNextLevel(Level);
        if (xpNeeded <= 0) // safety: malformed config guard
        {
            UpdatedAt = DateTimeOffset.UtcNow;
            return levelUps;
        }
        while (Experience >= xpNeeded)
        {
            Experience -= xpNeeded;
            Level++;
            levelUps.Add(Level);
            xpNeeded = xpToNextLevel(Level);
            if (xpNeeded <= 0) break; // safety
        }
        UpdatedAt = DateTimeOffset.UtcNow;
        return levelUps;
    }

    public void SetClass(PlayerClass newClass)
    {
        Class = newClass;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AddGold(long amount)
    {
        Gold += amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
