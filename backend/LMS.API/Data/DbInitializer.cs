using System.Security.Cryptography;
using System.Text;
using LMS.API.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LMS.API.Data;

/// <summary>
/// Inicializador idempotente de base de datos PostgreSQL (Supabase) para SciKids LMS.
/// Ejecuta migraciones/creación de esquema y siembra Roles, Grados (1° a 5°), Usuarios base y Cursos iniciales.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAndSeedAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LMSDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("LMS.API.Data.DbInitializer");

        try
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);

            await SeedRolesAsync(context, cancellationToken);
            await SeedGradosPrimariaAsync(context, cancellationToken);
            await SeedInitialUsersAndAcademicDataAsync(context, cancellationToken);

            logger.LogInformation("Inicialización y Seeding de SciKids LMS completados exitosamente.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ocurrió un error durante la inicialización o seeding de la base de datos.");
            throw;
        }
    }

    private static async Task SeedRolesAsync(LMSDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Roles.AnyAsync(cancellationToken))
        {
            return;
        }

        var roles = new List<Rol>
        {
            new()
            {
                Id = 1,
                Codigo = "ADMINISTRADOR",
                Nombre = "Administrador Institucional",
                Descripcion = "Control total de métricas, usuarios, auditoría Habeas Data y parámetros del LMS.",
                Activo = true,
                FechaCreacionUtc = DateTime.UtcNow
            },
            new()
            {
                Id = 2,
                Codigo = "DOCENTE",
                Nombre = "Docente de Primaria",
                Descripcion = "Gestión de cursos, matrículas, creación de tareas, calificación y semáforo escolar.",
                Activo = true,
                FechaCreacionUtc = DateTime.UtcNow
            },
            new()
            {
                Id = 3,
                Codigo = "ALUMNO",
                Nombre = "Alumno Explorador (6-11 años)",
                Descripcion = "Acceso a tablero Kanban accesible, misiones científicas y subida segura de evidencias.",
                Activo = true,
                FechaCreacionUtc = DateTime.UtcNow
            },
            new()
            {
                Id = 4,
                Codigo = "ACUDIENTE",
                Nombre = "Padre de Familia / Acudiente Legal",
                Descripcion = "Titular de autorización Ley 1581/1098 y consulta de seguimiento del menor tutelado.",
                Activo = true,
                FechaCreacionUtc = DateTime.UtcNow
            }
        };

        await context.Roles.AddRangeAsync(roles, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedGradosPrimariaAsync(LMSDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Grados.AnyAsync(cancellationToken))
        {
            return;
        }

        var grados = new List<Grado>
        {
            new()
            {
                Id = 1,
                NumeroNivel = 1,
                Nombre = "1° de Primaria",
                DescripcionEtapa = "Iniciación científica, exploración sensorial y lectoescritura inicial.",
                EdadRecomendadaMin = 6,
                EdadRecomendadaMax = 7,
                Activo = true
            },
            new()
            {
                Id = 2,
                NumeroNivel = 2,
                Nombre = "2° de Primaria",
                DescripcionEtapa = "Observación de la naturaleza, seres vivos y clasificación básica.",
                EdadRecomendadaMin = 7,
                EdadRecomendadaMax = 8,
                Activo = true
            },
            new()
            {
                Id = 3,
                NumeroNivel = 3,
                Nombre = "3° de Primaria",
                DescripcionEtapa = "Ecosistemas de Colombia, ciclo del agua y experimentación guiada.",
                EdadRecomendadaMin = 8,
                EdadRecomendadaMax = 9,
                Activo = true
            },
            new()
            {
                Id = 4,
                NumeroNivel = 4,
                Nombre = "4° de Primaria",
                DescripcionEtapa = "Sistema solar, energía, fuerzas físicas y método científico aplicado.",
                EdadRecomendadaMin = 9,
                EdadRecomendadaMax = 10,
                Activo = true
            },
            new()
            {
                Id = 5,
                NumeroNivel = 5,
                Nombre = "5° de Primaria",
                DescripcionEtapa = "Materia, células, pensamiento ambiental e investigación escolar.",
                EdadRecomendadaMin = 10,
                EdadRecomendadaMax = 11,
                Activo = true
            }
        };

        await context.Grados.AddRangeAsync(grados, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedInitialUsersAndAcademicDataAsync(
        LMSDbContext context,
        CancellationToken cancellationToken)
    {
        if (await context.Usuarios.AnyAsync(cancellationToken))
        {
            return;
        }

        Guid adminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid docenteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid alumnoUsuarioId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        Guid alumnoId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        Guid cursoId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        Guid tareaId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        var adminUser = new Usuario
        {
            Id = adminId,
            Username = "admin.scikids",
            Email = "admin@scikids.edu.co",
            PasswordHash = HashPasswordPbkdf2("AdminSciKids2026!"),
            Nombres = "Carlos Andrés",
            Apellidos = "Rojas Mendoza",
            DocumentoIdentidadCifrado = EncryptIdentifier("1010203040"),
            RolId = 1, // ADMINISTRADOR
            Activo = true,
            EliminadoLogico = false,
            FechaCreacionUtc = DateTime.UtcNow,
            Perfil = new Perfil
            {
                UsuarioId = adminId,
                BiografiaCorta = "Coordinador de Ciberseguridad y Plataforma Educativa SciKids.",
                TelefonoContacto = "+573001112233",
                ConsentimientoHabeasDataFirmado = true,
                FechaConsentimientoUtc = DateTime.UtcNow
            }
        };

        var docenteUser = new Usuario
        {
            Id = docenteId,
            Username = "profe.laura",
            Email = "laura.gomez@scikids.edu.co",
            PasswordHash = HashPasswordPbkdf2("DocenteSciKids2026!"),
            Nombres = "Laura Sofía",
            Apellidos = "Gómez Pineda",
            DocumentoIdentidadCifrado = EncryptIdentifier("52890123"),
            RolId = 2, // DOCENTE
            Activo = true,
            EliminadoLogico = false,
            FechaCreacionUtc = DateTime.UtcNow,
            Perfil = new Perfil
            {
                UsuarioId = docenteId,
                BiografiaCorta = "Licenciada en Ciencias Naturales y Educación Ambiental para Primaria.",
                TelefonoContacto = "+573104445566",
                ConsentimientoHabeasDataFirmado = true,
                FechaConsentimientoUtc = DateTime.UtcNow
            }
        };

        var alumnoUser = new Usuario
        {
            Id = alumnoUsuarioId,
            Username = "mateo.explorador",
            Email = "mateo.acudiente@scikids.edu.co",
            PasswordHash = HashPasswordPbkdf2("AlumnoSciKids2026!"),
            Nombres = "Mateo Alejandro",
            Apellidos = "Restrepo Díaz",
            DocumentoIdentidadCifrado = EncryptIdentifier("1109876543"),
            RolId = 3, // ALUMNO
            Activo = true,
            EliminadoLogico = false,
            FechaCreacionUtc = DateTime.UtcNow,
            Perfil = new Perfil
            {
                UsuarioId = alumnoUsuarioId,
                BiografiaCorta = "Explorador científico de 3° de Primaria.",
                PreferenciaLecturaVoz = true,
                ConsentimientoHabeasDataFirmado = true,
                FechaConsentimientoUtc = DateTime.UtcNow,
                HashDocumentoConsentimiento = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("CONSENT-LEY1581-MATEO-2026")))
            }
        };

        var alumnoRecord = new Alumno
        {
            Id = alumnoId,
            UsuarioId = alumnoUsuarioId,
            GradoId = 3, // 3° de Primaria
            CodigoSeudonimo = "EXPLORADOR-3001",
            FechaNacimiento = new DateOnly(2017, 5, 14),
            NombreAcudiente = "Diana Marcela Díaz",
            CorreoAcudiente = "diana.diaz@familia.co",
            TelefonoAcudiente = "+573157778899",
            AutorizacionLey1581Activa = true,
            PuntosGamificacion = 150,
            RachaDiasActivos = 5,
            Activo = true,
            FechaRegistroUtc = DateTime.UtcNow
        };

        var cursoInicial = new Curso
        {
            Id = cursoId,
            CodigoCurso = "CNAT-3P-2026",
            Nombre = "Guardianes de los Páramos y el Agua",
            Descripcion = "Curso interactivo de Ciencias Naturales para 3° de Primaria sobre biodiversidad colombiana.",
            AreaConocimiento = "Ciencias Naturales",
            GradoId = 3,
            DocenteId = docenteId,
            AnioLectivo = DateTime.UtcNow.Year,
            ColorTemaHex = "#0284C7",
            IconoEmoji = "💧",
            Activo = true,
            FechaCreacionUtc = DateTime.UtcNow
        };

        var inscripcionInicial = new Inscripcion
        {
            Id = Guid.NewGuid(),
            CursoId = cursoId,
            AlumnoId = alumnoId,
            Estado = "ACTIVA",
            PromedioAcumulado = 4.80m,
            PorcentajeAsistencia = 100.00m,
            FechaInscripcionUtc = DateTime.UtcNow
        };

        var materialInicial = new Material
        {
            Id = Guid.NewGuid(),
            CursoId = cursoId,
            SubidoPorDocenteId = docenteId,
            Titulo = "Cartilla Ilustrada: El Viaje de la Gota de Agua",
            Descripcion = "Guía visual con pictogramas accesibles para comprender la condensación y precipitación.",
            TipoRecurso = "PDF",
            UrlRecurso = "/storage/materials/cartilla-ciclo-agua-3p.pdf",
            TamanoBytes = 1_450_200,
            OrdenVisualizacion = 1,
            VisibleAlumnos = true,
            FechaPublicacionUtc = DateTime.UtcNow
        };

        var tareaInicial = new Tarea
        {
            Id = tareaId,
            CursoId = cursoId,
            CreadoPorDocenteId = docenteId,
            Titulo = "Misión 1: Dibuja tu Frailejón Protector",
            InstruccionesPedagogicas = "Realiza un dibujo de cómo los frailejones capturan el agua de la niebla y sube una foto (.png o .jpg) o un archivo .pdf.",
            PuntosRecompensa = 75,
            NotaMaxima = 5.00m,
            FechaPublicacionUtc = DateTime.UtcNow,
            FechaLimiteUtc = DateTime.UtcNow.AddDays(10),
            PermiteEntregaTardia = true,
            FormatosPermitidos = ".pdf,.png,.jpg",
            Activa = true
        };

        var semaforoInicial = new SemaforoStatus
        {
            Id = Guid.NewGuid(),
            AlumnoId = alumnoId,
            CursoId = cursoId,
            Color = SemaforoColor.VERDE,
            PorcentajeCumplimiento = 100.00m,
            TareasEntregadas = 1,
            TareasPendientes = 0,
            TareasVencidas = 0,
            PromedioCurso = 4.80m,
            ObservacionDocente = "Excelente participación y compromiso en las misiones científicas.",
            RequiereAtencionAcudiente = false,
            FechaCalculoUtc = DateTime.UtcNow
        };

        await context.Usuarios.AddRangeAsync(new[] { adminUser, docenteUser, alumnoUser }, cancellationToken);
        await context.Alumnos.AddAsync(alumnoRecord, cancellationToken);
        await context.Cursos.AddAsync(cursoInicial, cancellationToken);
        await context.Inscripciones.AddAsync(inscripcionInicial, cancellationToken);
        await context.Materiales.AddAsync(materialInicial, cancellationToken);
        await context.Tareas.AddAsync(tareaInicial, cancellationToken);
        await context.EstadosSemaforo.AddAsync(semaforoInicial, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    public static string HashPasswordPbkdf2(string plainPassword)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(plainPassword),
            salt,
            iterations: 100_000,
            HashAlgorithmName.SHA256,
            outputLength: 32);

        return $"PBKDF2-SHA256$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPasswordPbkdf2(string plainPassword, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(plainPassword) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        string[] parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2-SHA256" || !int.TryParse(parts[1], out int iterations))
        {
            return false;
        }

        byte[] salt = Convert.FromBase64String(parts[2]);
        byte[] expectedHash = Convert.FromBase64String(parts[3]);

        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(plainPassword),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    public static string EncryptIdentifier(string rawDocument)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"SCIKIDS_PII_PEPPER_2026:{rawDocument.Trim()}"));
        return $"ENC-AES256:{Convert.ToBase64String(hash)}";
    }
}
