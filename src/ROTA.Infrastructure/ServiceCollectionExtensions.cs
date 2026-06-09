using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Application.Validators;
using ROTA.Infrastructure.Persistence.Repositories;
using ROTA.Infrastructure.Services;

namespace ROTA.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRotaServices(
        this IServiceCollection services,
        string contentRootPath = "")
    {
        // Repositories — scoped to match DbContext lifetime
        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IPlayerResourceRepository, PlayerResourceRepository>();
        services.AddScoped<IGemTransactionRepository, GemTransactionRepository>();
        services.AddScoped<IQuestProgressRepository, QuestProgressRepository>();
        services.AddScoped<IQuestDifficultyProgressRepository, QuestDifficultyProgressRepository>();
        services.AddScoped<IActiveRaidRepository, ActiveRaidRepository>();
        services.AddScoped<IRaidParticipantRepository, RaidParticipantRepository>();
        services.AddScoped<IPlayerInventoryRepository, PlayerInventoryRepository>();
        services.AddScoped<IBetaKeyRepository, BetaKeyRepository>();
        services.AddScoped<IPlayerEquipmentRepository, PlayerEquipmentRepository>();
        services.AddScoped<IPlayerGearRepository, PlayerGearRepository>();
        services.AddScoped<IPlayerMagicRepository, PlayerMagicRepository>();
        services.AddScoped<IRaidMagicRepository, RaidMagicRepository>();
        services.AddScoped<IPlayerUnitRepository, PlayerUnitRepository>();
        services.AddScoped<IPlayerLegionRepository, PlayerLegionRepository>();
        services.AddScoped<IPlayerLegionSlotRepository, PlayerLegionSlotRepository>();
        services.AddScoped<IPlayerCommanderGearRepository, PlayerCommanderGearRepository>();
        services.AddScoped<ILeaderboardEntryRepository, LeaderboardEntryRepository>();
        services.AddScoped<IOutboundEmailRepository, OutboundEmailRepository>();
        services.AddScoped<IPinnacleClaimRepository, PinnacleClaimRepository>();
        services.AddScoped<IFriendshipRepository, FriendshipRepository>();
        services.AddScoped<IBlockRepository, BlockRepository>();
        services.AddScoped<IPrivateMessageRepository, PrivateMessageRepository>();
        // System 21 — Guild Foundations (Slice 1)
        services.AddScoped<IGuildRepository, GuildRepository>();
        services.AddScoped<IGuildMembershipRepository, GuildMembershipRepository>();
        services.AddScoped<IGuildJoinRequestRepository, GuildJoinRequestRepository>();
        // System 21 — Guild sigil economy (Slice 3a)
        services.AddScoped<IGuildEconomyRepository, GuildEconomyRepository>();
        // System 22 — Masteries Core (Phase A)
        services.AddScoped<IPlayerMasteryRepository, PlayerMasteryRepository>();
        services.AddScoped<IPlayerMasteryActivityRepository, PlayerMasteryActivityRepository>();
        services.AddScoped<IMasteryRespecRepository, MasteryRespecRepository>();
        services.AddScoped<IMasteryRespecCapStore, MasteryRespecCapStore>();
        // TICKET 46 — Achievement Points
        services.AddScoped<IAchievementProgressRepository, AchievementProgressRepository>();
        services.AddScoped<IAchievementAwardRepository, AchievementAwardRepository>();
        services.AddScoped<IAchievementProgressEventRepository, AchievementProgressEventRepository>();

        // System 16 — Gauntlet (Slice 2) repositories
        services.AddScoped<IGauntletEventRepository, GauntletEventRepository>();
        services.AddScoped<IGauntletEntryRepository, GauntletEntryRepository>();
        services.AddScoped<IStrikeRepository, StrikeRepository>();
        services.AddScoped<IGauntletCurrencyRepository, GauntletCurrencyRepository>();
        services.AddScoped<IPlayerGauntletTrophyRepository, PlayerGauntletTrophyRepository>();
        services.AddScoped<IPlayerEventMagicRepository, PlayerEventMagicRepository>();
        services.AddScoped<IPlayerMagicHonorRepository, PlayerMagicHonorRepository>();

        // Infrastructure services
        services.AddScoped<IAuthLockoutService, AuthLockoutService>();
        services.AddScoped<IRaidHitCache, RaidHitCache>();

        // Phase 2 — Ops & Social: operator email backbone (T39)
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddSingleton<IEmailSendQueue, EmailSendQueue>();
        // Anti-spam limiter for player submissions (T37 reports, T38 bug/ticket)
        services.AddScoped<ISubmissionRateLimiter, SubmissionRateLimiter>();
        // World-chat ring buffer (T36)
        services.AddScoped<IWorldChatStore, RedisWorldChatStore>();
        // Guild-chat ring buffer (System 21 Slice 2) — per-guild keyed
        services.AddScoped<IGuildChatStore, RedisGuildChatStore>();

        // Content definition providers — singletons: JSON files read once at startup
        services.AddSingleton<IQuestDefinitionProvider>(
            _ => new QuestDefinitionProvider(contentRootPath));
        services.AddSingleton<IRaidDefinitionProvider>(
            _ => new RaidDefinitionProvider(contentRootPath));
        services.AddSingleton<IItemDefinitionProvider>(
            _ => new ItemDefinitionProvider(contentRootPath));
        services.AddSingleton<ILootTableProvider>(sp =>
            new LootTableProvider(contentRootPath, sp.GetRequiredService<IRaidDefinitionProvider>()));
        services.AddSingleton<IGearDefinitionProvider>(
            _ => new GearDefinitionProvider(contentRootPath));
        services.AddSingleton<IMagicDefinitionProvider>(
            _ => new MagicDefinitionProvider(contentRootPath));
        services.AddSingleton<IUnitDefinitionProvider>(
            _ => new UnitDefinitionProvider(contentRootPath));
        services.AddSingleton<ILegionDefinitionProvider>(
            _ => new LegionDefinitionProvider(contentRootPath));
        // System 22 Phase A (Masteries) — the four Ancient definitions; throws at startup on bad content.
        services.AddSingleton<IMasteryDefinitionProvider>(
            _ => new MasteryDefinitionProvider(contentRootPath));
        // TICKET 46 (Achievements) — the achievement roster; throws at startup on bad content.
        services.AddSingleton<IAchievementDefinitionProvider>(
            _ => new AchievementDefinitionProvider(contentRootPath));
        // T52 — subject catalog (bug/report subject lists + feedback category); throws at startup on bad content.
        services.AddSingleton<ISubjectCatalogProvider>(
            _ => new SubjectCatalogProvider(contentRootPath));
        // System 16 Slice 1 — depends on the magic provider (validates Gauntlet magics +
        // prize magicId refs) and IOptions<GauntletConfig> (validates league bounds).
        services.AddSingleton<IGauntletContentProvider>(sp =>
            new GauntletContentProvider(
                contentRootPath,
                sp.GetRequiredService<IMagicDefinitionProvider>(),
                sp.GetRequiredService<IOptions<GauntletConfig>>()));
        // System 16 Slice 6 — token-shop catalogue. Depends on the unit/legion/gear def providers
        // for payloadId referential validation; throws at startup on a bad catalogue.
        services.AddSingleton<IGauntletShopProvider>(sp =>
            new GauntletShopProvider(
                contentRootPath,
                sp.GetRequiredService<IUnitDefinitionProvider>(),
                sp.GetRequiredService<ILegionDefinitionProvider>(),
                sp.GetRequiredService<IGearDefinitionProvider>()));

        // Leaderboard infrastructure — singleton because PeriodKeyResolver is stateless + validates config at ctor
        services.AddSingleton<IPeriodKeyResolver, PeriodKeyResolver>();

        // Application services
        services.AddScoped<IClassService, ClassService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IBetaKeyService, BetaKeyService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IEnergyService, EnergyService>();
        services.AddScoped<IPlayerService, PlayerService>();
        services.AddScoped<IGemService, GemService>();
        services.AddScoped<IStatService, StatService>();
        services.AddScoped<IQuestService, QuestService>();
        services.AddScoped<IRaidService, RaidService>();
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<IEquipmentService, EquipmentService>();
        services.AddScoped<IMagicService, MagicService>();
        services.AddScoped<ILegionService, LegionService>();
        services.AddScoped<ILeaderboardService, LeaderboardService>();
        services.AddScoped<IEmailNotificationService, EmailNotificationService>();
        services.AddScoped<IPinnacleService, PinnacleService>();
        services.AddScoped<ISocialService, SocialService>();
        services.AddScoped<IGuildService, GuildService>();
        services.AddScoped<IGuildEconomyService, GuildEconomyService>();
        services.AddScoped<IMasteryService, MasteryService>();
        services.AddScoped<IAchievementService, AchievementService>();

        // System 16 — Gauntlet (Slice 2) services
        services.AddScoped<IGauntletService, GauntletService>();
        services.AddScoped<IGauntletAdminService, GauntletAdminService>();

        // System 16 — Gauntlet (Slice 3) scoring / leaderboard read service
        services.AddScoped<IGauntletScoringService, GauntletScoringService>();

        // FluentValidation — scan Application assembly for all IValidator<T> implementations
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

        return services;
    }
}
