// Core player identity, progression, and economy state.
// Effective stats are never stored here � computed server-side on demand.
namespace ROTA.Domain.Entities;
using ROTA.Domain.Enums;

public class Player
{

    // Required by EF Core
    private Player() { }
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

    public Player(string username, string email, string passwordHash)
    {
        Username = username;
        Email = email;
        PasswordHash = passwordHash;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Username { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;

    // Progression
    public int Level { get; private set; } = 1;
    public long Experience { get; private set; } = 0;

    // Economy
    public long Gold { get; private set; } = 0;
    // Gem balance is never stored � computed from gem_transactions ledger

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

    public void AddGold(long amount)
    {
        Gold += amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}