using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

//        per-IP and per-player throttling. Service enforces business rules only.
public sealed class AuthService : IAuthService
{
    private const int BcryptWorkFactor = 12;
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);
    private const int MaxConcurrentSessions = 3;

    // SECURITY: timing-attack defense — always run BCrypt even when email not found.
    private static readonly string DummyPasswordHash =
        BCrypt.Net.BCrypt.HashPassword("timing-safe-dummy", BcryptWorkFactor);

    private readonly IPlayerRepository _players;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IConfiguration _config;
    private readonly IAuthLockoutService _lockout;
    private readonly IAuditLogRepository _auditLog;
    private readonly IBetaKeyRepository _betaKeys;

    public AuthService(
        IPlayerRepository players,
        IRefreshTokenRepository refreshTokens,
        IConfiguration config,
        IAuthLockoutService lockout,
        IAuditLogRepository auditLog,
        IBetaKeyRepository betaKeys)
    {
        _players = players;
        _refreshTokens = refreshTokens;
        _config = config;
        _lockout = lockout;
        _auditLog = auditLog;
        _betaKeys = betaKeys;
    }

    // -------------------------------------------------------------------
    // REGISTER
    // -------------------------------------------------------------------

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request, string ipAddress)
    {
        var betaGateEnabled = _config.GetValue("BetaGate:Enabled", true);

        if (betaGateEnabled)
            return await RegisterWithBetaGateAsync(request, ipAddress);

        return await RegisterCoreAsync(request, ipAddress, newPlayerId: null);
    }

    private async Task<AuthResponse?> RegisterWithBetaGateAsync(RegisterRequest request, string ipAddress)
    {
        // Pre-allocate the player ID so TryRedeemAsync can link the key to the player
        // before the player row exists. The DB transaction rolls back both if creation fails.
        var newPlayerId = Guid.NewGuid();

        return await _betaKeys.WithTransactionAsync(async ct =>
        {
            // Step 1: atomically claim the key. This is the single-use race guard.
            var claimed = await _betaKeys.TryRedeemAsync(request.BetaKey, newPlayerId, ct);
            if (!claimed)
            {
                await _auditLog.AppendAsync(AuditLog.Create(
                    null, "RegisterFailed", null,
                    "Invalid or already-redeemed beta key", ipAddress));
                return null;
            }

            // Step 2: create the player only after the key is secured.
            return await RegisterCoreAsync(request, ipAddress, newPlayerId, ct);
        });
    }

    private async Task<AuthResponse?> RegisterCoreAsync(
        RegisterRequest request, string ipAddress, Guid? newPlayerId,
        CancellationToken ct = default)
    {
        if (await _players.EmailExistsAsync(request.Email, ct) ||
            await _players.UsernameExistsAsync(request.Username, ct))
        {
            await _auditLog.AppendAsync(AuditLog.Create(
                null, "RegisterFailed", null,
                "Duplicate username or email", ipAddress));
            return null;
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BcryptWorkFactor);
        var player = newPlayerId.HasValue
            ? Player.CreateWithId(newPlayerId.Value, request.Username, request.Email, passwordHash)
            : Player.Create(request.Username, request.Email, passwordHash);
        await _players.CreateAsync(player, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            player.Id, "Register", null,
            "Player registered", ipAddress));

        return await IssueTokenPairAsync(player, ipAddress);
    }

    // -------------------------------------------------------------------
    // LOGIN
    // -------------------------------------------------------------------

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, string ipAddress)
    {
        // Check lockout BEFORE touching the DB — cheap Redis check first.
        if (await _lockout.IsLockedOutAsync(request.Email))
        {
            await _auditLog.AppendAsync(AuditLog.Create(
                null, "LoginFailed", null,
                "Account locked", ipAddress));
            return null;
        }

        var player = await _players.FindByEmailAsync(request.Email);

        // SECURITY: always run BCrypt.Verify even on null player to prevent timing attacks.
        var hashToCheck = player?.PasswordHash ?? DummyPasswordHash;
        var passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, hashToCheck);

        if (player is null || !passwordValid)
        {
            await _lockout.RecordFailedAttemptAsync(request.Email);
            await _auditLog.AppendAsync(AuditLog.Create(
                player?.Id, "LoginFailed", null,
                "Invalid credentials", ipAddress));
            return null;
        }

        if (player.IsBanned)
        {
            await _auditLog.AppendAsync(AuditLog.Create(
                player.Id, "LoginFailed", null,
                "Account banned", ipAddress));
            return null;
        }

        await _lockout.ClearAsync(request.Email);

        await _auditLog.AppendAsync(AuditLog.Create(
            player.Id, "Login", null,
            "Login successful", ipAddress));

        return await IssueTokenPairAsync(player, ipAddress);
    }

    // -------------------------------------------------------------------
    // REFRESH
    // -------------------------------------------------------------------

    public async Task<AuthResponse?> RefreshAsync(RefreshRequest request, string ipAddress)
    {
        var tokenHash = HashToken(request.RefreshToken);
        var stored = await _refreshTokens.FindByTokenHashAsync(tokenHash);

        if (stored is null || !stored.IsActive)
        {
            await _auditLog.AppendAsync(AuditLog.Create(
                null, "TokenRefreshFailed", null,
                "Invalid or expired token", ipAddress));
            return null;
        }

        var player = await _players.FindByIdAsync(stored.PlayerId);
        if (player is null || player.IsBanned)
        {
            await _auditLog.AppendAsync(AuditLog.Create(
                stored.PlayerId, "TokenRefreshFailed", null,
                "Player not found or banned", ipAddress));
            return null;
        }

        await _refreshTokens.RevokeAsync(stored);

        await _auditLog.AppendAsync(AuditLog.Create(
            player.Id, "TokenRefresh", null,
            "Token rotated", ipAddress));

        return await IssueTokenPairAsync(player, ipAddress);
    }

    // -------------------------------------------------------------------
    // LOGOUT
    // -------------------------------------------------------------------

    public async Task LogoutAsync(RefreshRequest request)
    {
        var tokenHash = HashToken(request.RefreshToken);
        var stored = await _refreshTokens.FindByTokenHashAsync(tokenHash);

        if (stored is null)
            return;

        await _refreshTokens.RevokeAsync(stored);

        await _auditLog.AppendAsync(AuditLog.Create(
            stored.PlayerId, "Logout", null,
            "Session ended", null));
    }

    // -------------------------------------------------------------------
    // PRIVATE HELPERS
    // -------------------------------------------------------------------

    private async Task<AuthResponse> IssueTokenPairAsync(Player player, string ipAddress)
    {
        var activeSessions = await _refreshTokens.CountActiveSessionsAsync(player.Id);
        if (activeSessions >= MaxConcurrentSessions)
        {
            var oldest = await _refreshTokens.FindOldestActiveAsync(player.Id);
            if (oldest is not null)
                await _refreshTokens.RevokeAsync(oldest);
        }

        var rawToken = GenerateSecureToken();
        var tokenHash = HashToken(rawToken);
        var expiresAt = DateTimeOffset.UtcNow.Add(RefreshTokenLifetime);

        var refreshToken = new RefreshToken(player.Id, tokenHash, expiresAt, ipAddress);
        await _refreshTokens.CreateAsync(refreshToken);

        var accessTokenExpiry = DateTimeOffset.UtcNow.Add(AccessTokenLifetime);
        var accessToken = GenerateAccessToken(player, accessTokenExpiry);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = rawToken,
            AccessTokenExpiry = accessTokenExpiry,
        };
    }

    private string GenerateAccessToken(Player player, DateTimeOffset expiry)
    {
        var privateKeyPem = _config["Jwt:PrivateKey"]
            ?? throw new InvalidOperationException("Jwt:PrivateKey is not configured.");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var credentials = new SigningCredentials(
            new RsaSecurityKey(rsa),
            SecurityAlgorithms.RsaSha256);

        // Build base claims
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub,   player.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Name,  player.Username),
            new Claim(JwtRegisteredClaimNames.Email, player.Email),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new Claim("display_name",                player.DisplayName),
        };

        // Emit one role claim per set flag (skip None)
        foreach (PlayerRoles flag in Enum.GetValues<PlayerRoles>())
        {
            if (flag != PlayerRoles.None && player.HasRole(flag))
                claims.Add(new Claim(ClaimTypes.Role, flag.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            notBefore: DateTimeOffset.UtcNow.UtcDateTime,
            expires: expiry.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncoder.Encode(bytes);
    }

    private static string HashToken(string rawToken)
    {
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
