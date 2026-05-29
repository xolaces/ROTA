using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Seeding;

namespace ROTA.Api;

/// <summary>
/// Admin bootstrap CLI — invoked when the first argument is a recognized command.
/// Reuses the application's service layer; never duplicates business logic.
/// </summary>
/// <remarks>
/// Usage (from the ROTA.Api project directory):
/// <code>
///   dotnet run --project src/ROTA.Api -- seed-admin
///   dotnet run --project src/ROTA.Api -- gen-beta-key [count]
///   dotnet run --project src/ROTA.Api -- promote {user|guid} {Role}
///   dotnet run --project src/ROTA.Api -- demote {user|guid} {Role}
/// </code>
/// </remarks>
public static class AdminCli
{
    private static readonly HashSet<string> KnownCommands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "seed-admin",
            "gen-beta-key",
            "promote",
            "demote",
        };

    /// <summary>Returns true if <paramref name="firstArg"/> is a recognised CLI command.</summary>
    public static bool IsCommand(string firstArg)
        => KnownCommands.Contains(firstArg);

    /// <summary>
    /// Builds the service container, applies pending migrations, runs the requested command,
    /// and returns an OS exit code. Kestrel is never started.
    /// </summary>
    public static async Task<int> RunAsync(string[] args, WebApplicationBuilder builder)
    {
        var app = builder.Build();

        // Apply any pending migrations so commands work against a fresh DB.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            await db.Database.MigrateAsync();
        }

        var command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "seed-admin"    => await RunSeedAdmin(app.Services),
                "gen-beta-key"  => await RunGenBetaKey(app.Services, args),
                "promote"       => await RunRoleChange(app.Services, args, grant: true),
                "demote"        => await RunRoleChange(app.Services, args, grant: false),
                _               => UnknownCommand(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CLI error: {ex.Message}");
            return 1;
        }
    }

    // -----------------------------------------------------------------------
    // Command implementations
    // -----------------------------------------------------------------------

    private static async Task<int> RunSeedAdmin(IServiceProvider services)
    {
        await SeedData.EnsureAdminAsync(services);
        Console.WriteLine("seed-admin: complete.");
        return 0;
    }

    private static async Task<int> RunGenBetaKey(IServiceProvider services, string[] args)
    {
        int count = 1;
        if (args.Length >= 2 && !int.TryParse(args[1], out count))
        {
            Console.Error.WriteLine($"gen-beta-key: invalid count '{args[1]}'. Usage: gen-beta-key [count]");
            return 1;
        }

        if (count < 1 || count > 100)
        {
            Console.Error.WriteLine("gen-beta-key: count must be between 1 and 100.");
            return 1;
        }

        using var scope = services.CreateScope();
        var betaKeyService = scope.ServiceProvider.GetRequiredService<IBetaKeyService>();

        // Guid.Empty = CLI/system actor (no DB actor check, no CreatedByPlayerId written)
        var keys = await betaKeyService.GenerateAsync(actorPlayerId: Guid.Empty, count);

        Console.WriteLine($"Generated {keys.Count} key(s):");
        foreach (var k in keys)
            Console.WriteLine($"  {k.Key}");

        return 0;
    }

    private static async Task<int> RunRoleChange(IServiceProvider services, string[] args, bool grant)
    {
        var verb = grant ? "promote" : "demote";

        if (args.Length < 3)
        {
            Console.Error.WriteLine($"{verb}: usage: {verb} <user|guid> <Role>");
            return 1;
        }

        var target   = args[1];
        var roleArg  = args[2];

        if (!Enum.TryParse<PlayerRoles>(roleArg, ignoreCase: true, out var role))
        {
            Console.Error.WriteLine($"{verb}: '{roleArg}' is not a valid role. Valid roles: Admin, Moderator.");
            return 1;
        }

        using var scope = services.CreateScope();
        var adminService = scope.ServiceProvider.GetRequiredService<IAdminService>();

        var result = grant
            ? await adminService.GrantRoleAsync(Guid.Empty, target, role)
            : await adminService.RevokeRoleAsync(Guid.Empty, target, role);

        if (!result.Success)
        {
            Console.Error.WriteLine($"{verb}: failed — {result.FailureReason}");
            return 1;
        }

        Console.WriteLine($"{verb}: {target} → {role} {(grant ? "granted" : "revoked")}.");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'. Valid commands: seed-admin, gen-beta-key, promote, demote.");
        return 1;
    }
}
