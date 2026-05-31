using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddScoped<IPlayerMagicRepository, PlayerMagicRepository>();

        // Infrastructure services
        services.AddScoped<IAuthLockoutService, AuthLockoutService>();
        services.AddScoped<IRaidHitCache, RaidHitCache>();

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

        // FluentValidation — scan Application assembly for all IValidator<T> implementations
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

        return services;
    }
}
