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
}