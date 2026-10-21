using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LMS.API.Data;
using LMS.API.DTOs;
using LMS.API.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LMS.API.Controllers;

/// <summary>
/// Controlador de Gestión Académica Docente en SciKids LMS.
/// Gestiona cursos, matrícula de alumnos, publicación de misiones/tareas, calificación y Semáforo Escolar.
/// </summary>
[ApiController]
[Route("api/v1/teacher")]
[Authorize(Roles = "DOCENTE,ADMINISTRADOR")]
public sealed class TeacherController : ControllerBase
{
    private readonly LMSDbContext _dbContext;
    private readonly ILogger<TeacherController> _logger;

    public TeacherController(
        LMSDbContext dbContext,
        ILogger<TeacherController> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lista todos los cursos asignados al docente autenticado con contadores de matrícula y alertas de semáforo.
    /// </summary>
    [HttpGet("courses")]
    [ProducesResponseType(typeof(IReadOnlyList<TeacherCourseSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeacherCourses(CancellationToken cancellationToken)
    {
        (Guid docenteId, string role) = GetCallerContext();

        IQueryable<Curso> query = _dbContext.Cursos
            .AsNoTracking()
            .Include(c => c.Grado)
            .Include(c => c.Inscripciones)
            .Include(c => c.Tareas)
            .Include(c => c.EstadosSemaforo)
            .Where(c => c.Activo);

        if (!role.Equals("ADMINISTRADOR", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(c => c.DocenteId == docenteId);
        }

        List<Curso> cursos = await query
            .OrderBy(c => c.GradoId)
            .ThenBy(c => c.Nombre)
            .ToListAsync(cancellationToken);

        List<TeacherCourseSummaryDto> resultado = cursos.Select(c => new TeacherCourseSummaryDto(
            CursoId: c.Id,
            CodigoCurso: c.CodigoCurso,
            Nombre: c.Nombre,
            Descripcion: c.Descripcion,
            AreaConocimiento: c.AreaConocimiento,
            GradoId: c.GradoId,
            GradoNombre: c.Grado.Nombre,
            AnioLectivo: c.AnioLectivo,
            ColorTemaHex: c.ColorTemaHex,
            IconoEmoji: c.IconoEmoji,
            TotalAlumnosInscritos: c.Inscripciones.Count(i => i.Estado == "ACTIVA"),
            TotalTareasActivas: c.Tareas.Count(t => t.Activa),
            AlumnosEnAlertaRoja: c.EstadosSemaforo.Count(s => s.Color == SemaforoColor.ROJO),
            AlumnosEnAlertaAmarilla: c.EstadosSemaforo.Count(s => s.Color == SemaforoColor.AMARILLO)
        )).ToList();

        return Ok(resultado);
    }

    /// <summary>
    /// Crea un nuevo curso académico asignado al docente actual y a un grado de primaria (1° a 5°).
    /// </summary>
    [HttpPost("courses")]
    [ProducesResponseType(typeof(TeacherCourseSummaryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCourse(
        [FromBody] CreateCourseDto request,
        CancellationToken cancellationToken)
    {
        (Guid docenteId, _) = GetCallerContext();

        string normalizedCode = request.CodigoCurso.Trim().ToUpperInvariant();
        bool codeExists = await _dbContext.Cursos.AnyAsync(
            c => c.CodigoCurso.ToUpper() == normalizedCode,
            cancellationToken);

        if (codeExists)
        {
            return Problem(
                title: "Código de Curso Duplicado",
                detail: $"Ya existe un curso registrado con el código '{normalizedCode}'.",
                statusCode: StatusCodes.Status409Conflict);
        }

        Grado? grado = await _dbContext.Grados.FirstOrDefaultAsync(
            g => g.Id == request.GradoId && g.Activo,
            cancellationToken);

        if (grado is null)
        {
            return Problem(
                title: "Grado Inválido",
                detail: "El grado seleccionado debe corresponder a un nivel de 1° a 5° de Primaria activo.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var nuevoCurso = new Curso
        {
            Id = Guid.NewGuid(),
            CodigoCurso = normalizedCode,
            Nombre = request.Nombre.Trim(),
            Descripcion = request.Descripcion.Trim(),
            AreaConocimiento = request.AreaConocimiento.Trim(),
            GradoId = grado.Id,
            DocenteId = docenteId,
            AnioLectivo = request.AnioLectivo,
            ColorTemaHex = request.ColorTemaHex,
            IconoEmoji = request.IconoEmoji,
            Activo = true,
            FechaCreacionUtc = DateTime.UtcNow
        };

        await _dbContext.Cursos.AddAsync(nuevoCurso, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var summary = new TeacherCourseSummaryDto(
            CursoId: nuevoCurso.Id,
            CodigoCurso: nuevoCurso.CodigoCurso,
            Nombre: nuevoCurso.Nombre,
            Descripcion: nuevoCurso.Descripcion,
            AreaConocimiento: nuevoCurso.AreaConocimiento,
            GradoId: grado.Id,
            GradoNombre: grado.Nombre,
            AnioLectivo: nuevoCurso.AnioLectivo,
            ColorTemaHex: nuevoCurso.ColorTemaHex,
            IconoEmoji: nuevoCurso.IconoEmoji,
            TotalAlumnosInscritos: 0,
            TotalTareasActivas: 0,
            AlumnosEnAlertaRoja: 0,
            AlumnosEnAlertaAmarilla: 0);

        return CreatedAtAction(nameof(GetCourseSemaforoStatus), new { cursoId = nuevoCurso.Id }, summary);
    }

    /// <summary>
    /// Matricula (inscribe) a un alumno en un curso del docente e inicializa su registro de Semáforo Escolar.
    /// </summary>
    [HttpPost("courses/enroll")]
    [ProducesResponseType(typeof(StudentStatusDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EnrollStudent(
        [FromBody] EnrollStudentDto request,
        CancellationToken cancellationToken)
    {
        (Guid docenteId, string role) = GetCallerContext();

        Curso? curso = await _dbContext.Cursos
            .Include(c => c.Tareas)
            .FirstOrDefaultAsync(c => c.Id == request.CursoId && c.Activo, cancellationToken);

        if (curso is null)
        {
            return Problem(
                title: "Curso No Encontrado",
                detail: "El curso especificado no existe o se encuentra inactivo.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!role.Equals("ADMINISTRADOR", StringComparison.OrdinalIgnoreCase) && curso.DocenteId != docenteId)
        {
            return Problem(
                title: "Acceso Denegado al Curso",
                detail: "Solo el docente titular del curso puede matricular estudiantes en él.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        Alumno? alumno = await _dbContext.Alumnos
            .Include(a => a.Usuario)
            .Include(a => a.Grado)
            .FirstOrDefaultAsync(
                a => a.Id == request.AlumnoId && a.Activo && !a.Usuario.EliminadoLogico,
                cancellationToken);

        if (alumno is null)
        {
            return Problem(
                title: "Alumno No Encontrado",
                detail: "El estudiante especificado no existe o su perfil está inactivo.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!alumno.AutorizacionLey1581Activa)
        {
            return Problem(
                title: "Bloqueo Legal Ley 1581 / Ley 1098",
                detail: "No se puede matricular al menor porque no cuenta con autorización parental activa de tratamiento de datos.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        bool alreadyEnrolled = await _dbContext.Inscripciones.AnyAsync(
            i => i.CursoId == curso.Id && i.AlumnoId == alumno.Id && i.Estado == "ACTIVA",
            cancellationToken);

        if (alreadyEnrolled)
        {
            return Problem(
                title: "Matrícula Duplicada",
                detail: "El alumno ya se encuentra matriculado activamente en este curso.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var nuevaInscripcion = new Inscripcion
        {
            Id = Guid.NewGuid(),
            CursoId = curso.Id,
            AlumnoId = alumno.Id,
            Estado = "ACTIVA",
            PromedioAcumulado = 5.00m,
            PorcentajeAsistencia = 100.00m,
            FechaInscripcionUtc = DateTime.UtcNow
        };

        await _dbContext.Inscripciones.AddAsync(nuevaInscripcion, cancellationToken);

        SemaforoStatus semaforo = await RecalculateAndPersistSemaforoAsync(curso.Id, alumno.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var statusDto = MapToStudentStatusDto(alumno, curso, semaforo);
        return CreatedAtAction(nameof(GetCourseSemaforoStatus), new { cursoId = curso.Id }, statusDto);
    }

    /// <summary>
    /// Crea y publica una nueva tarea/misión científica dentro de un curso del docente.
    /// </summary>
    [HttpPost("courses/{cursoId:guid}/tasks")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateTask(
        Guid cursoId,
        [FromBody] CreateTaskDto request,
        CancellationToken cancellationToken)
    {
        (Guid docenteId, string role) = GetCallerContext();

        Curso? curso = await _dbContext.Cursos
            .FirstOrDefaultAsync(c => c.Id == cursoId && c.Activo, cancellationToken);

        if (curso is null)
        {
            return Problem(
                title: "Curso No Encontrado",
                detail: "El curso indicado no existe.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!role.Equals("ADMINISTRADOR", StringComparison.OrdinalIgnoreCase) && curso.DocenteId != docenteId)
        {
            return Problem(
                title: "Operación No Autorizada",
                detail: "Solo el docente asignado al curso puede crear tareas en él.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var nuevaTarea = new Tarea
        {
            Id = Guid.NewGuid(),
            CursoId = curso.Id,
            CreadoPorDocenteId = docenteId,
            Titulo = request.Titulo.Trim(),
            InstruccionesPedagogicas = request.InstruccionesPedagogicas.Trim(),
            PuntosRecompensa = request.PuntosRecompensa,
            NotaMaxima = request.NotaMaxima,
            FechaPublicacionUtc = DateTime.UtcNow,
            FechaLimiteUtc = request.FechaLimiteUtc,
            PermiteEntregaTardia = request.PermiteEntregaTardia,
            FormatosPermitidos = request.FormatosPermitidos,
            Activa = true
        };

        await _dbContext.Tareas.AddAsync(nuevaTarea, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetCourseSemaforoStatus),
            new { cursoId = curso.Id },
            new
            {
                TareaId = nuevaTarea.Id,
                nuevaTarea.CursoId,
                nuevaTarea.Titulo,
                nuevaTarea.PuntosRecompensa,
                nuevaTarea.FechaLimiteUtc
            });
    }

    /// <summary>
    /// Califica la entrega de un alumno, otorga puntos de gamificación y recalcula automáticamente el Semáforo Escolar.
    /// </summary>
    [HttpPost("submissions/{entregaId:guid}/grade")]
    [ProducesResponseType(typeof(StudentStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GradeSubmission(
        Guid entregaId,
        [FromBody] GradeSubmissionDto request,
        CancellationToken cancellationToken)
    {
        (Guid docenteId, string role) = GetCallerContext();

        Entrega? entrega = await _dbContext.Entregas
            .Include(e => e.Tarea)
                .ThenInclude(t => t.Curso)
            .Include(e => e.Alumno)
                .ThenInclude(a => a.Usuario)
            .Include(e => e.Alumno)
                .ThenInclude(a => a.Grado)
            .FirstOrDefaultAsync(e => e.Id == entregaId, cancellationToken);

        if (entrega is null)
        {
            return Problem(
                title: "Entrega No Encontrada",
                detail: "No existe una entrega registrada con el identificador proporcionado.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!role.Equals("ADMINISTRADOR", StringComparison.OrdinalIgnoreCase) &&
            entrega.Tarea.Curso.DocenteId != docenteId)
        {
            return Problem(
                title: "Acceso Denegado",
                detail: "Solo el docente titular del curso puede calificar esta entrega.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        bool eraCalificadaPreviamente = entrega.EstadoKanban == "CALIFICADA";

        entrega.CalificacionNumerica = request.CalificacionNumerica;
        entrega.EscalaCualitativaMen = request.EscalaCualitativaMen;
        entrega.RetroalimentacionDocente = request.RetroalimentacionDocente.Trim();
        entrega.CalificadoPorDocenteId = docenteId;
        entrega.EstadoKanban = "CALIFICADA";
        entrega.FechaCalificacionUtc = DateTime.UtcNow;

        if (!eraCalificadaPreviamente && request.CalificacionNumerica >= 3.0m)
        {
            entrega.Alumno.PuntosGamificacion += entrega.Tarea.PuntosRecompensa;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        SemaforoStatus semaforo = await RecalculateAndPersistSemaforoAsync(
            entrega.Tarea.CursoId,
            entrega.AlumnoId,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Entrega {EntregaId} calificada por docente {DocenteId}. Nuevo estado semáforo alumno {AlumnoId}: {Color}",
            entrega.Id,
            docenteId,
            entrega.AlumnoId,
            semaforo.Color);

        return Ok(MapToStudentStatusDto(entrega.Alumno, entrega.Tarea.Curso, semaforo));
    }

    /// <summary>
    /// Obtiene y recalcula en tiempo real el Semáforo Escolar Pedagógico (VERDE, AMARILLO, ROJO)
    /// de todos los alumnos inscritos en un curso del docente.
    /// </summary>
    [HttpGet("courses/{cursoId:guid}/semaforo")]
    [ProducesResponseType(typeof(IReadOnlyList<StudentStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCourseSemaforoStatus(
        Guid cursoId,
        CancellationToken cancellationToken)
    {
        (Guid docenteId, string role) = GetCallerContext();

        Curso? curso = await _dbContext.Cursos
            .Include(c => c.Inscripciones.Where(i => i.Estado == "ACTIVA"))
                .ThenInclude(i => i.Alumno)
                    .ThenInclude(a => a.Usuario)
            .Include(c => c.Inscripciones.Where(i => i.Estado == "ACTIVA"))
                .ThenInclude(i => i.Alumno)
                    .ThenInclude(a => a.Grado)
            .FirstOrDefaultAsync(c => c.Id == cursoId && c.Activo, cancellationToken);

        if (curso is null)
        {
            return Problem(
                title: "Curso No Encontrado",
                detail: "El curso solicitado no existe.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (!role.Equals("ADMINISTRADOR", StringComparison.OrdinalIgnoreCase) && curso.DocenteId != docenteId)
        {
            return Problem(
                title: "Acceso Restringido (RLS / RBAC)",
                detail: "Un docente solo puede consultar el listado y semáforo de alumnos matriculados en sus cursos asignados.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var resultado = new List<StudentStatusDto>();

        foreach (Inscripcion inscripcion in curso.Inscripciones)
        {
            SemaforoStatus semaforo = await RecalculateAndPersistSemaforoAsync(
                curso.Id,
                inscripcion.AlumnoId,
                cancellationToken);

            resultado.Add(MapToStudentStatusDto(inscripcion.Alumno, curso, semaforo));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Ordenar priorizando alumnos en ROJO y AMARILLO para intervención pedagógica temprana
        List<StudentStatusDto> ordenado = resultado
            .OrderByDescending(s => s.ColorSemaforo == "ROJO")
            .ThenByDescending(s => s.ColorSemaforo == "AMARILLO")
            .ThenBy(s => s.NombreCompleto)
            .ToList();

        return Ok(ordenado);
    }

    private async Task<SemaforoStatus> RecalculateAndPersistSemaforoAsync(
        Guid cursoId,
        Guid alumnoId,
        CancellationToken cancellationToken)
    {
        List<Tarea> tareasCurso = await _dbContext.Tareas
            .AsNoTracking()
            .Where(t => t.CursoId == cursoId && t.Activa)
            .ToListAsync(cancellationToken);

        List<Guid> tareaIds = tareasCurso.Select(t => t.Id).ToList();

        List<Entrega> entregasAlumno = await _dbContext.Entregas
            .AsNoTracking()
            .Where(e => e.AlumnoId == alumnoId && tareaIds.Contains(e.TareaId))
            .ToListAsync(cancellationToken);

        int totalTareas = tareasCurso.Count;
        int entregadas = entregasAlumno.Count(e =>
            e.EstadoKanban is "ENTREGADA" or "CALIFICADA");

        DateTime ahora = DateTime.UtcNow;
        HashSet<Guid> tareasEntregadasIds = entregasAlumno
            .Where(e => e.EstadoKanban is "ENTREGADA" or "CALIFICADA")
            .Select(e => e.TareaId)
            .ToHashSet();

        int vencidasSinEntregar = tareasCurso.Count(t =>
            t.FechaLimiteUtc < ahora && !tareasEntregadasIds.Contains(t.Id));

        int pendientesEnPlazo = Math.Max(0, totalTareas - entregadas - vencidasSinEntregar);

        decimal porcentajeCumplimiento = totalTareas == 0
            ? 100.00m
            : Math.Round((decimal)entregadas / totalTareas * 100m, 2);

        List<decimal> notas = entregasAlumno
            .Where(e => e.CalificacionNumerica.HasValue)
            .Select(e => e.CalificacionNumerica!.Value)
            .ToList();

        decimal promedio = notas.Count > 0 ? Math.Round(notas.Average(), 2) : 4.50m;

        // Algoritmo de Semáforo Escolar Pedagógico
        SemaforoColor color;
        string observacion;
        bool requiereAtencionAcudiente;

        if (vencidasSinEntregar >= 2 || porcentajeCumplimiento < 50m || promedio < 3.0m)
        {
            color = SemaforoColor.ROJO;
            observacion = "Alerta Temprana Roja: Requiere acompañamiento prioritario del docente y contacto con el acudiente.";
            requiereAtencionAcudiente = true;
        }
        else if (vencidasSinEntregar == 1 || porcentajeCumplimiento < 80m || promedio < 3.8m)
        {
            color = SemaforoColor.AMARILLO;
            observacion = "Alerta Preventiva Amarilla: Tiene misiones pendientes próximas a vencer o en refuerzo.";
            requiereAtencionAcudiente = false;
        }
        else
        {
            color = SemaforoColor.VERDE;
            observacion = "Desempeño Sobresaliente Verde: Al día con sus misiones científicas.";
            requiereAtencionAcudiente = false;
        }

        SemaforoStatus? semaforo = await _dbContext.EstadosSemaforo
            .FirstOrDefaultAsync(s => s.CursoId == cursoId && s.AlumnoId == alumnoId, cancellationToken);

        if (semaforo is null)
        {
            semaforo = new SemaforoStatus
            {
                Id = Guid.NewGuid(),
                CursoId = cursoId,
                AlumnoId = alumnoId
            };
            await _dbContext.EstadosSemaforo.AddAsync(semaforo, cancellationToken);
        }

        semaforo.Color = color;
        semaforo.PorcentajeCumplimiento = porcentajeCumplimiento;
        semaforo.TareasEntregadas = entregadas;
        semaforo.TareasPendientes = pendientesEnPlazo;
        semaforo.TareasVencidas = vencidasSinEntregar;
        semaforo.PromedioCurso = promedio;
        semaforo.ObservacionDocente = observacion;
        semaforo.RequiereAtencionAcudiente = requiereAtencionAcudiente;
        semaforo.FechaCalculoUtc = ahora;

        return semaforo;
    }

    private static StudentStatusDto MapToStudentStatusDto(
        Alumno alumno,
        Curso curso,
        SemaforoStatus semaforo)
    {
        return new StudentStatusDto(
            AlumnoId: alumno.Id,
            UsuarioId: alumno.UsuarioId,
            CodigoSeudonimo: alumno.CodigoSeudonimo,
            NombreCompleto: $"{alumno.Usuario.Nombres} {alumno.Usuario.Apellidos}".Trim(),
            GradoNombre: alumno.Grado.Nombre,
            CursoId: curso.Id,
            CursoNombre: curso.Nombre,
            ColorSemaforo: semaforo.Color.ToString(),
            PorcentajeCumplimiento: semaforo.PorcentajeCumplimiento,
            TareasEntregadas: semaforo.TareasEntregadas,
            TareasPendientes: semaforo.TareasPendientes,
            TareasVencidas: semaforo.TareasVencidas,
            PromedioCurso: semaforo.PromedioCurso,
            RequiereAtencionAcudiente: semaforo.RequiereAtencionAcudiente,
            NombreAcudiente: alumno.NombreAcudiente,
            CorreoAcudiente: alumno.CorreoAcudiente,
            ObservacionDocente: semaforo.ObservacionDocente,
            FechaCalculoUtc: semaforo.FechaCalculoUtc);
    }

    private (Guid UserId, string Role) GetCallerContext()
    {
        string? rawId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        string role = User.FindFirstValue(ClaimTypes.Role) ?? "DOCENTE";

        if (!Guid.TryParse(rawId, out Guid userId))
        {
            throw new UnauthorizedAccessException("Token JWT sin identificador de docente válido.");
        }

        return (userId, role);
    }
}
