using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence.Configurations;

namespace ROTA.Infrastructure.Persistence;

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

    // ----- Phase 1 Extensions -----
    public DbSet<PlayerQuestDifficultyProgress> PlayerQuestDifficultyProgress => Set<PlayerQuestDifficultyProgress>();
    public DbSet<PlayerInventoryItem> PlayerInventoryItems => Set<PlayerInventoryItem>();

    // ----- System 12 — Beta Access Control -----
    public DbSet<BetaKey> BetaKeys => Set<BetaKey>();

    // ----- System 13 — Character Gear -----
    public DbSet<PlayerEquipment> PlayerEquipment => Set<PlayerEquipment>();

    // ----- System 14 — Raid Magic -----
    public DbSet<PlayerMagic> PlayerMagics => Set<PlayerMagic>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RotaDbContext).Assembly);
    }
}
