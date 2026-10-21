using LMS.API.Data;
using LMS.API.DTOs;
using LMS.API.Entities;
using LMS.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LMS.API.Controllers;

/// <summary>
/// Controlador de Administración Institucional de SciKids LMS.
/// Gestiona el ciclo de vida de usuarios (Soft Delete auditable, reactivación) y métricas ejecutivas del sistema.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "ADMINISTRADOR,SUPER_ADMIN")]
public sealed class AdminController : ControllerBase
{
    private readonly LMSDbContext _dbContext;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        LMSDbContext dbContext,
        IJwtTokenService jwtTokenService,
        ILogger<AdminController> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _jwtTokenService = jwtTokenService ?? throw new ArgumentNullException(nameof(jwtTokenService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lista todos los usuarios registrados en la plataforma (incluyendo estado activo y borrado lógico).
    /// </summary>
    [HttpGet("users")]
    [ProducesResponseType(typeof(IReadOnlyList<UserManagementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllUsers(
        [FromQuery] bool includeSoftDeleted = true,
        [FromQuery] string? rolCodigo = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Usuario> query = _dbContext.Usuarios
            .AsNoTracking()
            .Include(u => u.Rol)
            .Include(u => u.AlumnoPerfil)
                .ThenInclude(a => a!.Grado);

        if (!includeSoftDeleted)
        {
            query = query.Where(u => !u.EliminadoLogico);
        }

        if (!string.IsNullOrWhiteSpace(rolCodigo))
        {
            string normalizedRole = rolCodigo.Trim().ToUpperInvariant();
            query = query.Where(u => u.Rol.Codigo == normalizedRole);
        }

        List<Usuario> usuarios = await query
            .OrderByDescending(u => u.FechaCreacionUtc)
            .ToListAsync(cancellationToken);

        List<UserManagementDto> result = usuarios.Select(MapToUserManagementDto).ToList();
        return Ok(result);
    }

    /// <summary>
    /// Ejecuta el borrado lógico (Soft Delete) de un usuario, inactiva su perfil académico
    /// y revoca inmediatamente todas sus sesiones activas en la lista de revocación JWT.
    /// </summary>
    [HttpDelete("users/{usuarioId:guid}")]
    [ProducesResponseType(typeof(UserManagementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SoftDeleteUser(
        Guid usuarioId,
        [FromBody] SoftDeleteUserDto request,
        CancellationToken cancellationToken)
    {
        Usuario? usuario = await _dbContext.Usuarios
            .Include(u => u.Rol)
            .Include(u => u.AlumnoPerfil)
                .ThenInclude(a => a!.Grado)
            .FirstOrDefaultAsync(u => u.Id == usuarioId, cancellationToken);

        if (usuario is null)
        {
            return Problem(
                title: "Usuario No Encontrado",
                detail: "No existe un usuario con el identificador especificado.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (usuario.EliminadoLogico)
        {
            return Problem(
                title: "Usuario Previamente Eliminado",
                detail: "La cuenta ya se encontraba en estado de borrado lógico.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        usuario.Activo = false;
        usuario.EliminadoLogico = true;
        usuario.FechaEliminacionUtc = DateTime.UtcNow;
        usuario.MotivoEliminacion = request.MotivoEliminacion.Trim();

        if (usuario.AlumnoPerfil is not null)
        {
            usuario.AlumnoPerfil.Activo = false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Revocación inmediata de todos los tokens JWT y Refresh Tokens activos del usuario
        await _jwtTokenService.RevokeAllUserSessionsAsync(
            usuario.Id,
            $"ADMIN_SOFT_DELETE:{usuario.MotivoEliminacion}",
            cancellationToken);

        _logger.LogInformation(
            "Borrado lógico (Soft Delete) aplicado al usuario {UsuarioId} ({Username}). Motivo: {Motivo}",
            usuario.Id,
            usuario.Username,
            usuario.MotivoEliminacion);

        return Ok(MapToUserManagementDto(usuario));
    }

    /// <summary>
    /// Restaura una cuenta previamente marcada con borrado lógico (Soft Delete).
    /// </summary>
    [HttpPatch("users/{usuarioId:guid}/restore")]
    [ProducesResponseType(typeof(UserManagementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RestoreSoftDeletedUser(
        Guid usuarioId,
        CancellationToken cancellationToken)
    {
        Usuario? usuario = await _dbContext.Usuarios
            .Include(u => u.Rol)
            .Include(u => u.AlumnoPerfil)
                .ThenInclude(a => a!.Grado)
            .FirstOrDefaultAsync(u => u.Id == usuarioId, cancellationToken);

        if (usuario is null)
        {
            return Problem(
                title: "Usuario No Encontrado",
                detail: "El usuario especificado no existe.",
                statusCode: StatusCodes.Status404NotFound);
        }

        usuario.EliminadoLogico = false;
        usuario.Activo = true;
        usuario.FechaEliminacionUtc = null;
        usuario.MotivoEliminacion = null;

        if (usuario.AlumnoPerfil is not null)
        {
            usuario.AlumnoPerfil.Activo = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(MapToUserManagementDto(usuario));
    }

    /// <summary>
    /// Calcula y devuelve las métricas consolidadas del sistema, distribución por rol/grado y Semáforo Escolar.
    /// </summary>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(SystemMetricsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSystemMetrics(CancellationToken cancellationToken)
    {
        int totalUsuarios = await _dbContext.Usuarios.CountAsync(cancellationToken);
        int usuariosActivos = await _dbContext.Usuarios.CountAsync(u => u.Activo && !u.EliminadoLogico, cancellationToken);
        int usuariosEliminados = await _dbContext.Usuarios.CountAsync(u => u.EliminadoLogico, cancellationToken);

        int totalAlumnosActivos = await _dbContext.Alumnos.CountAsync(a => a.Activo && !a.Usuario.EliminadoLogico, cancellationToken);
        int alumnosConConsentimiento = await _dbContext.Alumnos.CountAsync(
            a => a.Activo && !a.Usuario.EliminadoLogico && a.AutorizacionLey1581Activa,
            cancellationToken);

        int totalDocentesActivos = await _dbContext.Usuarios.CountAsync(
            u => u.Activo && !u.EliminadoLogico && u.Rol.Codigo == "DOCENTE",
            cancellationToken);

        int totalCursosActivos = await _dbContext.Cursos.CountAsync(c => c.Activo, cancellationToken);
        int totalTareasPublicadas = await _dbContext.Tareas.CountAsync(t => t.Activa, cancellationToken);
        int totalEntregasRecibidas = await _dbContext.Entregas.CountAsync(
            e => e.EstadoKanban == "ENTREGADA" || e.EstadoKanban == "CALIFICADA",
            cancellationToken);

        int totalArchivosVerificados = await _dbContext.ArchivosEntrega.CountAsync(
            a => a.MagicBytesVerificados,
            cancellationToken);

        long almacenamientoTotalBytes = await _dbContext.ArchivosEntrega
            .SumAsync(a => (long?)a.TamanoBytes, cancellationToken) ?? 0L;

        int semaforoVerde = await _dbContext.EstadosSemaforo.CountAsync(s => s.Color == SemaforoColor.VERDE, cancellationToken);
        int semaforoAmarillo = await _dbContext.EstadosSemaforo.CountAsync(s => s.Color == SemaforoColor.AMARILLO, cancellationToken);
        int semaforoRojo = await _dbContext.EstadosSemaforo.CountAsync(s => s.Color == SemaforoColor.ROJO, cancellationToken);

        List<RoleDistributionMetricDto> distribucionPorRol = await _dbContext.Roles
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .Select(r => new RoleDistributionMetricDto(
                r.Codigo,
                r.Nombre,
                r.Usuarios.Count(u => u.Activo && !u.EliminadoLogico)))
            .ToListAsync(cancellationToken);

        List<GradeEnrollmentMetricDto> distribucionPorGrado = await _dbContext.Grados
            .AsNoTracking()
            .OrderBy(g => g.NumeroNivel)
            .Select(g => new GradeEnrollmentMetricDto(
                g.Id,
                g.NumeroNivel,
                g.Nombre,
                g.Alumnos.Count(a => a.Activo && !a.Usuario.EliminadoLogico),
                g.Cursos.Count(c => c.Activo)))
            .ToListAsync(cancellationToken);

        var metrics = new SystemMetricsDto(
            TotalUsuariosRegistrados: totalUsuarios,
            UsuariosActivos: usuariosActivos,
            UsuariosEliminadosLogicamente: usuariosEliminados,
            TotalAlumnosActivos: totalAlumnosActivos,
            AlumnosConConsentimientoLey1581: alumnosConConsentimiento,
            TotalDocentesActivos: totalDocentesActivos,
            TotalCursosActivos: totalCursosActivos,
            TotalTareasPublicadas: totalTareasPublicadas,
            TotalEntregasRecibidas: totalEntregasRecibidas,
            TotalArchivosVerificadosMagicBytes: totalArchivosVerificados,
            AlmacenamientoTotalBytes: almacenamientoTotalBytes,
            AlumnosSemaforoVerde: semaforoVerde,
            AlumnosSemaforoAmarillo: semaforoAmarillo,
            AlumnosSemaforoRojo: semaforoRojo,
            DistribucionPorRol: distribucionPorRol,
            DistribucionPorGrado: distribucionPorGrado,
            FechaCorteUtc: DateTime.UtcNow);

        return Ok(metrics);
    }

    private static UserManagementDto MapToUserManagementDto(Usuario u)
    {
        return new UserManagementDto(
            UsuarioId: u.Id,
            Username: u.Username,
            Email: u.Email,
            Nombres: u.Nombres,
            Apellidos: u.Apellidos,
            RolCodigo: u.Rol.Codigo,
            RolNombre: u.Rol.Nombre,
            Activo: u.Activo,
            EliminadoLogico: u.EliminadoLogico,
            FechaEliminacionUtc: u.FechaEliminacionUtc,
            MotivoEliminacion: u.MotivoEliminacion,
            UltimoAccesoUtc: u.UltimoAccesoUtc,
            FechaCreacionUtc: u.FechaCreacionUtc,
            CodigoSeudonimoAlumno: u.AlumnoPerfil?.CodigoSeudonimo,
            GradoAlumno: u.AlumnoPerfil?.Grado?.Nombre,
            ConsentimientoLey1581Activo: u.AlumnoPerfil?.AutorizacionLey1581Activa);
    }
}
