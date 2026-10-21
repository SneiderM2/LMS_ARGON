using System.Security.Claims;

namespace LMS.API.Services;

/// <summary>
/// Contexto de identidad verificada del usuario autenticado en SciKids LMS.
/// Incluye banderas de cumplimiento para protección de menores (Ley 1098 de 2006 y Ley 1581 de 2012).
/// </summary>
public sealed record AuthenticatedUserContext(
    Guid UserId,
    Guid TenantId,
    string Username,
    string Role,
    string PseudonymCode,
    bool HasActiveParentalConsent
);

/// <summary>
/// Resultado de generación o rotación del par de tokens (Access Token de corta duración + Refresh Token).
/// </summary>
public sealed record TokenPairResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    string SessionFamilyId,
    string TokenJti
);

/// <summary>
/// Metadatos persistidos en el almacén distribuido (Redis / MemoryCache) para control de sesión y detección de reúso.
/// </summary>
public sealed record RefreshTokenSessionState(
    Guid UserId,
    Guid TenantId,
    string Username,
    string Role,
    string PseudonymCode,
    bool HasActiveParentalConsent,
    string TokenHash,
    string SessionFamilyId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool IsRevoked,
    string? RevokedReason = null
);

/// <summary>
/// Contrato de servicio para emisión, validación, rotación y revocación de tokens JWT y Refresh Tokens.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Genera un nuevo Access Token JWT de corta duración (15 min) y un Refresh Token criptográfico.
    /// Valida que los estudiantes menores de edad cuenten con consentimiento parental activo (Ley 1581 / Ley 1098).
    /// </summary>
    Task<TokenPairResult> GenerateTokenPairAsync(
        AuthenticatedUserContext userContext,
        string? existingSessionFamilyId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rota un Refresh Token aplicando política de un solo uso (One-Time-Use) y detección de robo de sesión (Token Reuse Detection).
    /// </summary>
    Task<TokenPairResult?> RotateRefreshTokenAsync(
        string rawRefreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoca una sesión específica a partir de su Refresh Token y su identificador JTI activo.
    /// </summary>
    Task RevokeSessionAsync(
        string? rawRefreshToken,
        string? activeAccessTokenJti = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoca inmediatamente todas las sesiones activas y familias de tokens de un usuario (ej: tras revocatoria de consentimiento parental).
    /// </summary>
    Task RevokeAllUserSessionsAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifica si un identificador único de token JWT (jti) o el usuario completo se encuentra en la lista de revocación.
    /// </summary>
    Task<bool> IsTokenRevokedAsync(
        string jti,
        Guid userId,
        string? sessionFamilyId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida criptográficamente un Access Token (incluso si acaba de expirar) para extraer sus Claims de forma segura.
    /// </summary>
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string accessToken);
}
