using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
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
///   dotnet run --project src/ROTA.Api -- flag-dev {user|guid}
///   dotnet run --project src/ROTA.Api -- unflag-dev {user|guid}
///   dotnet run --project src/ROTA.Api -- leaderboard-refresh-stat
///   dotnet run --project src/ROTA.Api -- mastery-refresh-rating
///   dotnet run --project src/ROTA.Api -- grant-gear {user|guid} {gearDefId} [qty]
///   dotnet run --project src/ROTA.Api -- grant-starter-kit {user|guid|--all}
///   dotnet run --project src/ROTA.Api -- gauntlet-open {name} {startsAt} {endsAt}
///   dotnet run --project src/ROTA.Api -- gauntlet-close {eventId}
///   dotnet run --project src/ROTA.Api -- gauntlet-settle {eventId}
///   dotnet run --project src/ROTA.Api -- beta-reset [--purge-accounts] [--keep name,name] [--confirm WIPE-BETA]
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
            "flag-dev",
            "unflag-dev",
            "leaderboard-refresh-stat",
            "mastery-refresh-rating",
            "grant-gear",
            "grant-starter-kit",
            "gauntlet-open",
            "gauntlet-close",
            "gauntlet-settle",
            "beta-reset",
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
                "seed-admin"               => await RunSeedAdmin(app.Services),
                "gen-beta-key"             => await RunGenBetaKey(app.Services, args),
                "promote"                  => await RunRoleChange(app.Services, args, grant: true),
                "demote"                   => await RunRoleChange(app.Services, args, grant: false),
                "flag-dev"                 => await RunFlagDev(app.Services, args, grant: true),
                "unflag-dev"               => await RunFlagDev(app.Services, args, grant: false),
                "leaderboard-refresh-stat" => await RunLeaderboardRefreshStat(app.Services),
                "mastery-refresh-rating"   => await RunMasteryRefreshRating(app.Services),
                "grant-gear"               => await RunGrantGear(app.Services, args),
                "grant-starter-kit"        => await RunGrantStarterKit(app.Services, args),
                "gauntlet-open"            => await RunGauntletOpen(app.Services, args),
                "gauntlet-close"           => await RunGauntletClose(app.Services, args),
                "gauntlet-settle"          => await RunGauntletSettle(app.Services, args),
                "beta-reset"               => await RunBetaReset(app.Services, args),
                _                          => UnknownCommand(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CLI error: {ex.Message}");
            return 1;
        }
    }

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

    // flag/unflag a developer account. flag-dev grants the Developer flag and auto-joins the hidden
    // Dev guild ("The Dev Coffee Shop"); unflag-dev removes from the dev guild and revokes the flag.
    private static async Task<int> RunFlagDev(IServiceProvider services, string[] args, bool grant)
    {
        var verb = grant ? "flag-dev" : "unflag-dev";
        if (args.Length < 2)
        {
            Console.Error.WriteLine($"{verb}: usage: {verb} <user|guid>");
            return 1;
        }

        var target = args[1];
        var status = await SeedData.FlagDeveloperAsync(services, target, grant);
        if (status is null)
        {
            Console.Error.WriteLine($"{verb}: player '{target}' not found.");
            return 1;
        }

        Console.WriteLine($"{verb}: {status}");
        return 0;
    }

    private static async Task<int> RunLeaderboardRefreshStat(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var leaderboardService = scope.ServiceProvider.GetRequiredService<ILeaderboardService>();

        var count = await leaderboardService.SnapshotStatBoardAsync();

        Console.WriteLine($"leaderboard-refresh-stat: complete. {count} player(s) snapshotted across StatAttack, StatDefense, StatDiscernment boards.");
        return 0;
    }

    private static async Task<int> RunMasteryRefreshRating(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var masteryService = scope.ServiceProvider.GetRequiredService<IMasteryService>();

        var count = await masteryService.SnapshotRatingBoardAsync();

        Console.WriteLine($"mastery-refresh-rating: complete. {count} player(s) snapshotted onto the MasteryRating boards.");
        return 0;
    }

    // The starter kit for accounts that predate it (registration grants it from 2026-09-12).
    // Idempotent: owned pieces are not granted again and worn slots keep what they wear.
    private static async Task<int> RunGrantStarterKit(IServiceProvider services, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("grant-starter-kit: usage: grant-starter-kit <username|guid|--all>");
            return 1;
        }

        using var scope = services.CreateScope();
        var playerRepo = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        var equipment  = scope.ServiceProvider.GetRequiredService<IEquipmentService>();

        if (args[1] == "--all")
        {
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            var ids = await db.Players.AsNoTracking().Where(p => !p.IsDeleted).Select(p => p.Id).ToListAsync();
            int n = 0;
            foreach (var pid in ids)
            {
                await equipment.GrantStarterKitAsync(pid);
                n++;
            }
            Console.WriteLine($"grant-starter-kit: {n} player(s) checked; missing pieces granted, empty slots dressed.");
            return 0;
        }

        Player? player = Guid.TryParse(args[1], out var id)
            ? await playerRepo.FindByIdAsync(id)
            : await playerRepo.FindByUsernameAsync(args[1]);
        if (player is null)
        {
            Console.Error.WriteLine($"grant-starter-kit: player '{args[1]}' not found.");
            return 1;
        }

        await equipment.GrantStarterKitAsync(player.Id);
        Console.WriteLine($"grant-starter-kit: '{player.Username}' ({player.Id}) — missing pieces granted, empty slots dressed.");
        return 0;
    }

    private static async Task<int> RunGrantGear(IServiceProvider services, string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("grant-gear: usage: grant-gear <username|guid> <gearDefinitionId> [quantity]");
            return 1;
        }
        var userArg  = args[1];
        var gearId   = args[2];
        var qty      = args.Length >= 4 && int.TryParse(args[3], out var q) ? q : 1;
        if (qty < 1) { Console.Error.WriteLine("grant-gear: quantity must be >= 1"); return 1; }

        using var scope = services.CreateScope();
        var playerRepo = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        var equipment  = scope.ServiceProvider.GetRequiredService<IEquipmentService>();

        Player? player = Guid.TryParse(userArg, out var id)
            ? await playerRepo.FindByIdAsync(id)
            : await playerRepo.FindByUsernameAsync(userArg);

        if (player is null)
        {
            Console.Error.WriteLine($"grant-gear: player '{userArg}' not found.");
            return 1;
        }

        await equipment.GrantGearAsync(player.Id, gearId, qty);
        Console.WriteLine($"grant-gear: granted {qty}× '{gearId}' to player '{player.Username}' ({player.Id}).");
        return 0;
    }

    // Gauntlet event lifecycle. CLI uses the system bypass (no actor check),
    // matching the existing promote/demote/grant-gear commands.
    private static async Task<int> RunGauntletOpen(IServiceProvider services, string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("gauntlet-open: usage: gauntlet-open <name> <startsAt> <endsAt> [neck|ring]  (ISO-8601 timestamps; kind defaults to neck)");
            return 1;
        }
        var name = args[1];
        if (!DateTimeOffset.TryParse(args[2], out var startsAt))
        {
            Console.Error.WriteLine($"gauntlet-open: invalid startsAt '{args[2]}' (use ISO-8601, e.g. 2026-06-10T00:00:00Z).");
            return 1;
        }
        if (!DateTimeOffset.TryParse(args[3], out var endsAt))
        {
            Console.Error.WriteLine($"gauntlet-open: invalid endsAt '{args[3]}' (use ISO-8601, e.g. 2026-06-17T00:00:00Z).");
            return 1;
        }

        // optional 4th arg: event kind (neck = standard run, ring = the rare run).
        var kind = ROTA.Domain.Enums.GauntletEventKind.Neck;
        if (args.Length >= 5 && !Enum.TryParse(args[4], ignoreCase: true, out kind))
        {
            Console.Error.WriteLine($"gauntlet-open: invalid kind '{args[4]}' (use 'neck' or 'ring').");
            return 1;
        }

        using var scope = services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IGauntletAdminService>();
        var result = await admin.OpenEventAsync(name, startsAt, endsAt, kind);
        if (!result.Success)
        {
            Console.Error.WriteLine($"gauntlet-open: failed — {result.FailureReason}");
            return 1;
        }
        Console.WriteLine($"gauntlet-open: opened {result.Event!.Kind} event {result.Event.Id} '{result.Event.Name}' (run #{result.Event.RunNumber}, state {result.Event.State}).");
        return 0;
    }

    private static async Task<int> RunGauntletClose(IServiceProvider services, string[] args)
        => await RunGauntletLifecycle(services, args, "gauntlet-close", close: true);

    private static async Task<int> RunGauntletSettle(IServiceProvider services, string[] args)
        => await RunGauntletLifecycle(services, args, "gauntlet-settle", close: false);

    private static async Task<int> RunGauntletLifecycle(
        IServiceProvider services, string[] args, string verb, bool close)
    {
        if (args.Length < 2 || !Guid.TryParse(args[1], out var eventId))
        {
            Console.Error.WriteLine($"{verb}: usage: {verb} <eventId>");
            return 1;
        }

        using var scope = services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IGauntletAdminService>();
        var result = close
            ? await admin.CloseEventAsync(eventId)
            : await admin.SettleEventAsync(eventId);

        if (!result.Success)
        {
            Console.Error.WriteLine($"{verb}: failed — {result.FailureReason}");
            return 1;
        }
        Console.WriteLine($"{verb}: event {result.Event!.Id} → state {result.Event.State}.");
        // Surface the settlement payout counts (System 16 Slice 5) when present.
        if (result.Settlement is { } s)
            Console.WriteLine(
                $"  payout: ranks={s.RanksSettled}, tokens={s.TokensGranted}, " +
                $"pitchfork={s.PitchforkGranted}, trophies={s.TrophiesGranted}, honors={s.HonorsWritten}.");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'. Valid commands: seed-admin, gen-beta-key, promote, demote, flag-dev, unflag-dev, leaderboard-refresh-stat, mastery-refresh-rating, grant-gear, gauntlet-open, gauntlet-close, gauntlet-settle.");
        return 1;
    }

    /// <summary>
    /// Between-wave beta reset: clears the world, keeps the logins.
    /// </summary>
    /// <remarks>
    /// Runs a dry run unless <c>--confirm WIPE-BETA</c> is passed, because the failure mode of the
    /// opposite default is unrecoverable. Prints the same report either way, so what you previewed
    /// is what you get.
    /// </remarks>
    private static async Task<int> RunBetaReset(IServiceProvider services, string[] args)
    {
        const string Phrase = "WIPE-BETA";

        bool purge = args.Contains("--purge-accounts", StringComparer.OrdinalIgnoreCase);
        var keep = new List<string>();
        for (int i = 1; i < args.Length - 1; i++)
            if (string.Equals(args[i], "--keep", StringComparison.OrdinalIgnoreCase))
                keep.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries
                                                   | StringSplitOptions.TrimEntries));

        int ci = Array.FindIndex(args, a => string.Equals(a, "--confirm", StringComparison.OrdinalIgnoreCase));
        bool confirmed = ci >= 0 && ci + 1 < args.Length && args[ci + 1] == Phrase;
        if (ci >= 0 && !confirmed)
        {
            Console.Error.WriteLine($"beta-reset: --confirm must be followed by exactly {Phrase}.");
            return 1;
        }

        using var scope = services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IBetaResetService>();
        var report = await svc.RunAsync(dryRun: !confirmed, purgeAccounts: purge, keepUsernames: keep);

        if (report.UnclassifiedTables.Count > 0)
        {
            Console.Error.WriteLine("beta-reset: REFUSED — these tables are not classified as keep or wipe:");
            foreach (var t in report.UnclassifiedTables) Console.Error.WriteLine($"    {t}");
            Console.Error.WriteLine("Classify them in BetaResetService before running a reset.");
            return 1;
        }

        Console.WriteLine(report.DryRun
            ? $"beta-reset DRY RUN — nothing was written. Re-run with --confirm {Phrase} to apply."
            : "beta-reset APPLIED.");
        Console.WriteLine($"  accounts reset  : {report.PlayersReset}");
        Console.WriteLine($"  accounts purged : {report.PlayersPurged}");
        if (keep.Count > 0)
            Console.WriteLine($"  accounts kept   : {string.Join(", ", keep)}");
        Console.WriteLine($"  beta keys       : {report.UnredeemedKeysDeleted} unredeemed deleted, "
                          + $"{report.RedeemedKeysKept} redeemed kept");
        Console.WriteLine($"  rows removed    : {report.TotalRowsDeleted} across "
                          + $"{report.RowsDeletedByTable.Count} table(s)");
        foreach (var kv in report.RowsDeletedByTable)
            Console.WriteLine($"      {kv.Key,-36} {kv.Value,10}");
        Console.WriteLine("  untouched       :");
        foreach (var kv in report.TablesKept)
            Console.WriteLine($"      {kv.Key,-36} {kv.Value}");
        return 0;
    }
}
