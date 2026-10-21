using System.ComponentModel.DataAnnotations;

namespace LMS.API.DTOs;

/// <summary>
/// DTO para creación de un nuevo curso escolar por parte de un Docente o Administrador.
/// </summary>
public sealed record CreateCourseDto(
    [Required]
    [MaxLength(30)]
    string CodigoCurso,

    [Required]
    [MaxLength(150)]
    string Nombre,

    [Required]
    [MaxLength(600)]
    string Descripcion,

    [Required]
    [MaxLength(100)]
    string AreaConocimiento,

    [Required]
    [Range(1, 5)]
    int GradoId,

    [Range(2020, 2100)]
    int AnioLectivo = 2026,

    [MaxLength(12)]
    string ColorTemaHex = "#0284C7",

    [MaxLength(16)]
    string IconoEmoji = "🔬"
);

/// <summary>
/// DTO para matricular un estudiante de primaria en un curso.
/// </summary>
public sealed record EnrollStudentDto(
    [Required]
    Guid CursoId,

    [Required]
    Guid AlumnoId
);

/// <summary>
/// DTO para crear una nueva tarea o misión científica dentro de un curso.
/// </summary>
public sealed record CreateTaskDto(
    [Required]
    [MaxLength(160)]
    string Titulo,

    [Required]
    [MaxLength(2000)]
    string InstruccionesPedagogicas,

    [Range(10, 1000)]
    int PuntosRecompensa,

    [Range(1, 5)]
    decimal NotaMaxima,

    [Required]
    DateTime FechaLimiteUtc,

    bool PermiteEntregaTardia = true,

    [MaxLength(80)]
    string FormatosPermitidos = ".pdf,.png,.jpg"
);

/// <summary>
/// DTO para calificar la entrega de un alumno según escala numérica y cualitativa MEN.
/// </summary>
public sealed record GradeSubmissionDto(
    [Required]
    [Range(0, 5)]
    decimal CalificacionNumerica,

    [Required]
    [RegularExpression("^(SUPERIOR|ALTO|BASICO|BAJO)$")]
    string EscalaCualitativaMen,

    [Required]
    [MaxLength(1000)]
    string RetroalimentacionDocente
);

/// <summary>
/// Estado de un alumno en el Semáforo Escolar Pedagógico (VERDE, AMARILLO, ROJO).
/// </summary>
public sealed record StudentStatusDto(
    Guid AlumnoId,
    Guid UsuarioId,
    string CodigoSeudonimo,
    string NombreCompleto,
    string GradoNombre,
    Guid CursoId,
    string CursoNombre,
    string ColorSemaforo,
    decimal PorcentajeCumplimiento,
    int TareasEntregadas,
    int TareasPendientes,
    int TareasVencidas,
    decimal PromedioCurso,
    bool RequiereAtencionAcudiente,
    string NombreAcudiente,
    string CorreoAcudiente,
    string? ObservacionDocente,
    DateTime FechaCalculoUtc
);

/// <summary>
/// Resumen de curso asignado a un docente.
/// </summary>
public sealed record TeacherCourseSummaryDto(
    Guid CursoId,
    string CodigoCurso,
    string Nombre,
    string Descripcion,
    string AreaConocimiento,
    int GradoId,
    string GradoNombre,
    int AnioLectivo,
    string ColorTemaHex,
    string IconoEmoji,
    int TotalAlumnosInscritos,
    int TotalTareasActivas,
    int AlumnosEnAlertaRoja,
    int AlumnosEnAlertaAmarilla
);
