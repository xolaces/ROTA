using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Seeding;

/// <summary>
/// One-time idempotent data seeding run at application startup.
/// </summary>
public static class SeedData
{
    /// <summary>
    /// Ensures the bootstrap admin account "Xolaces" exists.
    /// Reads <c>Seed:AdminPassword</c> from configuration — NEVER falls back to a hardcoded default.
    /// If the password is missing, logs a warning and returns without creating the account.
    /// Idempotent: a second call with the same account already present is a no-op.
    /// </summary>
    /// <param name="sp">The root application <see cref="IServiceProvider"/>.</param>
    public static async Task EnsureAdminAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var config  = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logFac  = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger  = logFac.CreateLogger("ROTA.Infrastructure.Seeding.SeedData");
        var players = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        var audit   = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        // Idempotency guard: skip if the admin account already exists.
        if (await players.UsernameExistsAsync("Xolaces"))
        {
            logger.LogInformation("Seed: admin account 'Xolaces' already exists — skipping.");
            return;
        }

        // SECURITY: password is REQUIRED from config — never hardcode a default.
        var adminPassword = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogWarning(
                "Seed: Seed:AdminPassword is not configured. " +
                "Admin account 'Xolaces' was NOT created. " +
                "Set the value via user-secrets or environment variable to enable seeding.");
            return;
        }

        var adminEmail = config["Seed:AdminEmail"] ?? "xolaces@rota.dev";

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword, workFactor: 12);
        var admin = Player.Create("Xolaces", adminEmail, passwordHash);
        admin.UpdateDisplayName("DEV_Xolaces");
        admin.GrantRole(PlayerRoles.Admin);  // Roles = Player | Admin

        await players.CreateAsync(admin);

        await audit.AppendAsync(AuditLog.Create(
            admin.Id,
            "AdminSeeded",
            inputHash: null,
            resultSummary: "Bootstrap admin account 'Xolaces' created at startup",
            ipAddress: null));

        logger.LogInformation("Seed: admin account 'Xolaces' created (id={AdminId}).", admin.Id);
    }
}
