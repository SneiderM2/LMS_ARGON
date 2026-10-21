using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LMS.API.Services;

/// <summary>
/// Opciones de configuración para el motor de identidad JWT y sesiones deslizantes.
/// </summary>
public sealed class JwtSecurityOptions
{
    public const string SectionName = "JwtSecurity";

    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required string SecretKey { get; init; } // Mínimo 32 bytes (256 bits)
    public int AccessTokenLifetimeMinutes { get; init; } = 15;
    public int RefreshTokenLifetimeDays { get; init; } = 7;
    public int SlidingWindowHours { get; init; } = 12;
}

/// <summary>
/// Implementación Enterprise de gestión de tokens JWT (OAuth2 / OIDC), rotación de Refresh Tokens
/// con detección de reúso y lista de revocación distribuida en tiempo real.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtSecurityOptions _options;
    private readonly IDistributedCache _cache;
    private readonly ILogger<JwtTokenService> _logger;
    private readonly SymmetricSecurityKey _signingKey;

    public JwtTokenService(
        IOptions<JwtSecurityOptions> options,
        IDistributedCache cache,
        ILogger<JwtTokenService> logger)
    {
        _options = options.Value;
        _cache = cache;
        _logger = logger;

        byte[] keyBytes = Encoding.UTF8.GetBytes(_options.SecretKey);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "La clave de firma JWT (JwtSecurity:SecretKey) debe poseer al menos 256 bits (32 bytes).");
        }

        _signingKey = new SymmetricSecurityKey(keyBytes);
    }

    public async Task<TokenPairResult> GenerateTokenPairAsync(
        AuthenticatedUserContext userContext,
        string? existingSessionFamilyId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userContext);

        // Bloqueo normativo Ley 1098 de 2006 (Arts. 44/47) y Ley 1581 de 2012 (Art. 12):
        // Ningún estudiante menor de edad puede obtener tokens activos sin consentimiento parental verificado.
        bool isMinorStudentRole =
            userContext.Role.Equals("ALUMNO", StringComparison.OrdinalIgnoreCase) ||
            userContext.Role.Equals("ESTUDIANTE", StringComparison.OrdinalIgnoreCase);

        if (isMinorStudentRole && !userContext.HasActiveParentalConsent)
        {
            _logger.LogWarning(
                "Emisión de token denegada para el estudiante {UserId}: No cuenta con consentimiento parental activo.",
                userContext.UserId);

            throw new UnauthorizedAccessException(
                "Bloqueo Legal Ley 1581 / Ley 1098: El estudiante menor de edad requiere autorización expresa y vigente de su acudiente legal.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset accessExpiresAt = now.AddMinutes(_options.AccessTokenLifetimeMinutes);
        DateTimeOffset refreshExpiresAt = now.AddDays(_options.RefreshTokenLifetimeDays);

        string jti = Guid.NewGuid().ToString("N");
        string sessionFamilyId = existingSessionFamilyId ?? Guid.NewGuid().ToString("N");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userContext.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtRegisteredClaimNames.UniqueName, userContext.Username),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(ClaimTypes.NameIdentifier, userContext.UserId.ToString()),
            new(ClaimTypes.Role, userContext.Role),
            new("tenant_id", userContext.TenantId.ToString()),
            new("pseudonym", userContext.PseudonymCode),
            new("consent_verified", userContext.HasActiveParentalConsent.ToString().ToLowerInvariant()),
            new("sid", sessionFamilyId)
        };

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var securityToken = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: accessExpiresAt.UtcDateTime,
            signingCredentials: credentials);

        string accessToken = new JwtSecurityTokenHandler().WriteToken(securityToken);

        // Generación de Refresh Token de alta entropía (512 bits / 64 bytes)
        string rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        string refreshTokenHash = ComputeSha256Hex(rawRefreshToken);

        var sessionState = new RefreshTokenSessionState(
            UserId: userContext.UserId,
            TenantId: userContext.TenantId,
            Username: userContext.Username,
            Role: userContext.Role,
            PseudonymCode: userContext.PseudonymCode,
            HasActiveParentalConsent: userContext.HasActiveParentalConsent,
            TokenHash: refreshTokenHash,
            SessionFamilyId: sessionFamilyId,
            CreatedAtUtc: now,
            ExpiresAtUtc: refreshExpiresAt,
            IsRevoked: false);

        var cacheEntryOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = refreshExpiresAt,
            SlidingExpiration = TimeSpan.FromHours(_options.SlidingWindowHours)
        };

        string serializedState = JsonSerializer.Serialize(sessionState);

        await _cache.SetStringAsync(
            GetRefreshTokenCacheKey(refreshTokenHash),
            serializedState,
            cacheEntryOptions,
            cancellationToken);

        await _cache.SetStringAsync(
            GetSessionFamilyCacheKey(sessionFamilyId),
            refreshTokenHash,
            cacheEntryOptions,
            cancellationToken);

        return new TokenPairResult(
            AccessToken: accessToken,
            AccessTokenExpiresAtUtc: accessExpiresAt,
            RefreshToken: rawRefreshToken,
            RefreshTokenExpiresAtUtc: refreshExpiresAt,
            SessionFamilyId: sessionFamilyId,
            TokenJti: jti);
    }

    public async Task<TokenPairResult?> RotateRefreshTokenAsync(
        string rawRefreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return null;
        }

        string tokenHash = ComputeSha256Hex(rawRefreshToken);
        string? cachedPayload = await _cache.GetStringAsync(GetRefreshTokenCacheKey(tokenHash), cancellationToken);

        if (string.IsNullOrWhiteSpace(cachedPayload))
        {
            _logger.LogWarning("Intento de rotación con Refresh Token no encontrado o expirado.");
            return null;
        }

        RefreshTokenSessionState? sessionState = JsonSerializer.Deserialize<RefreshTokenSessionState>(cachedPayload);
        if (sessionState is null)
        {
            return null;
        }

        // Verificar si el usuario completo o la familia de sesión fueron revocados
        bool isUserOrFamilyRevoked = await IsTokenRevokedAsync(
            jti: string.Empty,
            userId: sessionState.UserId,
            sessionFamilyId: sessionState.SessionFamilyId,
            cancellationToken: cancellationToken);

        // Detección de Reúso de Refresh Token (Compromiso / Replay Attack)
        if (sessionState.IsRevoked || isUserOrFamilyRevoked || sessionState.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _logger.LogCritical(
                "ALERTA DE SEGURIDAD: Reúso o intento con Refresh Token revocado para el usuario {UserId} (Familia: {FamilyId}). Invalidando todas las sesiones.",
                sessionState.UserId,
                sessionState.SessionFamilyId);

            await RevokeAllUserSessionsAsync(
                sessionState.UserId,
                "REFRESH_TOKEN_REUSE_DETECTED",
                cancellationToken);

            return null;
        }

        // Invalidar el Refresh Token actual para garantizar un solo uso (One-Time-Use)
        RefreshTokenSessionState usedState = sessionState with
        {
            IsRevoked = true,
            RevokedReason = "ROTATED"
        };

        await _cache.SetStringAsync(
            GetRefreshTokenCacheKey(tokenHash),
            JsonSerializer.Serialize(usedState),
            new DistributedCacheEntryOptions { AbsoluteExpiration = sessionState.ExpiresAtUtc },
            cancellationToken);

        var userContext = new AuthenticatedUserContext(
            UserId: sessionState.UserId,
            TenantId: sessionState.TenantId,
            Username: sessionState.Username,
            Role: sessionState.Role,
            PseudonymCode: sessionState.PseudonymCode,
            HasActiveParentalConsent: sessionState.HasActiveParentalConsent);

        return await GenerateTokenPairAsync(userContext, sessionState.SessionFamilyId, cancellationToken);
    }

    public async Task RevokeSessionAsync(
        string? rawRefreshToken,
        string? activeAccessTokenJti = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(activeAccessTokenJti))
        {
            await _cache.SetStringAsync(
                GetRevokedJtiCacheKey(activeAccessTokenJti),
                "REVOKED",
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.AccessTokenLifetimeMinutes + 5)
                },
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            string tokenHash = ComputeSha256Hex(rawRefreshToken);
            string? cachedPayload = await _cache.GetStringAsync(GetRefreshTokenCacheKey(tokenHash), cancellationToken);

            if (!string.IsNullOrWhiteSpace(cachedPayload))
            {
                RefreshTokenSessionState? state = JsonSerializer.Deserialize<RefreshTokenSessionState>(cachedPayload);
                if (state is not null)
                {
                    await _cache.SetStringAsync(
                        GetRevokedFamilyCacheKey(state.SessionFamilyId),
                        "LOGOUT",
                        new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(_options.RefreshTokenLifetimeDays)
                        },
                        cancellationToken);
                }
            }

            await _cache.RemoveAsync(GetRefreshTokenCacheKey(tokenHash), cancellationToken);
        }
    }

    public async Task RevokeAllUserSessionsAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Revocando globalmente todas las sesiones del usuario {UserId}. Motivo: {Reason}",
            userId,
            reason);

        await _cache.SetStringAsync(
            GetRevokedUserCacheKey(userId),
            $"{reason}|{DateTimeOffset.UtcNow:O}",
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(_options.RefreshTokenLifetimeDays)
            },
            cancellationToken);
    }

    public async Task<bool> IsTokenRevokedAsync(
        string jti,
        Guid userId,
        string? sessionFamilyId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(jti))
        {
            string? revokedJti = await _cache.GetStringAsync(GetRevokedJtiCacheKey(jti), cancellationToken);
            if (!string.IsNullOrEmpty(revokedJti))
            {
                return true;
            }
        }

        if (userId != Guid.Empty)
        {
            string? revokedUser = await _cache.GetStringAsync(GetRevokedUserCacheKey(userId), cancellationToken);
            if (!string.IsNullOrEmpty(revokedUser))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(sessionFamilyId))
        {
            string? revokedFamily = await _cache.GetStringAsync(GetRevokedFamilyCacheKey(sessionFamilyId), cancellationToken);
            if (!string.IsNullOrEmpty(revokedFamily))
            {
                return true;
            }
        }

        return false;
    }

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ValidateLifetime = false // Permitir inspeccionar claims de un token recién expirado durante el flujo de refresh
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        ClaimsPrincipal principal = tokenHandler.ValidateToken(
            accessToken,
            tokenValidationParameters,
            out SecurityToken securityToken);

        if (securityToken is not JwtSecurityToken jwtSecurityToken ||
            !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
        {
            throw new SecurityTokenException("Algoritmo de firma del token JWT inválido.");
        }

        return principal;
    }

    private static string ComputeSha256Hex(string rawValue)
    {
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawValue));
        return Convert.ToHexString(hashBytes);
    }

    private static string GetRefreshTokenCacheKey(string tokenHash) => $"scikids:rt:{tokenHash}";
    private static string GetSessionFamilyCacheKey(string familyId) => $"scikids:rt_family:{familyId}";
    private static string GetRevokedFamilyCacheKey(string familyId) => $"scikids:revoked_family:{familyId}";
    private static string GetRevokedJtiCacheKey(string jti) => $"scikids:revoked_jti:{jti}";
    private static string GetRevokedUserCacheKey(Guid userId) => $"scikids:revoked_user:{userId:N}";
}
