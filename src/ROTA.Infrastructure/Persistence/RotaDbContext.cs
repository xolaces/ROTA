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
    public DbSet<PunishmentLog> PunishmentLogs => Set<PunishmentLog>();

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

    // ----- T65 — Password Reset -----
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    // ----- System 13 — Character Gear -----
    public DbSet<PlayerEquipment> PlayerEquipment => Set<PlayerEquipment>();

    // ----- System 14 — Raid Magic -----
    public DbSet<PlayerMagic> PlayerMagics => Set<PlayerMagic>();
    public DbSet<RaidMagic>   RaidMagics   => Set<RaidMagic>();

    // ----- System 15 — Legion -----
    public DbSet<PlayerUnit>             PlayerUnits          => Set<PlayerUnit>();
    public DbSet<PlayerLegion>           PlayerLegions        => Set<PlayerLegion>();
    public DbSet<PlayerLegionSlot>       PlayerLegionSlots    => Set<PlayerLegionSlot>();
    public DbSet<PlayerCommanderGear>    PlayerCommanderGear  => Set<PlayerCommanderGear>();

    // ----- System 17 — Global Leaderboards -----
    public DbSet<LeaderboardEntry> LeaderboardEntries => Set<LeaderboardEntry>();

    // ----- System 18 — Gear Ownership -----
    public DbSet<PlayerGear> PlayerGear => Set<PlayerGear>();

    // ----- System 16 — Gauntlet (Slice 2) -----
    public DbSet<GauntletEvent> GauntletEvents => Set<GauntletEvent>();
    public DbSet<GauntletEntry> GauntletEntries => Set<GauntletEntry>();
    public DbSet<StrikeTransaction> StrikeTransactions => Set<StrikeTransaction>();
    public DbSet<GauntletCurrencyTransaction> GauntletCurrencyTransactions => Set<GauntletCurrencyTransaction>();
    public DbSet<PlayerGauntletTrophy> PlayerGauntletTrophies => Set<PlayerGauntletTrophy>();
    public DbSet<PlayerEventMagic> PlayerEventMagics => Set<PlayerEventMagic>();
    public DbSet<PlayerMagicHonor> PlayerMagicHonors => Set<PlayerMagicHonor>();

    // ----- System 24 (D8) — Gauntlet battalion -----
    public DbSet<PlayerGauntletBattalion> PlayerGauntletBattalions => Set<PlayerGauntletBattalion>();

    // ----- Phase 2 — Ops & Social -----
    public DbSet<OutboundEmail> OutboundEmails => Set<OutboundEmail>();
    public DbSet<PinnacleFirstClaim> PinnacleFirstClaims => Set<PinnacleFirstClaim>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<PlayerBlock> PlayerBlocks => Set<PlayerBlock>();
    public DbSet<PrivateMessage> PrivateMessages => Set<PrivateMessage>();

    // ----- System 21 — Guild / Clan Foundations (Slice 1) -----
    public DbSet<Guild> Guilds => Set<Guild>();
    public DbSet<GuildMembership> GuildMemberships => Set<GuildMembership>();
    public DbSet<GuildJoinRequest> GuildJoinRequests => Set<GuildJoinRequest>();

    // ----- System 21 — Guild sigil economy (Slice 3a) -----
    public DbSet<GuildCurrencyTransaction> GuildCurrencyTransactions => Set<GuildCurrencyTransaction>();
    public DbSet<GuildSigilPoolTransaction> GuildSigilPoolTransactions => Set<GuildSigilPoolTransaction>();

    // ----- System 22 — Masteries Core (Phase A) -----
    public DbSet<PlayerMastery> PlayerMasteries => Set<PlayerMastery>();
    public DbSet<PlayerMasteryActivity> PlayerMasteryActivities => Set<PlayerMasteryActivity>();
    public DbSet<MasteryActivityEvent> MasteryActivityEvents => Set<MasteryActivityEvent>();
    public DbSet<MasteryRespecTransaction> MasteryRespecTransactions => Set<MasteryRespecTransaction>();

    // ----- TICKET 46 — Achievement Points -----
    public DbSet<AchievementProgress> AchievementProgress => Set<AchievementProgress>();
    public DbSet<AchievementAward> AchievementAwards => Set<AchievementAward>();
    public DbSet<AchievementProgressEvent> AchievementProgressEvents => Set<AchievementProgressEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RotaDbContext).Assembly);
    }

    // ----- audit_log and punishment_log are APPEND-ONLY ----------------------------------------
    // CLAUDE.md states the rule for audit_log; northstar §6 states it for punishment_log ("Append-only,
    // like the audit log. Non-negotiable."). Until this existed nothing enforced either. Neither entity
    // has mutators and neither repository exposes anything but AppendAsync, so the rule held by
    // convention -- but this context exposes both DbSets, and any future code could Remove() or mutate
    // a tracked row and have it silently persist. A tamperable record is worth less than no record,
    // because it still looks authoritative.
    //
    // The guard lives on SaveChanges rather than in an interceptor so it cannot be lost by a missed DI
    // registration: it applies to every context instance, including the design-time factory and any
    // test that constructs one directly. A database-level trigger backs it up for anything that does
    // not come through EF at all.

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAppendOnlyTables();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAppendOnlyTables();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Throws if this unit of work would UPDATE or DELETE a row in an append-only table. Deliberately
    /// fails loudly rather than dropping the change: silently discarding it would leave the caller
    /// believing an edit succeeded, which is its own kind of dishonest record.
    /// </summary>
    private void GuardAppendOnlyTables()
    {
        foreach (var entry in ChangeTracker.Entries<AuditLog>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"audit_log is append-only: attempted to {entry.State.ToString().ToUpperInvariant()} "
                    + $"audit entry {entry.Entity.Id} (action '{entry.Entity.Action}'). "
                    + "Append a correcting entry instead of editing history.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<PunishmentLog>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"punishment_log is append-only: attempted to "
                    + $"{entry.State.ToString().ToUpperInvariant()} punishment entry {entry.Entity.Id} "
                    + $"({entry.Entity.Type} against {entry.Entity.TargetUsername}). "
                    + "A reversal is a NEW entry; history is never edited.");
            }
        }
    }
}
