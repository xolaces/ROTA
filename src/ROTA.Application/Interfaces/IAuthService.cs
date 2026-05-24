// Service contract for all authentication flows.
// Implemented by AuthService in ROTA.Application.Services.
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IAuthService
{
    /// <summary>Registers a new player. Returns null if username or email already taken.</summary>
    Task<AuthResponse?> RegisterAsync(RegisterRequest request, string ipAddress);

    /// <summary>Authenticates a player. Returns null if credentials invalid or account locked.</summary>
    Task<AuthResponse?> LoginAsync(LoginRequest request, string ipAddress);

    /// <summary>Rotates a refresh token. Returns null if token is invalid, expired, or revoked.</summary>
    Task<AuthResponse?> RefreshAsync(RefreshRequest request, string ipAddress);

    /// <summary>Revokes a refresh token, ending the session.</summary>
    Task LogoutAsync(RefreshRequest request);
}