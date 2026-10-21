using System.ComponentModel.DataAnnotations;

namespace LMS.API.DTOs;

/// <summary>
/// DTO de administración del ciclo de vida de usuarios en SciKids LMS.
/// </summary>
public sealed record UserManagementDto(
    Guid UsuarioId,
    string Username,
    string Email,
    string Nombres,
    string Apellidos,
    string RolCodigo,
    string RolNombre,
    bool Activo,
    bool EliminadoLogico,
    DateTime? FechaEliminacionUtc,
    string? MotivoEliminacion,
    DateTime? UltimoAccesoUtc,
    DateTime FechaCreacionUtc,
    string? CodigoSeudonimoAlumno,
    string? GradoAlumno,
    bool? ConsentimientoLey1581Activo
);

/// <summary>
/// Solicitud de borrado lógico (Soft Delete) con justificación auditable.
/// </summary>
public sealed record SoftDeleteUserDto(
    [Required(ErrorMessage = "Debe especificar el motivo de inactivación o borrado lógico.")]
    [MaxLength(250)]
    string MotivoEliminacion
);

/// <summary>
/// Métricas agregadas del sistema e indicadores del Semáforo Escolar y Cumplimiento Legal.
/// </summary>
public sealed record SystemMetricsDto(
    int TotalUsuariosRegistrados,
    int UsuariosActivos,
    int UsuariosEliminadosLogicamente,
    int TotalAlumnosActivos,
    int AlumnosConConsentimientoLey1581,
    int TotalDocentesActivos,
    int TotalCursosActivos,
    int TotalTareasPublicadas,
    int TotalEntregasRecibidas,
    int TotalArchivosVerificadosMagicBytes,
    long AlmacenamientoTotalBytes,
    int AlumnosSemaforoVerde,
    int AlumnosSemaforoAmarillo,
    int AlumnosSemaforoRojo,
    IReadOnlyList<RoleDistributionMetricDto> DistribucionPorRol,
    IReadOnlyList<GradeEnrollmentMetricDto> DistribucionPorGrado,
    DateTime FechaCorteUtc
);

public sealed record RoleDistributionMetricDto(
    string RolCodigo,
    string RolNombre,
    int TotalUsuariosActivos
);

public sealed record GradeEnrollmentMetricDto(
    int GradoId,
    int NumeroNivel,
    string GradoNombre,
    int TotalAlumnos,
    int TotalCursos
);
