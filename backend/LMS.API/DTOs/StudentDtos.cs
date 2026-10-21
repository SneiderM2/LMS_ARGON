using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace LMS.API.DTOs;

/// <summary>
/// DTO para envío de entrega de tarea con archivo de evidencia validado por Magic Bytes.
/// </summary>
public sealed class AssignmentSubmitDto
{
    [Required]
    public Guid TareaId { get; set; }

    [MaxLength(1000)]
    public string? ComentarioAlumno { get; set; }

    [Required(ErrorMessage = "Debe adjuntar un archivo de evidencia (.pdf, .png, .jpg).")]
    public IFormFile ArchivoEvidencia { get; set; } = null!;
}

/// <summary>
/// DTO para actualizar el estado de una tarjeta de tarea en el Tablero Kanban Accesible.
/// </summary>
public sealed record UpdateKanbanStatusDto(
    [Required]
    [RegularExpression("^(PENDIENTE|EN_PROGRESO|ENTREGADA)$")]
    string NuevoEstadoKanban
);

/// <summary>
/// Resumen de progreso gamificado y semáforo académico del alumno.
/// </summary>
public sealed record StudentProgressDto(
    Guid AlumnoId,
    string CodigoSeudonimo,
    string NombreAlumno,
    string GradoNombre,
    int PuntosGamificacion,
    int RachaDiasActivos,
    int TotalTareasAsignadas,
    int TareasCompletadas,
    int TareasEnProgreso,
    int TareasPendientes,
    decimal PorcentajeAvanceGlobal,
    decimal PromedioGeneral,
    string ColorSemaforoGlobal,
    SubmissionFileDto? UltimoArchivoSubido
);

/// <summary>
/// Metadatos de un archivo de evidencia verificado y almacenado con nombre GUID.
/// </summary>
public sealed record SubmissionFileDto(
    Guid ArchivoId,
    Guid EntregaId,
    string NombreOriginalSanitizado,
    string NombreAlmacenamientoGuid,
    string RutaRelativaStorage,
    string MimeTypeVerificado,
    long TamanoBytes,
    string ChecksumSha256,
    DateTime FechaSubidaUtc
);

/// <summary>
/// Tarjeta de misión/tarea para el tablero Kanban del estudiante.
/// </summary>
public sealed record StudentTaskCardDto(
    Guid TareaId,
    Guid CursoId,
    string CursoNombre,
    string CursoIconoEmoji,
    string CursoColorHex,
    string Titulo,
    string InstruccionesPedagogicas,
    int PuntosRecompensa,
    decimal NotaMaxima,
    DateTime FechaLimiteUtc,
    string FormatosPermitidos,
    string EstadoKanban,
    Guid? EntregaId,
    decimal? CalificacionNumerica,
    string? EscalaCualitativaMen,
    string? RetroalimentacionDocente,
    IReadOnlyList<SubmissionFileDto> ArchivosAdjuntos
);

/// <summary>
/// Resumen de curso inscrito en el panel del estudiante.
/// </summary>
public sealed record StudentCourseCardDto(
    Guid CursoId,
    string CodigoCurso,
    string Nombre,
    string AreaConocimiento,
    string DocenteNombre,
    string ColorTemaHex,
    string IconoEmoji,
    decimal PromedioAcumulado,
    string ColorSemaforo
);

/// <summary>
/// Respuesta integral del Dashboard del Estudiante.
/// </summary>
public sealed record StudentDashboardDto(
    StudentProgressDto Progreso,
    IReadOnlyList<StudentCourseCardDto> CursosInscritos,
    IReadOnlyList<StudentTaskCardDto> MisionesKanban
);
