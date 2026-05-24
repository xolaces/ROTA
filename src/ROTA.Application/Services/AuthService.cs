// Handles player registration, login, token refresh, and logout.
// All security guards are built in — not bolted on after.
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

public class AuthService : IAuthService
{
    private readonly RotaDbContext _db;
    private readonly IConfiguration _config;

    private int MaxSessions => _config.GetValue<int>("Auth:MaxConcurrentSessions", 3);
    private int AccessTokenMinutes => _config.GetValue<int>("Jwt:AccessTokenExpiryMinutes", 15);
    private int RefreshTokenDays => _config.GetValue<int>("Jwt:RefreshTokenExpiryDays", 7);

    public AuthService(RotaDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    /// <summary>Registers a new player. Returns null if username or email already taken.</summary>
    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request, string ipAddress)
    {
        var exists = await _db.Players.AnyAsync(p =>
            p.Username == request.Username || p.Email == request.Email);

        if (exists)
            return null;

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        var player = new Player(request.Username, request.Email, passwordHash);

        _db.Players.Add(player);
        await _db.SaveChangesAsync();

        return await IssueTokensAsync(player, ipAddress);
    }

    /// <summary>Authenticates a player. Returns null if credentials invalid or account banned.</summary>
    public async Task<AuthResponse?> LoginAsync(LoginRequest request, string ipAddress)
    {
        var player = await _db.Players
            .FirstOrDefaultAsync(p => p.Email == request.Email && !p.IsDeleted);

        if (player == null || player.IsBanned)
            return null;

        // SECURITY: generic failure — never reveal whether the email exists
        if (!BCrypt.Net.BCrypt.Verify(request.Password, player.PasswordHash))
            return null;

        return await IssueTokensAsync(player, ipAddress);
    }

    /// <summary>Rotates a refresh token. Returns null if invalid, expired, or revoked.</summary>
    public async Task<AuthResponse?> RefreshAsync(RefreshRequest request, string ipAddress)
    {
        var tokenHash = HashToken(request.RefreshToken);

        var stored = await _db.RefreshTokens
            .Include(t => t.Player)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (stored == null || stored.IsRevoked || stored.ExpiresAt < DateTimeOffset.UtcNow)
            return null;

        // SECURITY: revoke old token immediately before issuing new one
        stored.Revoke();
        await _db.SaveChangesAsync();

        return await IssueTokensAsync(stored.Player, ipAddress);
    }

    /// <summary>Revokes a refresh token, ending the session.</summary>
    public async Task LogoutAsync(RefreshRequest request)
    {
        var tokenHash = HashToken(request.RefreshToken);

        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (stored == null || stored.IsRevoked)
            return;

        stored.Revoke();
        await _db.SaveChangesAsync();
    }

    // Issues a new access + refresh token pair. Revokes oldest session if at limit.
    private async Task<AuthResponse> IssueTokensAsync(Player player, string ipAddress)
    {
        // SECURITY: enforce max concurrent sessions — revoke oldest if at limit
        var activeSessions = await _db.RefreshTokens
            .Where(t => t.PlayerId == player.Id && !t.IsRevoked && t.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();

        if (activeSessions.Count >= MaxSessions)
            activeSessions.First().Revoke();

        var rawRefreshToken = GenerateSecureToken();
        var refreshTokenHash = HashToken(rawRefreshToken);

        var refreshToken = new RefreshToken(
            player.Id,
            refreshTokenHash,
            DateTimeOffset.UtcNow.AddDays(RefreshTokenDays),
            ipAddress);

        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync();

        var accessTokenExpiry = DateTimeOffset.UtcNow.AddMinutes(AccessTokenMinutes);
        var accessToken = GenerateJwt(player, accessTokenExpiry);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = rawRefreshToken,
            AccessTokenExpiry = accessTokenExpiry
        };
    }

    // Generates a signed RS256 JWT for the given player.
    private string GenerateJwt(Player player, DateTimeOffset expiry)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(_config["Jwt:PrivateKey"]
            ?? throw new InvalidOperationException("Jwt:PrivateKey is not configured."));

        var credentials = new SigningCredentials(
            new RsaSecurityKey(rsa),
            SecurityAlgorithms.RsaSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   player.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, player.Email),
            new Claim("username",                    player.Username),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString())
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

    // Cryptographically secure random token string.
    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    // SHA256 hash of a token — raw tokens are never persisted.
    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}