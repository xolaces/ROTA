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

        // Application services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IEnergyService, EnergyService>();
        services.AddScoped<IPlayerService, PlayerService>();
        services.AddScoped<IGemService, GemService>();
        services.AddScoped<IStatService, StatService>();
        services.AddScoped<IQuestService, QuestService>();
        services.AddScoped<IRaidService, RaidService>();
        services.AddScoped<IItemService, ItemService>();

        // FluentValidation — scan Application assembly for all IValidator<T> implementations
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

        return services;
    }
}
