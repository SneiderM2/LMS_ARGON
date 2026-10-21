using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LMS.API.Data;
using LMS.API.DTOs;
using LMS.API.Entities;
using LMS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace LMS.API.Controllers;

/// <summary>
/// Controlador de Autenticación e Identidad de SciKids LMS.
/// Implementa Captcha accesible, emisión de JWT + Refresh Token en Cookie HttpOnly,
/// registro con validación de consentimiento parental (Ley 1581 / Ley 1098) y perfil actual (/me).
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    public const string RefreshTokenCookieName = "__Host-scikids-refresh";
    private static readonly Guid DefaultTenantId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly LMSDbContext _dbContext;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        LMSDbContext dbContext,
        IJwtTokenService jwtTokenService,
        IDistributedCache cache,
        ILogger<AuthController> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _jwtTokenService = jwtTokenService ?? throw new ArgumentNullException(nameof(jwtTokenService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Genera un desafío Captcha matemático accesible (NTC 5854 / WCAG 2.1 AA) con soporte de lectura por voz.
    /// </summary>
    [HttpGet("captcha")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CaptchaResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerateCaptcha(CancellationToken cancellationToken)
    {
        int operandA = RandomNumberGenerator.GetInt32(2, 12);
        int operandB = RandomNumberGenerator.GetInt32(1, 10);
        int expectedSum = operandA + operandB;

        string challengeId = Guid.NewGuid().ToString("N");
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        await _cache.SetStringAsync(
            GetCaptchaCacheKey(challengeId),
            expectedSum.ToString(),
            new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAt },
            cancellationToken);

        var response = new CaptchaResponse(
            ChallengeId: challengeId,
            QuestionText: $"¿Cuánto es {operandA} + {operandB}?",
            AudioHintText: $"Pregunta de seguridad para exploradores: suma el número {operandA} más el número {operandB}.",
            ExpiresAtUtc: expiresAt);

        return Ok(response);
    }

    /// <summary>
    /// Autentica un usuario validando Captcha, estado activo, no eliminación lógica y consentimiento Ley 1581.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Validación estricta del desafío Captcha de un solo uso
        string captchaKey = GetCaptchaCacheKey(request.CaptchaChallengeId);
        string? expectedAnswer = await _cache.GetStringAsync(captchaKey, cancellationToken);
        await _cache.RemoveAsync(captchaKey, cancellationToken);

        if (string.IsNullOrWhiteSpace(expectedAnswer) ||
            !string.Equals(expectedAnswer.Trim(), request.CaptchaAnswer.Trim(), StringComparison.Ordinal))
        {
            return Problem(
                title: "Validación Anti-Bot Fallida",
                detail: "La respuesta del desafío Captcha es incorrecta o ha expirado. Solicite un nuevo desafío.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string normalizedIdentifier = request.UsernameOrEmail.Trim().ToLowerInvariant();

        Usuario? usuario = await _dbContext.Usuarios
            .Include(u => u.Rol)
            .Include(u => u.Perfil)
            .Include(u => u.AlumnoPerfil)
            .FirstOrDefaultAsync(
                u => u.Username.ToLower() == normalizedIdentifier || u.Email.ToLower() == normalizedIdentifier,
                cancellationToken);

        if (usuario is null || !DbInitializer.VerifyPasswordPbkdf2(request.Password, usuario.PasswordHash))
        {
            _logger.LogWarning(
                "Intento de inicio de sesión inválido para el identificador {Identifier}.",
                request.UsernameOrEmail);

            return Problem(
                title: "Credenciales Inválidas",
                detail: "El usuario o la contraseña ingresados no son correctos.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (usuario.EliminadoLogico || !usuario.Activo)
        {
            return Problem(
                title: "Cuenta Inactiva o Suspendida",
                detail: "Esta cuenta se encuentra deshabilitada por administración. Contacte al coordinador escolar.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        bool consentimientoVerificado = usuario.Rol.Codigo == "ALUMNO"
            ? (usuario.AlumnoPerfil?.AutorizacionLey1581Activa ?? false)
            : (usuario.Perfil?.ConsentimientoHabeasDataFirmado ?? true);

        if (usuario.Rol.Codigo == "ALUMNO" && !consentimientoVerificado)
        {
            return Problem(
                title: "Consentimiento Parental Requerido (Ley 1581 / Ley 1098)",
                detail: "El alumno menor de edad requiere autorización vigente de su acudiente antes de ingresar a la plataforma.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        string codigoSeudonimo = usuario.AlumnoPerfil?.CodigoSeudonimo
                                 ?? $"{usuario.Rol.Codigo}-{usuario.Id.ToString("N")[..6].ToUpperInvariant()}";

        var authContext = new AuthenticatedUserContext(
            UserId: usuario.Id,
            TenantId: DefaultTenantId,
            Username: usuario.Username,
            Role: usuario.Rol.Codigo,
            PseudonymCode: codigoSeudonimo,
            HasActiveParentalConsent: consentimientoVerificado);

        TokenPairResult tokenPair = await _jwtTokenService.GenerateTokenPairAsync(
            authContext,
            existingSessionFamilyId: null,
            cancellationToken: cancellationToken);

        usuario.UltimoAccesoUtc = DateTime.UtcNow;
        if (usuario.AlumnoPerfil is not null)
        {
            usuario.AlumnoPerfil.RachaDiasActivos += 1;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        SetHttpOnlyRefreshTokenCookie(tokenPair.RefreshToken, tokenPair.RefreshTokenExpiresAtUtc);

        return Ok(new AuthResponse(
            UsuarioId: usuario.Id,
            Username: usuario.Username,
            Email: usuario.Email,
            NombreCompleto: $"{usuario.Nombres} {usuario.Apellidos}".Trim(),
            Rol: usuario.Rol.Codigo,
            CodigoSeudonimo: codigoSeudonimo,
            ConsentimientoParentalVerificado: consentimientoVerificado,
            AccessToken: tokenPair.AccessToken,
            AccessTokenExpiresAtUtc: tokenPair.AccessTokenExpiresAtUtc,
            SessionFamilyId: tokenPair.SessionFamilyId));
    }

    /// <summary>
    /// Registra un nuevo usuario en SciKids LMS verificando reglas de protección de menores (Ley 1098 de 2006).
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.AceptaConsentimientoLey1581)
        {
            return Problem(
                title: "Autorización Habeas Data Obligatoria",
                detail: "En cumplimiento de la Ley Estatutaria 1581 de 2012, es obligatorio aceptar el consentimiento informado de tratamiento de datos.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string normalizedUsername = request.Username.Trim().ToLowerInvariant();
        string normalizedEmail = request.Email.Trim().ToLowerInvariant();

        bool duplicateExists = await _dbContext.Usuarios.AnyAsync(
            u => u.Username.ToLower() == normalizedUsername || u.Email.ToLower() == normalizedEmail,
            cancellationToken);

        if (duplicateExists)
        {
            return Problem(
                title: "Usuario o Correo Duplicado",
                detail: "Ya existe una cuenta registrada con el mismo nombre de usuario o correo electrónico.",
                statusCode: StatusCodes.Status409Conflict);
        }

        Rol? rol = await _dbContext.Roles.FirstOrDefaultAsync(
            r => r.Codigo == request.RolCodigo && r.Activo,
            cancellationToken);

        if (rol is null)
        {
            return Problem(
                title: "Rol Inválido",
                detail: $"El rol '{request.RolCodigo}' no se encuentra activo en el catálogo.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (rol.Codigo == "ALUMNO")
        {
            if (request.GradoNivel is null or < 1 or > 5 ||
                string.IsNullOrWhiteSpace(request.NombreAcudiente) ||
                string.IsNullOrWhiteSpace(request.CorreoAcudiente) ||
                string.IsNullOrWhiteSpace(request.TelefonoAcudiente))
            {
                return Problem(
                    title: "Datos de Acudiente y Grado Requeridos (Ley 1098 de 2006)",
                    detail: "Para registrar un alumno de primaria (1° a 5°) es obligatorio suministrar el grado y los datos verificables del padre o acudiente.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        Guid newUserId = Guid.NewGuid();
        string consentHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"CONSENT-LEY1581|{normalizedUsername}|{DateTime.UtcNow:O}")));

        var nuevoUsuario = new Usuario
        {
            Id = newUserId,
            Username = request.Username.Trim(),
            Email = request.Email.Trim(),
            PasswordHash = DbInitializer.HashPasswordPbkdf2(request.Password),
            Nombres = request.Nombres.Trim(),
            Apellidos = request.Apellidos.Trim(),
            DocumentoIdentidadCifrado = DbInitializer.EncryptIdentifier(request.DocumentoIdentidad),
            RolId = rol.Id,
            Activo = true,
            EliminadoLogico = false,
            UltimoAccesoUtc = DateTime.UtcNow,
            FechaCreacionUtc = DateTime.UtcNow,
            Perfil = new Perfil
            {
                Id = Guid.NewGuid(),
                UsuarioId = newUserId,
                BiografiaCorta = $"Perfil {rol.Nombre} en SciKids LMS",
                PreferenciaAltoContraste = false,
                PreferenciaLecturaVoz = rol.Codigo == "ALUMNO",
                ConsentimientoHabeasDataFirmado = true,
                FechaConsentimientoUtc = DateTime.UtcNow,
                HashDocumentoConsentimiento = consentHash,
                FechaActualizacionUtc = DateTime.UtcNow
            }
        };

        string codigoSeudonimo = $"{rol.Codigo}-{newUserId.ToString("N")[..6].ToUpperInvariant()}";

        if (rol.Codigo == "ALUMNO")
        {
            int gradoId = request.GradoNivel!.Value;
            codigoSeudonimo = $"EXPLORADOR-{gradoId}{RandomNumberGenerator.GetInt32(100, 999)}";

            var nuevoAlumno = new Alumno
            {
                Id = Guid.NewGuid(),
                UsuarioId = newUserId,
                GradoId = gradoId,
                CodigoSeudonimo = codigoSeudonimo,
                FechaNacimiento = request.FechaNacimiento ?? new DateOnly(2017, 1, 1),
                NombreAcudiente = request.NombreAcudiente!.Trim(),
                CorreoAcudiente = request.CorreoAcudiente!.Trim(),
                TelefonoAcudiente = request.TelefonoAcudiente!.Trim(),
                AutorizacionLey1581Activa = true,
                PuntosGamificacion = 50, // Bono de bienvenida
                RachaDiasActivos = 1,
                Activo = true,
                FechaRegistroUtc = DateTime.UtcNow
            };

            await _dbContext.Alumnos.AddAsync(nuevoAlumno, cancellationToken);
        }

        await _dbContext.Usuarios.AddAsync(nuevoUsuario, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var authContext = new AuthenticatedUserContext(
            UserId: nuevoUsuario.Id,
            TenantId: DefaultTenantId,
            Username: nuevoUsuario.Username,
            Role: rol.Codigo,
            PseudonymCode: codigoSeudonimo,
            HasActiveParentalConsent: true);

        TokenPairResult tokenPair = await _jwtTokenService.GenerateTokenPairAsync(
            authContext,
            existingSessionFamilyId: null,
            cancellationToken: cancellationToken);

        SetHttpOnlyRefreshTokenCookie(tokenPair.RefreshToken, tokenPair.RefreshTokenExpiresAtUtc);

        return CreatedAtAction(
            nameof(GetCurrentUserProfile),
            null,
            new AuthResponse(
                UsuarioId: nuevoUsuario.Id,
                Username: nuevoUsuario.Username,
                Email: nuevoUsuario.Email,
                NombreCompleto: $"{nuevoUsuario.Nombres} {nuevoUsuario.Apellidos}".Trim(),
                Rol: rol.Codigo,
                CodigoSeudonimo: codigoSeudonimo,
                ConsentimientoParentalVerificado: true,
                AccessToken: tokenPair.AccessToken,
                AccessTokenExpiresAtUtc: tokenPair.AccessTokenExpiresAtUtc,
                SessionFamilyId: tokenPair.SessionFamilyId));
    }

    /// <summary>
    /// Retorna la información completa del usuario actualmente autenticado (/api/v1/auth/me).
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrentUserProfile(CancellationToken cancellationToken)
    {
        Guid userId = GetAuthenticatedUserId();

        Usuario? usuario = await _dbContext.Usuarios
            .AsNoTracking()
            .Include(u => u.Rol)
            .Include(u => u.Perfil)
            .Include(u => u.AlumnoPerfil)
                .ThenInclude(a => a!.Grado)
            .FirstOrDefaultAsync(u => u.Id == userId && !u.EliminadoLogico, cancellationToken);

        if (usuario is null)
        {
            return Problem(
                title: "Sesión Inválida",
                detail: "El usuario asociado al token ya no existe o fue desactivado.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        string seudonimo = usuario.AlumnoPerfil?.CodigoSeudonimo
                           ?? $"{usuario.Rol.Codigo}-{usuario.Id.ToString("N")[..6].ToUpperInvariant()}";

        var profileDto = new CurrentUserProfileDto(
            UsuarioId: usuario.Id,
            AlumnoId: usuario.AlumnoPerfil?.Id,
            Username: usuario.Username,
            Email: usuario.Email,
            Nombres: usuario.Nombres,
            Apellidos: usuario.Apellidos,
            Rol: usuario.Rol.Codigo,
            CodigoSeudonimo: seudonimo,
            GradoId: usuario.AlumnoPerfil?.GradoId,
            GradoNombre: usuario.AlumnoPerfil?.Grado.Nombre,
            PuntosGamificacion: usuario.AlumnoPerfil?.PuntosGamificacion ?? 0,
            RachaDiasActivos: usuario.AlumnoPerfil?.RachaDiasActivos ?? 1,
            PreferenciaAltoContraste: usuario.Perfil?.PreferenciaAltoContraste ?? false,
            PreferenciaLecturaVoz: usuario.Perfil?.PreferenciaLecturaVoz ?? true,
            ConsentimientoHabeasDataFirmado: usuario.Perfil?.ConsentimientoHabeasDataFirmado ?? true,
            UltimoAccesoUtc: usuario.UltimoAccesoUtc);

        return Ok(profileDto);
    }

    /// <summary>
    /// Rota de forma silenciosa el Refresh Token (desde Cookie HttpOnly o cuerpo) y emite un nuevo Access Token.
    /// Soporta las rutas /api/v1/auth/refresh-token y /api/v1/auth/refresh para compatibilidad con el interceptor Angular.
    /// </summary>
    [HttpPost("refresh-token")]
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenRequest? bodyRequest,
        CancellationToken cancellationToken)
    {
        string? rawRefreshToken = Request.Cookies[RefreshTokenCookieName] ?? bodyRequest?.RefreshToken;

        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return Problem(
                title: "Refresh Token Ausente",
                detail: "No se suministró una cookie HttpOnly de refresco ni un token válido.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        TokenPairResult? rotatedPair = await _jwtTokenService.RotateRefreshTokenAsync(
            rawRefreshToken,
            cancellationToken);

        if (rotatedPair is null)
        {
            DeleteRefreshTokenCookie();
            return Problem(
                title: "Sesión Expirada o Revocada",
                detail: "El token de refresco ya fue utilizado, expiró o fue revocado por seguridad.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        SetHttpOnlyRefreshTokenCookie(rotatedPair.RefreshToken, rotatedPair.RefreshTokenExpiresAtUtc);

        ClaimsPrincipal? principal = _jwtTokenService.GetPrincipalFromExpiredToken(rotatedPair.AccessToken);
        Guid userId = Guid.Parse(principal?.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? Guid.Empty.ToString());

        Usuario? usuario = await _dbContext.Usuarios
            .AsNoTracking()
            .Include(u => u.Rol)
            .Include(u => u.AlumnoPerfil)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        string rol = usuario?.Rol.Codigo ?? principal?.FindFirstValue(ClaimTypes.Role) ?? "ALUMNO";
        string seudonimo = usuario?.AlumnoPerfil?.CodigoSeudonimo
                           ?? principal?.FindFirstValue("pseudonym")
                           ?? "EXPLORADOR";

        return Ok(new AuthResponse(
            UsuarioId: userId,
            Username: usuario?.Username ?? principal?.FindFirstValue(JwtRegisteredClaimNames.UniqueName) ?? string.Empty,
            Email: usuario?.Email ?? string.Empty,
            NombreCompleto: usuario is not null ? $"{usuario.Nombres} {usuario.Apellidos}".Trim() : string.Empty,
            Rol: rol,
            CodigoSeudonimo: seudonimo,
            ConsentimientoParentalVerificado: true,
            AccessToken: rotatedPair.AccessToken,
            AccessTokenExpiresAtUtc: rotatedPair.AccessTokenExpiresAtUtc,
            SessionFamilyId: rotatedPair.SessionFamilyId));
    }

    /// <summary>
    /// Cierra la sesión actual revocando el Access Token JTI, la familia de Refresh Token y limpiando la Cookie HttpOnly.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        string? rawRefreshToken = Request.Cookies[RefreshTokenCookieName];
        string? jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);

        await _jwtTokenService.RevokeSessionAsync(rawRefreshToken, jti, cancellationToken);
        DeleteRefreshTokenCookie();

        return NoContent();
    }

    private Guid GetAuthenticatedUserId()
    {
        string? rawId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(rawId, out Guid userId))
        {
            throw new UnauthorizedAccessException("Identificador de usuario inválido en el token JWT.");
        }

        return userId;
    }

    private void SetHttpOnlyRefreshTokenCookie(string refreshToken, DateTimeOffset expiresAtUtc)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth",
            Expires = expiresAtUtc,
            IsEssential = true
        };

        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, cookieOptions);
    }

    private void DeleteRefreshTokenCookie()
    {
        Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth"
        });
    }

    private static string GetCaptchaCacheKey(string challengeId) => $"scikids:captcha:{challengeId}";
}
