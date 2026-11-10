using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse?> RegisterAsync(RegisterRequest request, string ipAddress);

    Task<AuthResponse?> LoginAsync(LoginRequest request, string ipAddress);

    Task<AuthResponse?> RefreshAsync(RefreshRequest request, string ipAddress);

    Task LogoutAsync(RefreshRequest request);

    /// <summary>
    /// T65 step 1 — issues a single-use reset code and emails it to the player. ALWAYS completes
    /// silently (unknown email = audited no-op) so the endpoint can't be used for enumeration.
    /// </summary>
    Task RequestPasswordResetAsync(PasswordResetRequest request, string ipAddress);

    /// <summary>
    /// Owner tool (admin CLI <c>issue-reset-code</c>): issues a reset code for the account with this
    /// email or username without sending email, and returns it so the owner can pass it on. The
    /// player redeems it through the normal confirm step. Supersedes any outstanding code. Returns
    /// null for an unknown or banned account.
    /// </summary>
    Task<IssuedResetCode?> IssuePasswordResetCodeAsync(string emailOrUsername, TimeSpan ttl, string issuedBy);

    /// <summary>
    /// T65 step 2 — consumes the code, replaces the password, and revokes ALL active sessions.
    /// Returns false on unknown email / wrong / expired / already-used code.
    /// </summary>
    Task<bool> ResetPasswordAsync(ResetPasswordRequest request, string ipAddress);
}

/// <summary>A reset code issued by the owner, and the account it belongs to.</summary>
public sealed record IssuedResetCode(string Username, string Email, string Code, DateTimeOffset ExpiresAt);
