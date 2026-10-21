using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LMS.API.Data;
using LMS.API.DTOs;
using LMS.API.Entities;
using LMS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LMS.API.Controllers;

/// <summary>
/// Controlador del Portal del Alumno Explorador (6-11 años) en SciKids LMS.
/// Gestiona el Dashboard gamificado, el Tablero Kanban accesible y la subida segura de evidencias
/// validada por Magic Bytes y sanitizada con GUID.
/// </summary>
[ApiController]
[Route("api/v1/student")]
[Authorize(Roles = "ALUMNO,ESTUDIANTE,ADMINISTRADOR")]
public sealed class StudentController : ControllerBase
{
    private readonly LMSDbContext _dbContext;
    private readonly IFileUploadValidationService _fileValidationService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<StudentController> _logger;

    public StudentController(
        LMSDbContext dbContext,
        IFileUploadValidationService fileValidationService,
        IWebHostEnvironment environment,
        ILogger<StudentController> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _fileValidationService = fileValidationService ?? throw new ArgumentNullException(nameof(fileValidationService));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Obtiene el Dashboard completo del alumno: progreso gamificado, cursos inscritos y tablero Kanban de misiones.
    /// </summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(StudentDashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStudentDashboard(CancellationToken cancellationToken)
    {
        Alumno? alumno = await ResolveAuthenticatedStudentAsync(cancellationToken);
        if (alumno is null)
        {
            return Problem(
                title: "Perfil de Alumno No Encontrado",
                detail: "El usuario actual no posee una ficha de alumno activa.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!alumno.AutorizacionLey1581Activa)
        {
            return Problem(
                title: "Consentimiento Parental Requerido (Ley 1581 / Ley 1098)",
                detail: "El acceso al tablero de aprendizaje requiere autorización activa del padre o acudiente.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        List<Inscripcion> inscripciones = await _dbContext.Inscripciones
            .AsNoTracking()
            .Include(i => i.Curso)
                .ThenInclude(c => c.Docente)
            .Where(i => i.AlumnoId == alumno.Id && i.Estado == "ACTIVA" && i.Curso.Activo)
            .ToListAsync(cancellationToken);

        List<Guid> cursoIds = inscripciones.Select(i => i.CursoId).ToList();

        List<SemaforoStatus> semaforos = await _dbContext.EstadosSemaforo
            .AsNoTracking()
            .Where(s => s.AlumnoId == alumno.Id && cursoIds.Contains(s.CursoId))
            .ToListAsync(cancellationToken);

        List<StudentTaskCardDto> misiones = await BuildStudentTaskCardsAsync(alumno.Id, cursoIds, cancellationToken);

        StudentProgressDto progreso = BuildStudentProgressSummary(alumno, misiones, semaforos, ultimoArchivo: null);

        List<StudentCourseCardDto> cursosDto = inscripciones.Select(i =>
        {
            SemaforoStatus? sem = semaforos.FirstOrDefault(s => s.CursoId == i.CursoId);
            return new StudentCourseCardDto(
                CursoId: i.CursoId,
                CodigoCurso: i.Curso.CodigoCurso,
                Nombre: i.Curso.Nombre,
                AreaConocimiento: i.Curso.AreaConocimiento,
                DocenteNombre: $"{i.Curso.Docente.Nombres} {i.Curso.Docente.Apellidos}".Trim(),
                ColorTemaHex: i.Curso.ColorTemaHex,
                IconoEmoji: i.Curso.IconoEmoji,
                PromedioAcumulado: sem?.PromedioCurso ?? i.PromedioAcumulado,
                ColorSemaforo: sem?.Color.ToString() ?? "VERDE");
        }).ToList();

        return Ok(new StudentDashboardDto(
            Progreso: progreso,
            CursosInscritos: cursosDto,
            MisionesKanban: misiones));
    }

    /// <summary>
    /// Lista todas las tareas/misiones asignadas a los cursos donde el alumno está matriculado.
    /// </summary>
    [HttpGet("tasks")]
    [ProducesResponseType(typeof(IReadOnlyList<StudentTaskCardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStudentTasks(CancellationToken cancellationToken)
    {
        Alumno? alumno = await ResolveAuthenticatedStudentAsync(cancellationToken);
        if (alumno is null)
        {
            return Problem(
                title: "Alumno No Encontrado",
                detail: "No se encontró el perfil académico del estudiante.",
                statusCode: StatusCodes.Status404NotFound);
        }

        List<Guid> cursoIds = await _dbContext.Inscripciones
            .AsNoTracking()
            .Where(i => i.AlumnoId == alumno.Id && i.Estado == "ACTIVA")
            .Select(i => i.CursoId)
            .ToListAsync(cancellationToken);

        List<StudentTaskCardDto> tareas = await BuildStudentTaskCardsAsync(alumno.Id, cursoIds, cancellationToken);
        return Ok(tareas);
    }

    /// <summary>
    /// Actualiza la columna de una tarea en el Tablero Kanban Accesible (PENDIENTE, EN_PROGRESO, ENTREGADA).
    /// </summary>
    [HttpPatch("tasks/{tareaId:guid}/kanban")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTaskKanbanStatus(
        Guid tareaId,
        [FromBody] UpdateKanbanStatusDto request,
        CancellationToken cancellationToken)
    {
        Alumno? alumno = await ResolveAuthenticatedStudentAsync(cancellationToken);
        if (alumno is null)
        {
            return Problem(
                title: "Alumno No Encontrado",
                detail: "Perfil de alumno no localizado.",
                statusCode: StatusCodes.Status404NotFound);
        }

        Tarea? tarea = await _dbContext.Tareas
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tareaId && t.Activa, cancellationToken);

        if (tarea is null)
        {
            return Problem(
                title: "Tarea No Encontrada",
                detail: "La misión seleccionada no existe o ya no está activa.",
                statusCode: StatusCodes.Status404NotFound);
        }

        bool isEnrolled = await _dbContext.Inscripciones.AnyAsync(
            i => i.CursoId == tarea.CursoId && i.AlumnoId == alumno.Id && i.Estado == "ACTIVA",
            cancellationToken);

        if (!isEnrolled)
        {
            return Problem(
                title: "Acceso Denegado (Aislamiento RLS)",
                detail: "El estudiante solo puede modificar el estado de misiones pertenecientes a sus cursos inscritos.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        Entrega? entrega = await _dbContext.Entregas
            .FirstOrDefaultAsync(e => e.TareaId == tareaId && e.AlumnoId == alumno.Id, cancellationToken);

        if (entrega is null)
        {
            entrega = new Entrega
            {
                Id = Guid.NewGuid(),
                TareaId = tareaId,
                AlumnoId = alumno.Id,
                EstadoKanban = request.NuevoEstadoKanban,
                EsEntregaTardia = DateTime.UtcNow > tarea.FechaLimiteUtc,
                FechaEnvioUtc = DateTime.UtcNow
            };
            await _dbContext.Entregas.AddAsync(entrega, cancellationToken);
        }
        else if (entrega.EstadoKanban != "CALIFICADA")
        {
            entrega.EstadoKanban = request.NuevoEstadoKanban;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            TareaId = tareaId,
            EntregaId = entrega.Id,
            EstadoKanban = entrega.EstadoKanban,
            ActualizadoUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Recibe la entrega de una tarea con archivo adjunto, verifica Magic Bytes, renombra con GUID
    /// contra ataques Path Traversal, persiste el archivo y devuelve el progreso actualizado del alumno.
    /// </summary>
    [HttpPost("tasks/submit")]
    [RequestSizeLimit(FileUploadValidationService.MaxAllowedFileSizeBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = FileUploadValidationService.MaxAllowedFileSizeBytes)]
    [ProducesResponseType(typeof(StudentProgressDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitAssignment(
        [FromForm] AssignmentSubmitDto request,
        CancellationToken cancellationToken)
    {
        Alumno? alumno = await ResolveAuthenticatedStudentAsync(cancellationToken);
        if (alumno is null)
        {
            return Problem(
                title: "Perfil de Alumno No Encontrado",
                detail: "No se pudo identificar la ficha del alumno autenticado.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!alumno.AutorizacionLey1581Activa)
        {
            return Problem(
                title: "Consentimiento Parental No Verificado (Ley 1581 / Ley 1098)",
                detail: "No se permite almacenar evidencias de menores sin autorización vigente del acudiente.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        Tarea? tarea = await _dbContext.Tareas
            .FirstOrDefaultAsync(t => t.Id == request.TareaId && t.Activa, cancellationToken);

        if (tarea is null)
        {
            return Problem(
                title: "Misión No Encontrada",
                detail: "La tarea especificada no existe o se encuentra cerrada.",
                statusCode: StatusCodes.Status404NotFound);
        }

        bool isEnrolled = await _dbContext.Inscripciones.AnyAsync(
            i => i.CursoId == tarea.CursoId && i.AlumnoId == alumno.Id && i.Estado == "ACTIVA",
            cancellationToken);

        if (!isEnrolled)
        {
            return Problem(
                title: "Curso No Inscrito",
                detail: "Solo los alumnos matriculados en el curso pueden enviar entregas para esta misión.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        bool esTardia = DateTime.UtcNow > tarea.FechaLimiteUtc;
        if (esTardia && !tarea.PermiteEntregaTardia)
        {
            return Problem(
                title: "Plazo de Entrega Vencido",
                detail: "La fecha límite de esta misión ha expirado y no admite entregas extemporáneas.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Validación multicapa: Extensión + Límite 10MB + Inspección Magic Bytes + Renombrado GUID
        FileUploadValidationResult validation = await _fileValidationService.ValidateAndSanitizeAsync(
            request.ArchivoEvidencia,
            cancellationToken);

        if (!validation.IsValid)
        {
            return Problem(
                title: "Archivo Rechazado por Seguridad (Magic Bytes / Lista Blanca)",
                detail: validation.ErrorMessage,
                statusCode: StatusCodes.Status400BadRequest);
        }

        string relativeStoragePath = _fileValidationService.BuildSafeStorageObjectPath(
            tarea.CursoId,
            tarea.Id,
            alumno.Id,
            validation.SanitizedFileName);

        // Almacenamiento seguro en disco local aislado (o bucket de Supabase Storage)
        string baseStorageRoot = Path.Combine(
            _environment.ContentRootPath,
            "App_Data",
            "SecureEvidenceStorage");

        string physicalFullPath = Path.GetFullPath(
            Path.Combine(baseStorageRoot, relativeStoragePath.Replace('/', Path.DirectorySeparatorChar)));

        // Verificación canónica adicional contra Path Traversal
        if (!physicalFullPath.StartsWith(Path.GetFullPath(baseStorageRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Intento de Path Traversal bloqueado al resolver la ruta física.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(physicalFullPath)!);
        await using (var targetFileStream = new FileStream(physicalFullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await request.ArchivoEvidencia.CopyToAsync(targetFileStream, cancellationToken);
        }

        Entrega? entrega = await _dbContext.Entregas
            .Include(e => e.Archivos)
            .FirstOrDefaultAsync(e => e.TareaId == tarea.Id && e.AlumnoId == alumno.Id, cancellationToken);

        bool primeraEntrega = entrega is null;
        if (entrega is null)
        {
            entrega = new Entrega
            {
                Id = Guid.NewGuid(),
                TareaId = tarea.Id,
                AlumnoId = alumno.Id
            };
            await _dbContext.Entregas.AddAsync(entrega, cancellationToken);
        }

        entrega.EstadoKanban = "ENTREGADA";
        entrega.ComentarioAlumno = request.ComentarioAlumno?.Trim();
        entrega.EsEntregaTardia = esTardia;
        entrega.FechaEnvioUtc = DateTime.UtcNow;

        var nuevoArchivo = new ArchivoEntrega
        {
            Id = Guid.NewGuid(),
            EntregaId = entrega.Id,
            NombreOriginalSanitizado = validation.OriginalFileNameSanitized,
            NombreAlmacenamientoGuid = validation.SanitizedFileName,
            RutaRelativaStorage = relativeStoragePath,
            MimeTypeVerificado = validation.VerifiedMimeType,
            Extension = validation.Extension,
            TamanoBytes = validation.FileSizeBytes,
            ChecksumSha256 = validation.Sha256Checksum,
            MagicBytesVerificados = true,
            FechaSubidaUtc = DateTime.UtcNow
        };

        await _dbContext.ArchivosEntrega.AddAsync(nuevoArchivo, cancellationToken);

        if (primeraEntrega)
        {
            alumno.PuntosGamificacion += 25; // Bono inmediato por enviar la evidencia a tiempo
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Evidencia subida exitosamente por alumno {AlumnoId} en tarea {TareaId}. Archivo GUID: {GuidName}, SHA256: {Hash}",
            alumno.Id,
            tarea.Id,
            nuevoArchivo.NombreAlmacenamientoGuid,
            nuevoArchivo.ChecksumSha256);

        List<Guid> cursoIds = await _dbContext.Inscripciones
            .AsNoTracking()
            .Where(i => i.AlumnoId == alumno.Id && i.Estado == "ACTIVA")
            .Select(i => i.CursoId)
            .ToListAsync(cancellationToken);

        List<SemaforoStatus> semaforos = await _dbContext.EstadosSemaforo
            .AsNoTracking()
            .Where(s => s.AlumnoId == alumno.Id && cursoIds.Contains(s.CursoId))
            .ToListAsync(cancellationToken);

        List<StudentTaskCardDto> misiones = await BuildStudentTaskCardsAsync(alumno.Id, cursoIds, cancellationToken);

        var archivoDto = new SubmissionFileDto(
            ArchivoId: nuevoArchivo.Id,
            EntregaId: entrega.Id,
            NombreOriginalSanitizado: nuevoArchivo.NombreOriginalSanitizado,
            NombreAlmacenamientoGuid: nuevoArchivo.NombreAlmacenamientoGuid,
            RutaRelativaStorage: nuevoArchivo.RutaRelativaStorage,
            MimeTypeVerificado: nuevoArchivo.MimeTypeVerificado,
            TamanoBytes: nuevoArchivo.TamanoBytes,
            ChecksumSha256: nuevoArchivo.ChecksumSha256,
            FechaSubidaUtc: nuevoArchivo.FechaSubidaUtc);

        StudentProgressDto progressDto = BuildStudentProgressSummary(alumno, misiones, semaforos, archivoDto);

        return CreatedAtAction(nameof(GetStudentDashboard), null, progressDto);
    }

    private async Task<Alumno?> ResolveAuthenticatedStudentAsync(CancellationToken cancellationToken)
    {
        string? rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(rawUserId, out Guid userId))
        {
            return null;
        }

        return await _dbContext.Alumnos
            .Include(a => a.Usuario)
            .Include(a => a.Grado)
            .FirstOrDefaultAsync(
                a => a.UsuarioId == userId && a.Activo && !a.Usuario.EliminadoLogico,
                cancellationToken);
    }

    private async Task<List<StudentTaskCardDto>> BuildStudentTaskCardsAsync(
        Guid alumnoId,
        List<Guid> cursoIds,
        CancellationToken cancellationToken)
    {
        if (cursoIds.Count == 0)
        {
            return new List<StudentTaskCardDto>();
        }

        List<Tarea> tareas = await _dbContext.Tareas
            .AsNoTracking()
            .Include(t => t.Curso)
            .Where(t => cursoIds.Contains(t.CursoId) && t.Activa)
            .OrderBy(t => t.FechaLimiteUtc)
            .ToListAsync(cancellationToken);

        List<Guid> tareaIds = tareas.Select(t => t.Id).ToList();

        List<Entrega> entregas = await _dbContext.Entregas
            .AsNoTracking()
            .Include(e => e.Archivos)
            .Where(e => e.AlumnoId == alumnoId && tareaIds.Contains(e.TareaId))
            .ToListAsync(cancellationToken);

        return tareas.Select(t =>
        {
            Entrega? ent = entregas.FirstOrDefault(e => e.TareaId == t.Id);
            IReadOnlyList<SubmissionFileDto> archivos = ent?.Archivos
                .OrderByDescending(a => a.FechaSubidaUtc)
                .Select(a => new SubmissionFileDto(
                    ArchivoId: a.Id,
                    EntregaId: a.EntregaId,
                    NombreOriginalSanitizado: a.NombreOriginalSanitizado,
                    NombreAlmacenamientoGuid: a.NombreAlmacenamientoGuid,
                    RutaRelativaStorage: a.RutaRelativaStorage,
                    MimeTypeVerificado: a.MimeTypeVerificado,
                    TamanoBytes: a.TamanoBytes,
                    ChecksumSha256: a.ChecksumSha256,
                    FechaSubidaUtc: a.FechaSubidaUtc))
                .ToList() ?? (IReadOnlyList<SubmissionFileDto>)Array.Empty<SubmissionFileDto>();

            return new StudentTaskCardDto(
                TareaId: t.Id,
                CursoId: t.CursoId,
                CursoNombre: t.Curso.Nombre,
                CursoIconoEmoji: t.Curso.IconoEmoji,
                CursoColorHex: t.Curso.ColorTemaHex,
                Titulo: t.Titulo,
                InstruccionesPedagogicas: t.InstruccionesPedagogicas,
                PuntosRecompensa: t.PuntosRecompensa,
                NotaMaxima: t.NotaMaxima,
                FechaLimiteUtc: t.FechaLimiteUtc,
                FormatosPermitidos: t.FormatosPermitidos,
                EstadoKanban: ent?.EstadoKanban ?? "PENDIENTE",
                EntregaId: ent?.Id,
                CalificacionNumerica: ent?.CalificacionNumerica,
                EscalaCualitativaMen: ent?.EscalaCualitativaMen,
                RetroalimentacionDocente: ent?.RetroalimentacionDocente,
                ArchivosAdjuntos: archivos);
        }).ToList();
    }

    private static StudentProgressDto BuildStudentProgressSummary(
        Alumno alumno,
        List<StudentTaskCardDto> misiones,
        List<SemaforoStatus> semaforos,
        SubmissionFileDto? ultimoArchivo)
    {
        int total = misiones.Count;
        int completadas = misiones.Count(m => m.EstadoKanban is "ENTREGADA" or "CALIFICADA");
        int enProgreso = misiones.Count(m => m.EstadoKanban == "EN_PROGRESO");
        int pendientes = Math.Max(0, total - completadas - enProgreso);

        decimal porcentajeAvance = total == 0
            ? 100.00m
            : Math.Round((decimal)completadas / total * 100m, 2);

        List<decimal> calificaciones = misiones
            .Where(m => m.CalificacionNumerica.HasValue)
            .Select(m => m.CalificacionNumerica!.Value)
            .ToList();

        decimal promedioGeneral = calificaciones.Count > 0
            ? Math.Round(calificaciones.Average(), 2)
            : 4.80m;

        string colorGlobal = "VERDE";
        if (semaforos.Any(s => s.Color == SemaforoColor.ROJO))
        {
            colorGlobal = "ROJO";
        }
        else if (semaforos.Any(s => s.Color == SemaforoColor.AMARILLO))
        {
            colorGlobal = "AMARILLO";
        }

        return new StudentProgressDto(
            AlumnoId: alumno.Id,
            CodigoSeudonimo: alumno.CodigoSeudonimo,
            NombreAlumno: $"{alumno.Usuario.Nombres} {alumno.Usuario.Apellidos}".Trim(),
            GradoNombre: alumno.Grado.Nombre,
            PuntosGamificacion: alumno.PuntosGamificacion,
            RachaDiasActivos: alumno.RachaDiasActivos,
            TotalTareasAsignadas: total,
            TareasCompletadas: completadas,
            TareasEnProgreso: enProgreso,
            TareasPendientes: pendientes,
            PorcentajeAvanceGlobal: porcentajeAvance,
            PromedioGeneral: promedioGeneral,
            ColorSemaforoGlobal: colorGlobal,
            UltimoArchivoSubido: ultimoArchivo);
    }
}
