// Request and response shapes for the auth system.
// Consumed by AuthController and returned to the Unity client.
namespace ROTA.Shared.DTOs;

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    /// <summary>Beta access key in ROTA-XXXX-XXXX-XXXX format. Required when BetaGate:Enabled is true.</summary>
    public string BetaKey { get; set; } = string.Empty;

    /// <summary>T68 — must equal the server's current terms version (GET /api/legal/terms).</summary>
    public int AcceptedTermsVersion { get; set; }
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>T65 — step 1: request a reset code by email. Endpoint always answers 202 (anti-enumeration).</summary>
public class PasswordResetRequest
{
    public string Email { get; set; } = string.Empty;
}

/// <summary>T65 — step 2: redeem the emailed code with a new password.</summary>
public class ResetPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    /// <summary>The emailed code, XXXX-XXXX (case/dash-insensitive).</summary>
    public string Code { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset AccessTokenExpiry { get; set; }

    /// <summary>T68 — true when the terms version changed since this account last accepted; the
    /// client must show the terms gate and POST /api/legal/accept.</summary>
    public bool RequiresTermsAcceptance { get; set; }

    /// <summary>T68 — the server's current terms version (what /api/legal/accept expects).</summary>
    public int CurrentTermsVersion { get; set; }
}