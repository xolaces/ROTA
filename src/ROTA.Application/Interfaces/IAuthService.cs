// Service contract for all authentication flows.
// Implemented by AuthService in ROTA.Application.Services.
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
    /// T65 step 2 — consumes the code, replaces the password, and revokes ALL active sessions.
    /// Returns false on unknown email / wrong / expired / already-used code.
    /// </summary>
    Task<bool> ResetPasswordAsync(ResetPasswordRequest request, string ipAddress);
}