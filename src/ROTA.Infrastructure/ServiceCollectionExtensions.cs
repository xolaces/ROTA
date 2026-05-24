using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Application.Validators;
using ROTA.Infrastructure.Persistence.Repositories;
using ROTA.Infrastructure.Services;

namespace ROTA.Infrastructure;

// BETA — add new registrations here as systems are built.
public static class ServiceCollectionExtensions
{
    /// <param name="contentRootPath">
    /// The application content root — used to locate content/quests.json.
    /// Pass env.ContentRootPath from Program.cs.
    /// </param>
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

        // Infrastructure services
        services.AddScoped<IAuthLockoutService, AuthLockoutService>();

        // Quest definitions — singleton: JSON file read once at startup
        services.AddSingleton<IQuestDefinitionProvider>(
            _ => new QuestDefinitionProvider(contentRootPath));

        // Application services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IEnergyService, EnergyService>();
        services.AddScoped<IPlayerService, PlayerService>();
        services.AddScoped<IGemService, GemService>();
        services.AddScoped<IQuestService, QuestService>();

        // FluentValidation — scan Application assembly for all IValidator<T> implementations
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

        return services;
    }
}
