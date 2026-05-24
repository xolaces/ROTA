using Microsoft.Extensions.DependencyInjection;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Infrastructure.Persistence.Repositories;

namespace ROTA.Infrastructure;

// BETA - Full implementation. Add new registrations here as systems are built.
/// <summary>
/// Extension method to register all Infrastructure and Application services in DI.
/// Called once from Program.cs: builder.Services.AddRotaServices();
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRotaServices(this IServiceCollection services)
    {
        // Repositories
        // Scoped: one instance per HTTP request. Matches DbContext lifetime.
        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

        // Application Services
        services.AddScoped<IAuthService, AuthService>();

        // TODO-PHASE1: register remaining services as systems are built
        // services.AddScoped<IEnergyService,  EnergyService>();
        // services.AddScoped<IQuestService,   QuestService>();
        // services.AddScoped<ICombatService,  CombatService>();

        return services;
    }
}