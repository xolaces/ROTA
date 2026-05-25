using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence.Configurations;

namespace ROTA.Infrastructure.Persistence;

/// <summary>
/// Primary EF Core database context for ROTA.
/// All entity configurations are defined in separate Fluent API
/// configuration classes — no data annotations on domain entities.
/// </summary>
public class RotaDbContext : DbContext
{
    public RotaDbContext(DbContextOptions<RotaDbContext> options) : base(options) { }

    // ----- Phase 0 — Auth + Player Foundation -----
    public DbSet<Player> Players => Set<Player>();
    public DbSet<PlayerStats> PlayerStats => Set<PlayerStats>();
    public DbSet<PlayerResource> PlayerResources => Set<PlayerResource>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // ----- Phase 1 — Beta Core -----
    public DbSet<GemTransaction> GemTransactions => Set<GemTransaction>();
    public DbSet<PlayerQuestProgress> PlayerQuestProgress => Set<PlayerQuestProgress>();
    public DbSet<ActiveRaid> ActiveRaids => Set<ActiveRaid>();
    public DbSet<RaidParticipant> RaidParticipants => Set<RaidParticipant>();
    // public DbSet<ItemDefinition> ItemDefinitions => Set<ItemDefinition>();
    // public DbSet<PlayerInventory> PlayerInventory => Set<PlayerInventory>();
    // public DbSet<DragonDefinition> DragonDefinitions => Set<DragonDefinition>();
    // public DbSet<PlayerDragon> PlayerDragons => Set<PlayerDragon>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity configurations from this assembly automatically.
        // Any class implementing IEntityTypeConfiguration<T> in this project
        // is picked up here — no manual registration needed per entity.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RotaDbContext).Assembly);
    }
}
