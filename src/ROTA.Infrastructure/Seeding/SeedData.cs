using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
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

        // SECURITY: password is REQUIRED from config — never hardcode a default.
        // Check this FIRST (before any DB query) so the seed is entirely passive
        // when not configured (e.g., in CI / integration test environments).
        var adminPassword = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogWarning(
                "Seed: Seed:AdminPassword is not configured. " +
                "Admin account 'Xolaces' was NOT created. " +
                "Set the value via user-secrets or environment variable to enable seeding.");
            return;
        }

        // Idempotency guard: skip if the admin account already exists.
        if (await players.UsernameExistsAsync("Xolaces"))
        {
            logger.LogInformation("Seed: admin account 'Xolaces' already exists — skipping.");
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

    /// <summary>
    /// Ensures the hidden Dev guild ("The Dev Coffee Shop") and the developer allowlist are seeded (T43).
    /// Behaviour, idempotent throughout:
    /// <list type="number">
    /// <item>Grant the <see cref="PlayerRoles.Developer"/> flag to every resolvable account in the
    ///   <c>Developer</c> config allowlist (by username and by GUID) that does not already have it.</item>
    /// <item>Ensure the Dev guild exists. If absent, create it LED BY the first resolvable dev account
    ///   (join policy InviteOnly). If NO dev account is resolvable, SKIP guild creation and log a warning
    ///   — a guild requires a non-null leader FK and we must never flag/lock the seeded admin into it.</item>
    /// <item>Auto-join every resolvable dev account that is not already in the Dev guild and not in another
    ///   guild (membership row + denormalized Player guild fields + member-count increment).</item>
    /// </list>
    /// The allowlist is EMPTY by default — when unconfigured this method does nothing but log.
    /// </summary>
    /// <param name="sp">The root application <see cref="IServiceProvider"/>.</param>
    public static async Task EnsureDevGuildAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var logFac      = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger      = logFac.CreateLogger("ROTA.Infrastructure.Seeding.SeedData");
        var players     = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        var guilds      = scope.ServiceProvider.GetRequiredService<IGuildRepository>();
        var memberships = scope.ServiceProvider.GetRequiredService<IGuildMembershipRepository>();
        var audit       = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var guildConfig = scope.ServiceProvider.GetRequiredService<IOptions<GuildConfig>>().Value;
        var devConfig   = scope.ServiceProvider.GetRequiredService<IOptions<DeveloperConfig>>().Value;

        // Resolve the configured allowlist to distinct, de-duplicated Player entities.
        var devPlayers = await ResolveAllowlistAsync(players, devConfig, logger);
        if (devPlayers.Count == 0)
        {
            logger.LogInformation(
                "Seed: developer allowlist is empty (or no entries resolved). Dev guild seeding is a no-op.");
            return;
        }

        // 1) Grant the Developer flag to any allowlisted account missing it.
        foreach (var p in devPlayers)
        {
            if (p.HasRole(PlayerRoles.Developer)) continue;
            p.GrantRole(PlayerRoles.Developer);
            await players.UpdateAsync(p);
            await audit.AppendAsync(AuditLog.Create(
                p.Id, "DevFlagGranted", null,
                $"Developer flag granted at startup to '{p.Username}'", null));
            logger.LogInformation("Seed: Developer flag granted to '{Username}' ({Id}).", p.Username, p.Id);
        }

        // 2) Ensure the Dev guild exists (create led by the first resolvable dev account if absent).
        var devGuild = await guilds.FindByTagAsync(guildConfig.DevGuildTag);
        if (devGuild is null)
        {
            var leader = devPlayers[0];
            devGuild = Guild.Create(
                leader.Id,
                guildConfig.DevGuildName,
                guildConfig.DevGuildTag,
                guildConfig.DevGuildDescription,
                GuildJoinPolicy.InviteOnly,
                guildConfig.MemberCap);
            await guilds.CreateAsync(devGuild);
            await memberships.CreateAsync(GuildMembership.Create(devGuild.Id, leader.Id, GuildRank.Leader));
            leader.JoinGuild(devGuild.Id, GuildRank.Leader);
            await players.UpdateAsync(leader);

            await audit.AppendAsync(AuditLog.Create(
                leader.Id, "DevGuildSeeded", null,
                $"Dev guild '{devGuild.Name}' (tag {devGuild.Tag}) seeded, led by '{leader.Username}'", null));
            logger.LogInformation(
                "Seed: dev guild '{Name}' created (id={Id}, leader={Username}).",
                devGuild.Name, devGuild.Id, leader.Username);
        }

        // 3) Auto-join every dev account not already in the Dev guild and not in another guild.
        foreach (var p in devPlayers)
        {
            // Already in the Dev guild (e.g. the leader created above, or a prior run) → skip.
            if (p.GuildId == devGuild.Id) continue;
            // In a DIFFERENT guild — leave that alone; the service gates prevent new cross-joins.
            if (p.GuildId is not null) continue;
            if (await memberships.FindByPlayerAsync(p.Id) is not null) continue;

            // Mirror GuildService.JoinAsMemberAsync (membership + player sync + count++).
            await memberships.CreateAsync(GuildMembership.Create(devGuild.Id, p.Id, GuildRank.Member));
            p.JoinGuild(devGuild.Id, GuildRank.Member);
            await players.UpdateAsync(p);
            devGuild.IncrementMemberCount();
            await guilds.UpdateAsync(devGuild);

            await audit.AppendAsync(AuditLog.Create(
                p.Id, "DevGuildJoined", null,
                $"Developer '{p.Username}' auto-joined dev guild '{devGuild.Name}'", null));
            logger.LogInformation("Seed: developer '{Username}' auto-joined the dev guild.", p.Username);
        }
    }

    /// <summary>
    /// Resolves the <c>Developer</c> allowlist (usernames + GUIDs) to a de-duplicated list of Players.
    /// Unresolved entries are logged and skipped. Used by both startup seeding and the CLI flag command.
    /// </summary>
    internal static async Task<IReadOnlyList<Player>> ResolveAllowlistAsync(
        IPlayerRepository players, DeveloperConfig devConfig, ILogger logger)
    {
        var byId = new Dictionary<Guid, Player>();

        foreach (var raw in (devConfig.Usernames ?? []).Concat(devConfig.PlayerIds ?? []))
        {
            var entry = (raw ?? string.Empty).Trim();
            if (entry.Length == 0) continue;

            var player = Guid.TryParse(entry, out var id)
                ? await players.FindByIdAsync(id)
                : await players.FindByUsernameAsync(entry);

            if (player is null)
            {
                logger.LogWarning("Seed: developer allowlist entry '{Entry}' did not resolve to a player.", entry);
                continue;
            }
            byId[player.Id] = player;
        }

        return byId.Values.ToList();
    }

    /// <summary>
    /// CLI entry point (T43) for the <c>flag-dev</c> / <c>unflag-dev</c> commands. Resolves
    /// <paramref name="targetUsernameOrId"/> to a single player, then:
    /// <list type="bullet">
    /// <item><b>grant=true</b>: grants the <see cref="PlayerRoles.Developer"/> flag, ensures the Dev guild
    ///   exists (created led by this player if absent), and auto-joins this player to the Dev guild when
    ///   they are not already in a guild.</item>
    /// <item><b>grant=false</b>: removes this player from the Dev guild (if a member) and revokes the
    ///   Developer flag.</item>
    /// </list>
    /// Idempotent. Returns a human-readable status string; null when the target did not resolve.
    /// </summary>
    public static async Task<string?> FlagDeveloperAsync(IServiceProvider sp, string targetUsernameOrId, bool grant)
    {
        using var scope = sp.CreateScope();
        var logFac      = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var logger      = logFac.CreateLogger("ROTA.Infrastructure.Seeding.SeedData");
        var players     = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        var guilds      = scope.ServiceProvider.GetRequiredService<IGuildRepository>();
        var memberships = scope.ServiceProvider.GetRequiredService<IGuildMembershipRepository>();
        var audit       = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var guildConfig = scope.ServiceProvider.GetRequiredService<IOptions<GuildConfig>>().Value;

        var entry = (targetUsernameOrId ?? string.Empty).Trim();
        var player = Guid.TryParse(entry, out var id)
            ? await players.FindByIdAsync(id)
            : await players.FindByUsernameAsync(entry);
        if (player is null) return null;

        if (grant)
        {
            if (!player.HasRole(PlayerRoles.Developer))
            {
                player.GrantRole(PlayerRoles.Developer);
                await players.UpdateAsync(player);
                await audit.AppendAsync(AuditLog.Create(
                    player.Id, "DevFlagGranted", null,
                    $"Developer flag granted via CLI to '{player.Username}'", null));
            }

            // Ensure the Dev guild exists (created led by this player if absent).
            var devGuild = await guilds.FindByTagAsync(guildConfig.DevGuildTag);
            if (devGuild is null)
            {
                devGuild = Guild.Create(
                    player.Id, guildConfig.DevGuildName, guildConfig.DevGuildTag,
                    guildConfig.DevGuildDescription, GuildJoinPolicy.InviteOnly, guildConfig.MemberCap);
                await guilds.CreateAsync(devGuild);
                await memberships.CreateAsync(GuildMembership.Create(devGuild.Id, player.Id, GuildRank.Leader));
                player.JoinGuild(devGuild.Id, GuildRank.Leader);
                await players.UpdateAsync(player);
                await audit.AppendAsync(AuditLog.Create(
                    player.Id, "DevGuildSeeded", null,
                    $"Dev guild '{devGuild.Name}' (tag {devGuild.Tag}) seeded via CLI, led by '{player.Username}'", null));
                return $"flagged '{player.Username}' as Developer and created dev guild '{devGuild.Name}' (leader).";
            }

            // Auto-join only when not already in a guild (never disturb an existing membership).
            if (player.GuildId == devGuild.Id)
                return $"flagged '{player.Username}' as Developer (already a dev-guild member).";
            if (player.GuildId is not null || await memberships.FindByPlayerAsync(player.Id) is not null)
                return $"flagged '{player.Username}' as Developer (already in another guild — not auto-joined).";

            await memberships.CreateAsync(GuildMembership.Create(devGuild.Id, player.Id, GuildRank.Member));
            player.JoinGuild(devGuild.Id, GuildRank.Member);
            await players.UpdateAsync(player);
            devGuild.IncrementMemberCount();
            await guilds.UpdateAsync(devGuild);
            await audit.AppendAsync(AuditLog.Create(
                player.Id, "DevGuildJoined", null,
                $"Developer '{player.Username}' auto-joined dev guild '{devGuild.Name}' via CLI", null));
            return $"flagged '{player.Username}' as Developer and joined dev guild '{devGuild.Name}'.";
        }

        // grant=false → remove from the dev guild (if a member) then revoke the flag.
        var membership = await memberships.FindByPlayerAsync(player.Id);
        if (membership is not null)
        {
            var devGuild = await guilds.FindByTagAsync(guildConfig.DevGuildTag);
            if (devGuild is not null && membership.GuildId == devGuild.Id)
            {
                membership.SoftDelete();
                await memberships.UpdateAsync(membership);
                player.LeaveGuild();
                await players.UpdateAsync(player);
                devGuild.DecrementMemberCount();
                await guilds.UpdateAsync(devGuild);
            }
        }

        if (player.HasRole(PlayerRoles.Developer))
        {
            player.RevokeRole(PlayerRoles.Developer);
            await players.UpdateAsync(player);
            await audit.AppendAsync(AuditLog.Create(
                player.Id, "DevFlagRevoked", null,
                $"Developer flag revoked via CLI from '{player.Username}'", null));
        }
        logger.LogInformation("Seed: Developer flag revoked from '{Username}'.", player.Username);
        return $"unflagged '{player.Username}' (Developer flag revoked, removed from dev guild).";
    }
}
