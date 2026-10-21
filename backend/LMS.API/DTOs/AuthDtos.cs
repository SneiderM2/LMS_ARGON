using System.ComponentModel.DataAnnotations;

namespace LMS.API.DTOs;

/// <summary>
/// Solicitud de inicio de sesión con validación de Captcha accesible.
/// </summary>
public sealed record LoginRequest(
    [Required(ErrorMessage = "El nombre de usuario o correo electrónico es obligatorio.")]
    [MaxLength(150)]
    string UsernameOrEmail,

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [MinLength(8)]
    [MaxLength(128)]
    string Password,

    [Required(ErrorMessage = "El identificador del desafío Captcha es obligatorio.")]
    string CaptchaChallengeId,

    [Required(ErrorMessage = "La respuesta del Captcha es obligatoria.")]
    string CaptchaAnswer
);

/// <summary>
/// Solicitud de registro de nuevo usuario en SciKids LMS con cumplimiento Ley 1581 / Ley 1098.
/// </summary>
public sealed record RegisterRequest(
    [Required]
    [MaxLength(60)]
    string Username,

    [Required]
    [EmailAddress]
    [MaxLength(150)]
    string Email,

    [Required]
    [MinLength(8)]
    [MaxLength(128)]
    string Password,

    [Required]
    [MaxLength(100)]
    string Nombres,

    [Required]
    [MaxLength(100)]
    string Apellidos,

    [Required]
    [MaxLength(40)]
    string DocumentoIdentidad,

    [Required]
    [RegularExpression("^(ADMINISTRADOR|DOCENTE|ALUMNO|ACUDIENTE)$")]
    string RolCodigo,

    [Range(1, 5)]
    int? GradoNivel,

    DateOnly? FechaNacimiento,

    [MaxLength(150)]
    string? NombreAcudiente,

    [EmailAddress]
    [MaxLength(150)]
    string? CorreoAcudiente,

    [MaxLength(30)]
    string? TelefonoAcudiente,

    bool AceptaConsentimientoLey1581
);

/// <summary>
/// Respuesta de autenticación OAuth2/OIDC con Access Token de corta duración.
/// El Refresh Token se entrega paralelamente mediante cookie HttpOnly SameSite=Strict.
/// </summary>
public sealed record AuthResponse(
    Guid UsuarioId,
    string Username,
    string Email,
    string NombreCompleto,
    string Rol,
    string CodigoSeudonimo,
    bool ConsentimientoParentalVerificado,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string SessionFamilyId
);

/// <summary>
/// Desafío Captcha accesible (matemático y textual apto para lectores de pantalla y niños de primaria).
/// </summary>
public sealed record CaptchaResponse(
    string ChallengeId,
    string QuestionText,
    string AudioHintText,
    DateTimeOffset ExpiresAtUtc
);

/// <summary>
/// Solicitud opcional por cuerpo para rotación de Refresh Token cuando no se envía vía Cookie.
/// </summary>
public sealed record RefreshTokenRequest(
    string? RefreshToken
);

/// <summary>
/// Información detallada del perfil autenticado (/api/v1/auth/me).
/// </summary>
public sealed record CurrentUserProfileDto(
    Guid UsuarioId,
    Guid? AlumnoId,
    string Username,
    string Email,
    string Nombres,
    string Apellidos,
    string Rol,
    string CodigoSeudonimo,
    int? GradoId,
    string? GradoNombre,
    int PuntosGamificacion,
    int RachaDiasActivos,
    bool PreferenciaAltoContraste,
    bool PreferenciaLecturaVoz,
    bool ConsentimientoHabeasDataFirmado,
    DateTime? UltimoAccesoUtc
);
