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
    private readonly IAchievementService _achievements;
    private readonly IPasswordResetTokenRepository _resetTokens;
    private readonly IEmailNotificationService _emails;

    public AuthService(
        IPlayerRepository players,
        IRefreshTokenRepository refreshTokens,
        IConfiguration config,
        IAuthLockoutService lockout,
        IAuditLogRepository auditLog,
        IBetaKeyRepository betaKeys,
        IAchievementService achievements,
        IPasswordResetTokenRepository resetTokens,
        IEmailNotificationService emails)
    {
        _players = players;
        _refreshTokens = refreshTokens;
        _config = config;
        _lockout = lockout;
        _auditLog = auditLog;
        _betaKeys = betaKeys;
        _achievements = achievements;
        _resetTokens = resetTokens;
        _emails = emails;
    }

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request, string ipAddress)
    {
        var betaGateEnabled = _config.GetValue("BetaGate:Enabled", true);

        if (betaGateEnabled)
            return await RegisterWithBetaGateAsync(request, ipAddress);

        return await RegisterCoreAsync(request, ipAddress, newPlayerId: null);
    }

    private async Task<AuthResponse?> RegisterWithBetaGateAsync(RegisterRequest request, string ipAddress)
    {
        // SECURITY/CORRECTNESS: reject duplicates BEFORE consuming the single-use key, so a
        // taken username/email never burns a valid key. Zero side effects on this path.
        if (await _players.EmailExistsAsync(request.Email) ||
            await _players.UsernameExistsAsync(request.Username))
        {
            await _auditLog.AppendAsync(AuditLog.Create(
                null, "RegisterFailed", null,
                "Duplicate username or email", ipAddress));
            return null;
        }

        // Pre-allocate the player ID so TryRedeemAsync can link the key to the player
        // before the player row exists. If player creation throws (e.g. a unique-constraint
        // race), the DB transaction rolls back the key claim, freeing the key.
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
        // T68 — the validator already required the CURRENT version; stamp it on the new account.
        player.AcceptTerms(request.AcceptedTermsVersion);
        await _players.CreateAsync(player, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            player.Id, "Register", null,
            "Player registered", ipAddress));

        return await IssueTokenPairAsync(player, ipAddress);
    }

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
            // The audit line carries the expiry so an operator fielding "why can I not log in" can
            // answer it from the log alone.
            //
            // KNOWN GAP: the PLAYER is still told nothing - LoginAsync signals refusal by returning
            // null, so every failure looks alike. That is right for bad credentials (anti-enumeration)
            // but wrong for a ban, where the caller has already proven who they are and a temporary ban
            // has an end date worth showing. Fixing it means changing this method's contract, so it is
            // deliberately out of scope here.
            await _auditLog.AppendAsync(AuditLog.Create(
                player.Id, "LoginFailed", null,
                player.BannedUntil is null
                    ? "Account banned (permanent)"
                    : $"Account banned until {player.BannedUntil.Value:O}",
                ipAddress));
            return null;
        }

        await _lockout.ClearAsync(request.Email);

        await _auditLog.AppendAsync(AuditLog.Create(
            player.Id, "Login", null,
            "Login successful", ipAddress));

        // Achievements (TICKET 46) — days-played login hook. Increment DaysPlayed only when the UTC
        // calendar day advances (RecordLogin is idempotent per day), then record the metric with a
        // per-day referenceId so a re-login never double-counts. Best-effort: NEVER blocks login.
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (player.RecordLogin(today))
            {
                await _players.UpdateAsync(player);
                await _achievements.RecordProgressAsync(
                    player.Id, Domain.Enums.AchievementMetric.DaysPlayed, 1,
                    $"ach:day:{player.Id}:{today:yyyy-MM-dd}");
                await _achievements.EvaluateCompletionsAsync(player.Id);
            }
        }
        catch
        {
            // swallow — achievement tracking must never break authentication
        }

        return await IssueTokenPairAsync(player, ipAddress);
    }

    public async Task<AuthResponse?> RefreshAsync(RefreshRequest request, string ipAddress)
    {
        var tokenHash = HashToken(request.RefreshToken);
        var stored = await _refreshTokens.FindByTokenHashAsync(tokenHash);

        if (stored is null)
        {
            await _auditLog.AppendAsync(AuditLog.Create(
                null, "TokenRefreshFailed", null,
                "Invalid or expired token", ipAddress));
            return null;
        }

        // Replay of an already-rotated token is the canonical theft signal: the legitimate client
        // holds the NEW token, so whoever presents the old one (victim or thief — we can't tell
        // which side is which) means the family is compromised. Revoke every active session so the
        // thief is evicted; the victim re-authenticates with their password.
        if (stored.IsRevoked)
        {
            await _refreshTokens.RevokeAllActiveAsync(stored.PlayerId);
            await _auditLog.AppendAsync(AuditLog.Create(
                stored.PlayerId, "TokenReplayDetected", null,
                "Rotated refresh token was replayed — all sessions revoked", ipAddress));
            return null;
        }

        if (!stored.IsActive) // not revoked, so: expired — ordinary failure, no breach response
        {
            await _auditLog.AppendAsync(AuditLog.Create(
                stored.PlayerId, "TokenRefreshFailed", null,
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

        // Atomic rotation: a single conditional UPDATE claims the token. Losing the race means a
        // concurrent request rotated it between our read and now — same semantics as a replay.
        if (!await _refreshTokens.TryRevokeAsync(tokenHash))
        {
            await _refreshTokens.RevokeAllActiveAsync(stored.PlayerId);
            await _auditLog.AppendAsync(AuditLog.Create(
                stored.PlayerId, "TokenReplayDetected", null,
                "Concurrent rotation of the same refresh token — all sessions revoked", ipAddress));
            return null;
        }

        await _auditLog.AppendAsync(AuditLog.Create(
            player.Id, "TokenRefresh", null,
            "Token rotated", ipAddress));

        return await IssueTokenPairAsync(player, ipAddress);
    }

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

    // PASSWORD RESET (T65)

    // Crockford-style base32 (no 0/1/I/L/O/U) — same alphabet as beta keys.
    private const string ResetCodeAlphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    public async Task RequestPasswordResetAsync(PasswordResetRequest request, string ipAddress)
    {
        var player = await _players.FindByEmailAsync(request.Email);

        // SECURITY: unknown / banned accounts get the same silent outcome as success — the endpoint
        // always 202s, so the only observable difference is whether an email arrives.
        if (player is null || player.IsBanned)
        {
            await _auditLog.AppendAsync(AuditLog.Create(
                player?.Id, "PasswordResetRequested", null,
                player is null ? "Unknown email — no code issued" : "Banned account — no code issued",
                ipAddress));
            return;
        }

        // One live code at a time: a new request invalidates any outstanding unused code.
        await _resetTokens.InvalidateActiveAsync(player.Id);

        var ttlMinutes = _config.GetValue("Auth:PasswordResetTokenMinutes", 15);
        var code = GenerateResetCode();
        await _resetTokens.CreateAsync(PasswordResetToken.Create(
            player.Id, HashToken(NormalizeResetCode(code)), TimeSpan.FromMinutes(ttlMinutes)));

        await _auditLog.AppendAsync(AuditLog.Create(
            player.Id, "PasswordResetRequested", null,
            $"Reset code issued (ttl={ttlMinutes}m)", ipAddress));

        // Rides the T39 backbone with RecipientOverride — delivered to the PLAYER, not the operator.
        await _emails.QueueAsync(new Models.EmailPayload
        {
            Type = EmailType.PasswordReset,
            Subject = "Your ROTA password reset code",
            Summary = $"Password reset code issued for {player.Username}",
            TriggeringPlayerId = player.Id,
            TriggeringSystem = "T65",
            RecipientOverride = player.Email,
            Detail = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["expiresMinutes"] = ttlMinutes,
            },
        }, ipAddress);
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordRequest request, string ipAddress)
    {
        var player = await _players.FindByEmailAsync(request.Email);
        if (player is null || player.IsBanned)
        {
            await _auditLog.AppendAsync(AuditLog.Create(
                player?.Id, "PasswordResetFailed", null,
                player is null ? "Unknown email" : "Account banned", ipAddress));
            return false;
        }

        // Atomic single-use consume — wrong, expired, superseded, and replayed codes all land here.
        var codeHash = HashToken(NormalizeResetCode(request.Code));
        if (!await _resetTokens.TryConsumeAsync(player.Id, codeHash))
        {
            await _auditLog.AppendAsync(AuditLog.Create(
                player.Id, "PasswordResetFailed", null,
                "Invalid, expired, or already-used reset code", ipAddress));
            return false;
        }

        player.SetPasswordHash(BCrypt.Net.BCrypt.HashPassword(request.NewPassword, BcryptWorkFactor));
        await _players.UpdateAsync(player);

        // SECURITY: a reset is a credential-compromise recovery path — evict every session.
        await _refreshTokens.RevokeAllActiveAsync(player.Id);

        await _auditLog.AppendAsync(AuditLog.Create(
            player.Id, "PasswordReset", null,
            "Password reset — all sessions revoked", ipAddress));

        return true;
    }

    /// <summary>8 chars in XXXX-XXXX form. ~49 bits of entropy at a 15-min single-use TTL.</summary>
    private static string GenerateResetCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        var chars = new char[8];
        for (int i = 0; i < 8; i++)
            chars[i] = ResetCodeAlphabet[bytes[i] % ResetCodeAlphabet.Length];
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
    }

    /// <summary>Uppercases and strips separators/whitespace so player typos in form don't reject.</summary>
    private static string NormalizeResetCode(string code)
        => new(code.Trim().ToUpperInvariant().Where(c => c != '-' && c != ' ').ToArray());

    private async Task<AuthResponse> IssueTokenPairAsync(Player player, string ipAddress)
    {
        // Trim to the cap, not by one: if races (or historical drift) ever push the count past the
        // cap, a single-revoke would leave it permanently exceeded — every login would trim one and
        // add one. Looping restores the invariant on the next issue regardless of how it drifted.
        var activeSessions = await _refreshTokens.CountActiveSessionsAsync(player.Id);
        while (activeSessions >= MaxConcurrentSessions)
        {
            var oldest = await _refreshTokens.FindOldestActiveAsync(player.Id);
            if (oldest is null) break;
            await _refreshTokens.RevokeAsync(oldest);
            activeSessions--;
        }

        var rawToken = GenerateSecureToken();
        var tokenHash = HashToken(rawToken);
        var expiresAt = DateTimeOffset.UtcNow.Add(RefreshTokenLifetime);

        var refreshToken = new RefreshToken(player.Id, tokenHash, expiresAt, ipAddress);
        await _refreshTokens.CreateAsync(refreshToken);

        var accessTokenExpiry = DateTimeOffset.UtcNow.Add(AccessTokenLifetime);
        var accessToken = GenerateAccessToken(player, accessTokenExpiry);

        // T68 — flag stale terms acceptance on every token issue (login/refresh/register).
        var currentTermsVersion = _config.GetValue("Legal:CurrentTermsVersion", 1);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = rawToken,
            AccessTokenExpiry = accessTokenExpiry,
            RequiresTermsAcceptance = player.AcceptedTermsVersion < currentTermsVersion,
            CurrentTermsVersion = currentTermsVersion,
        };
    }

    // SECURITY/CORRECTNESS: signing credentials are created once per private key and reused for
    // the process lifetime. Microsoft.IdentityModel caches signature providers that hold a
    // reference to the RSA key, so the key must NOT be disposed per call — a previous
    // `using var rsa = RSA.Create()` disposed it at method exit, and the cached provider then hit
    // the disposed key on the 2nd+ token signed, throwing ObjectDisposedException (intermittent
    // 500 on login/register/refresh). Keyed by PEM so unit tests with per-test keys stay isolated.
    // Mirrors the long-lived public verification key built once in Program.cs.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SigningCredentials>
        SigningCredentialsByPem = new();

    private SigningCredentials GetSigningCredentials()
    {
        // PEM may arrive single-line with literal "\n" (a Docker .env can't hold real newlines) — normalize.
        var privateKeyPem = Configuration.PemKey.Normalize(_config["Jwt:PrivateKey"]);

        return SigningCredentialsByPem.GetOrAdd(privateKeyPem, static pem =>
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);  // intentionally not disposed — long-lived signing key
            return new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);
        });
    }

    private string GenerateAccessToken(Player player, DateTimeOffset expiry)
    {
        var credentials = GetSigningCredentials();

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
