using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA - Full implementation. Rate limiting and audit log middleware handle
//        per-IP and per-player throttling. Service enforces business rules only.
/// <summary>
/// Handles all authentication flows: register, login, refresh, logout.
/// SECURITY design decisions:
///   - Passwords hashed with BCrypt (work factor 12).
///   - Refresh tokens are 256-bit random values - SHA-256 hash stored in DB.
///   - Access tokens: RS256, 15-minute lifetime, zero clock skew.
///   - Refresh tokens: 7-day lifetime, immediately rotated on use.
///   - Max 3 concurrent sessions per player. 4th login evicts oldest.
///   - Timing-safe: failed login returns null with identical latency to success.
/// </summary>
public sealed class AuthService : IAuthService
{
    // SECURITY: work factor 12 = ~250ms on modern hardware. Slows brute-force significantly.
    private const int BcryptWorkFactor = 12;

    // Access token lifetime: short enough to limit damage if stolen.
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    // Refresh token lifetime: 7 days. Rotation means each use issues a new one.
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    // SECURITY: max concurrent sessions. 4th login evicts oldest.
    private const int MaxConcurrentSessions = 3;

    // SECURITY: timing-attack defense for login. When the email doesn't exist we still
    // run BCrypt.Verify against THIS valid hash so the response latency matches a real
    // (failed) verification. Computed once at type-init - a malformed literal would make
    // BCrypt.Verify throw SaltParseException instead of returning false.
    private static readonly string DummyPasswordHash =
        BCrypt.Net.BCrypt.HashPassword("timing-safe-dummy", BcryptWorkFactor);

    private readonly IPlayerRepository _players;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IConfiguration _config;

    public AuthService(
        IPlayerRepository players,
        IRefreshTokenRepository refreshTokens,
        IConfiguration config)
    {
        _players = players;
        _refreshTokens = refreshTokens;
        _config = config;
    }

    // -------------------------------------------------------------------
    // REGISTER
    // -------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request, string ipAddress)
    {
        // EXPLOIT GUARD: duplicate email or username -> silent null (not 409)
        // 409 leaks whether an email exists - enumeration attack vector
        if (await _players.EmailExistsAsync(request.Email))
            return null;

        if (await _players.UsernameExistsAsync(request.Username))
            return null;

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BcryptWorkFactor);

        var player = Player.Create(request.Username, request.Email, passwordHash);
        await _players.CreateAsync(player);

        return await IssueTokenPairAsync(player, ipAddress);
    }

    // -------------------------------------------------------------------
    // LOGIN
    // -------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<AuthResponse?> LoginAsync(LoginRequest request, string ipAddress)
    {
        var player = await _players.FindByEmailAsync(request.Email);

        // SECURITY: always run BCrypt.Verify even on null player to prevent
        // timing-based user-enumeration attacks. DummyPasswordHash is a valid hash.
        var hashToCheck = player?.PasswordHash ?? DummyPasswordHash;
        var passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, hashToCheck);

        if (player is null || !passwordValid)
            return null;   // identical timing path for both failure cases

        if (player.IsBanned)
            return null;   // banned players silently fail - no hint given

        return await IssueTokenPairAsync(player, ipAddress);
    }

    // -------------------------------------------------------------------
    // REFRESH
    // -------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<AuthResponse?> RefreshAsync(RefreshRequest request, string ipAddress)
    {
        var tokenHash = HashToken(request.RefreshToken);
        var stored = await _refreshTokens.FindByTokenHashAsync(tokenHash);

        // SECURITY: invalid, expired, or already-revoked tokens all return null.
        // No distinction given to caller - prevents token oracle attacks.
        if (stored is null || !stored.IsActive)
            return null;

        var player = await _players.FindByIdAsync(stored.PlayerId);
        if (player is null || player.IsBanned)
            return null;

        // Immediately revoke consumed token before issuing new pair (rotation)
        await _refreshTokens.RevokeAsync(stored);

        return await IssueTokenPairAsync(player, ipAddress);
    }

    // -------------------------------------------------------------------
    // LOGOUT
    // -------------------------------------------------------------------

    /// <inheritdoc />
    public async Task LogoutAsync(RefreshRequest request)
    {
        var tokenHash = HashToken(request.RefreshToken);
        var stored = await _refreshTokens.FindByTokenHashAsync(tokenHash);

        if (stored is null)
            return;   // idempotent - double-logout is not an error

        await _refreshTokens.RevokeAsync(stored);
    }

    // -------------------------------------------------------------------
    // PRIVATE HELPERS
    // -------------------------------------------------------------------

    /// <summary>
    /// Issues a new access token + refresh token pair for a player.
    /// Enforces the 3-session maximum: evicts oldest session if needed.
    /// </summary>
    private async Task<AuthResponse> IssueTokenPairAsync(Player player, string ipAddress)
    {
        // Session cap enforcement: evict oldest if at limit
        var activeSessions = await _refreshTokens.CountActiveSessionsAsync(player.Id);

        if (activeSessions >= MaxConcurrentSessions)
        {
            var oldest = await _refreshTokens.FindOldestActiveAsync(player.Id);

            if (oldest is not null)
                await _refreshTokens.RevokeAsync(oldest);
        }

        // Generate cryptographically random 256-bit refresh token
        var rawToken = GenerateSecureToken();
        var tokenHash = HashToken(rawToken);
        var expiresAt = DateTimeOffset.UtcNow.Add(RefreshTokenLifetime);

        var refreshToken = new RefreshToken(
            player.Id,
            tokenHash,
            expiresAt,
            ipAddress);

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


        /// <summary>
        /// Generates a signed RS256 JWT access token.
        /// Claims: sub (PlayerId), name (Username), jti (unique token ID).
        /// </summary>
        private string GenerateAccessToken(Player player, DateTimeOffset expiry)
    {
        var privateKeyPem = _config["Jwt:PrivateKey"]
            ?? throw new InvalidOperationException("Jwt:PrivateKey is not configured.");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var credentials = new SigningCredentials(
            new RsaSecurityKey(rsa),
            SecurityAlgorithms.RsaSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,  player.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Name, player.Username),
            new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            notBefore: DateTimeOffset.UtcNow.UtcDateTime,
            expires: expiry.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Generates a cryptographically random 256-bit token encoded as Base64Url.
    /// Used as the raw refresh token value - never stored, only its SHA-256 hash is.
    /// </summary>
    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32); // 256 bits
        return Base64UrlEncoder.Encode(bytes);
    }

    /// <summary>
    /// Returns the SHA-256 hash of a raw token as a lowercase hex string.
    /// SECURITY: we store the hash, never the raw token. Even if the DB is
    /// compromised, raw tokens cannot be reconstructed.
    /// </summary>
    private static string HashToken(string rawToken)
    {
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}