using LMS.API.Entities;
using Microsoft.EntityFrameworkCore;

namespace LMS.API.Data;

/// <summary>
/// Contexto principal de datos Entity Framework Core configurado para PostgreSQL (Supabase) en snake_case.
/// </summary>
public class LMSDbContext : DbContext
{
    public LMSDbContext(DbContextOptions<LMSDbContext> options)
        : base(options)
    {
    }

    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Perfil> Perfiles => Set<Perfil>();
    public DbSet<Grado> Grados => Set<Grado>();
    public DbSet<Alumno> Alumnos => Set<Alumno>();
    public DbSet<Curso> Cursos => Set<Curso>();
    public DbSet<Inscripcion> Inscripciones => Set<Inscripcion>();
    public DbSet<Material> Materiales => Set<Material>();
    public DbSet<Tarea> Tareas => Set<Tarea>();
    public DbSet<Entrega> Entregas => Set<Entrega>();
    public DbSet<ArchivoEntrega> ArchivosEntrega => Set<ArchivoEntrega>();
    public DbSet<SemaforoStatus> EstadosSemaforo => Set<SemaforoStatus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("public");

        // 1. Configuración de ROL (roles)
        modelBuilder.Entity<Rol>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(r => r.Id).HasName("pk_roles");
            entity.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(r => r.Codigo).HasColumnName("codigo").HasMaxLength(40).IsRequired();
            entity.Property(r => r.Nombre).HasColumnName("nombre").HasMaxLength(80).IsRequired();
            entity.Property(r => r.Descripcion).HasColumnName("descripcion").HasMaxLength(250).IsRequired();
            entity.Property(r => r.Activo).HasColumnName("activo").HasDefaultValue(true);
            entity.Property(r => r.FechaCreacionUtc).HasColumnName("fecha_creacion_utc");

            entity.HasIndex(r => r.Codigo).IsUnique().HasDatabaseName("ux_roles_codigo");
        });

        // 2. Configuración de USUARIO (usuarios)
        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.ToTable("usuarios");
            entity.HasKey(u => u.Id).HasName("pk_usuarios");
            entity.Property(u => u.Id).HasColumnName("id");
            entity.Property(u => u.Username).HasColumnName("username").HasMaxLength(60).IsRequired();
            entity.Property(u => u.Email).HasColumnName("email").HasMaxLength(150).IsRequired();
            entity.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
            entity.Property(u => u.Nombres).HasColumnName("nombres").HasMaxLength(100).IsRequired();
            entity.Property(u => u.Apellidos).HasColumnName("apellidos").HasMaxLength(100).IsRequired();
            entity.Property(u => u.DocumentoIdentidadCifrado).HasColumnName("documento_identidad_cifrado").HasMaxLength(255).IsRequired();
            entity.Property(u => u.RolId).HasColumnName("rol_id").IsRequired();
            entity.Property(u => u.Activo).HasColumnName("activo").HasDefaultValue(true);
            entity.Property(u => u.EliminadoLogico).HasColumnName("eliminado_logico").HasDefaultValue(false);
            entity.Property(u => u.FechaEliminacionUtc).HasColumnName("fecha_eliminacion_utc");
            entity.Property(u => u.MotivoEliminacion).HasColumnName("motivo_eliminacion").HasMaxLength(250);
            entity.Property(u => u.UltimoAccesoUtc).HasColumnName("ultimo_acceso_utc");
            entity.Property(u => u.FechaCreacionUtc).HasColumnName("fecha_creacion_utc");

            entity.HasIndex(u => u.Username).IsUnique().HasDatabaseName("ux_usuarios_username");
            entity.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ux_usuarios_email");
            entity.HasIndex(u => new { u.RolId, u.Activo, u.EliminadoLogico }).HasDatabaseName("ix_usuarios_rol_estado");

            entity.HasOne(u => u.Rol)
                .WithMany(r => r.Usuarios)
                .HasForeignKey(u => u.RolId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_usuarios_roles");
        });

        // 3. Configuración de PERFIL (perfiles)
        modelBuilder.Entity<Perfil>(entity =>
        {
            entity.ToTable("perfiles");
            entity.HasKey(p => p.Id).HasName("pk_perfiles");
            entity.Property(p => p.Id).HasColumnName("id");
            entity.Property(p => p.UsuarioId).HasColumnName("usuario_id").IsRequired();
            entity.Property(p => p.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(300);
            entity.Property(p => p.BiografiaCorta).HasColumnName("biografia_corta").HasMaxLength(400);
            entity.Property(p => p.TelefonoContacto).HasColumnName("telefono_contacto").HasMaxLength(30);
            entity.Property(p => p.DireccionCifrada).HasColumnName("direccion_cifrada").HasMaxLength(300);
            entity.Property(p => p.PreferenciaAltoContraste).HasColumnName("preferencia_alto_contraste").HasDefaultValue(false);
            entity.Property(p => p.PreferenciaLecturaVoz).HasColumnName("preferencia_lectura_voz").HasDefaultValue(true);
            entity.Property(p => p.ConsentimientoHabeasDataFirmado).HasColumnName("consentimiento_habeas_data_firmado").HasDefaultValue(true);
            entity.Property(p => p.FechaConsentimientoUtc).HasColumnName("fecha_consentimiento_utc");
            entity.Property(p => p.HashDocumentoConsentimiento).HasColumnName("hash_documento_consentimiento").HasMaxLength(64);
            entity.Property(p => p.FechaActualizacionUtc).HasColumnName("fecha_actualizacion_utc");

            entity.HasIndex(p => p.UsuarioId).IsUnique().HasDatabaseName("ux_perfiles_usuario_id");

            entity.HasOne(p => p.Usuario)
                .WithOne(u => u.Perfil)
                .HasForeignKey<Perfil>(p => p.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_perfiles_usuarios");
        });

        // 4. Configuración de GRADO (grados)
        modelBuilder.Entity<Grado>(entity =>
        {
            entity.ToTable("grados");
            entity.HasKey(g => g.Id).HasName("pk_grados");
            entity.Property(g => g.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(g => g.NumeroNivel).HasColumnName("numero_nivel").IsRequired();
            entity.Property(g => g.Nombre).HasColumnName("nombre").HasMaxLength(60).IsRequired();
            entity.Property(g => g.DescripcionEtapa).HasColumnName("descripcion_etapa").HasMaxLength(250).IsRequired();
            entity.Property(g => g.EdadRecomendadaMin).HasColumnName("edad_recomendada_min");
            entity.Property(g => g.EdadRecomendadaMax).HasColumnName("edad_recomendada_max");
            entity.Property(g => g.Activo).HasColumnName("activo").HasDefaultValue(true);

            entity.HasIndex(g => g.NumeroNivel).IsUnique().HasDatabaseName("ux_grados_numero_nivel");
        });

        // 5. Configuración de ALUMNO (alumnos)
        modelBuilder.Entity<Alumno>(entity =>
        {
            entity.ToTable("alumnos");
            entity.HasKey(a => a.Id).HasName("pk_alumnos");
            entity.Property(a => a.Id).HasColumnName("id");
            entity.Property(a => a.UsuarioId).HasColumnName("usuario_id").IsRequired();
            entity.Property(a => a.GradoId).HasColumnName("grado_id").IsRequired();
            entity.Property(a => a.CodigoSeudonimo).HasColumnName("codigo_seudonimo").HasMaxLength(40).IsRequired();
            entity.Property(a => a.FechaNacimiento).HasColumnName("fecha_nacimiento").IsRequired();
            entity.Property(a => a.NombreAcudiente).HasColumnName("nombre_acudiente").HasMaxLength(150).IsRequired();
            entity.Property(a => a.CorreoAcudiente).HasColumnName("correo_acudiente").HasMaxLength(150).IsRequired();
            entity.Property(a => a.TelefonoAcudiente).HasColumnName("telefono_acudiente").HasMaxLength(30).IsRequired();
            entity.Property(a => a.AutorizacionLey1581Activa).HasColumnName("autorizacion_ley1581_activa").HasDefaultValue(true);
            entity.Property(a => a.PuntosGamificacion).HasColumnName("puntos_gamificacion").HasDefaultValue(0);
            entity.Property(a => a.RachaDiasActivos).HasColumnName("racha_dias_activos").HasDefaultValue(1);
            entity.Property(a => a.Activo).HasColumnName("activo").HasDefaultValue(true);
            entity.Property(a => a.FechaRegistroUtc).HasColumnName("fecha_registro_utc");

            entity.HasIndex(a => a.UsuarioId).IsUnique().HasDatabaseName("ux_alumnos_usuario_id");
            entity.HasIndex(a => a.CodigoSeudonimo).IsUnique().HasDatabaseName("ux_alumnos_codigo_seudonimo");
            entity.HasIndex(a => a.GradoId).HasDatabaseName("ix_alumnos_grado_id");

            entity.HasOne(a => a.Usuario)
                .WithOne(u => u.AlumnoPerfil)
                .HasForeignKey<Alumno>(a => a.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_alumnos_usuarios");

            entity.HasOne(a => a.Grado)
                .WithMany(g => g.Alumnos)
                .HasForeignKey(a => a.GradoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_alumnos_grados");
        });

        // 6. Configuración de CURSO (cursos)
        modelBuilder.Entity<Curso>(entity =>
        {
            entity.ToTable("cursos");
            entity.HasKey(c => c.Id).HasName("pk_cursos");
            entity.Property(c => c.Id).HasColumnName("id");
            entity.Property(c => c.CodigoCurso).HasColumnName("codigo_curso").HasMaxLength(30).IsRequired();
            entity.Property(c => c.Nombre).HasColumnName("nombre").HasMaxLength(150).IsRequired();
            entity.Property(c => c.Descripcion).HasColumnName("descripcion").HasMaxLength(600).IsRequired();
            entity.Property(c => c.AreaConocimiento).HasColumnName("area_conocimiento").HasMaxLength(100).IsRequired();
            entity.Property(c => c.GradoId).HasColumnName("grado_id").IsRequired();
            entity.Property(c => c.DocenteId).HasColumnName("docente_id").IsRequired();
            entity.Property(c => c.AnioLectivo).HasColumnName("anio_lectivo").IsRequired();
            entity.Property(c => c.ColorTemaHex).HasColumnName("color_tema_hex").HasMaxLength(12);
            entity.Property(c => c.IconoEmoji).HasColumnName("icono_emoji").HasMaxLength(16);
            entity.Property(c => c.Activo).HasColumnName("activo").HasDefaultValue(true);
            entity.Property(c => c.FechaCreacionUtc).HasColumnName("fecha_creacion_utc");

            entity.HasIndex(c => c.CodigoCurso).IsUnique().HasDatabaseName("ux_cursos_codigo_curso");
            entity.HasIndex(c => new { c.DocenteId, c.Activo }).HasDatabaseName("ix_cursos_docente_activo");

            entity.HasOne(c => c.Grado)
                .WithMany(g => g.Cursos)
                .HasForeignKey(c => c.GradoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cursos_grados");

            entity.HasOne(c => c.Docente)
                .WithMany(u => u.CursosDictados)
                .HasForeignKey(c => c.DocenteId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cursos_docentes");
        });

        // 7. Configuración de INSCRIPCION (inscripciones)
        modelBuilder.Entity<Inscripcion>(entity =>
        {
            entity.ToTable("inscripciones");
            entity.HasKey(i => i.Id).HasName("pk_inscripciones");
            entity.Property(i => i.Id).HasColumnName("id");
            entity.Property(i => i.CursoId).HasColumnName("curso_id").IsRequired();
            entity.Property(i => i.AlumnoId).HasColumnName("alumno_id").IsRequired();
            entity.Property(i => i.Estado).HasColumnName("estado").HasMaxLength(25).IsRequired();
            entity.Property(i => i.PromedioAcumulado).HasColumnName("promedio_acumulado").HasPrecision(4, 2);
            entity.Property(i => i.PorcentajeAsistencia).HasColumnName("porcentaje_asistencia").HasPrecision(5, 2);
            entity.Property(i => i.FechaInscripcionUtc).HasColumnName("fecha_inscripcion_utc");
            entity.Property(i => i.FechaBajaUtc).HasColumnName("fecha_baja_utc");

            entity.HasIndex(i => new { i.CursoId, i.AlumnoId }).IsUnique().HasDatabaseName("ux_inscripciones_curso_alumno");

            entity.HasOne(i => i.Curso)
                .WithMany(c => c.Inscripciones)
                .HasForeignKey(i => i.CursoId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_inscripciones_cursos");

            entity.HasOne(i => i.Alumno)
                .WithMany(a => a.Inscripciones)
                .HasForeignKey(i => i.AlumnoId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_inscripciones_alumnos");
        });

        // 8. Configuración de MATERIAL (materiales)
        modelBuilder.Entity<Material>(entity =>
        {
            entity.ToTable("materiales");
            entity.HasKey(m => m.Id).HasName("pk_materiales");
            entity.Property(m => m.Id).HasColumnName("id");
            entity.Property(m => m.CursoId).HasColumnName("curso_id").IsRequired();
            entity.Property(m => m.SubidoPorDocenteId).HasColumnName("subido_por_docente_id").IsRequired();
            entity.Property(m => m.Titulo).HasColumnName("titulo").HasMaxLength(160).IsRequired();
            entity.Property(m => m.Descripcion).HasColumnName("descripcion").HasMaxLength(800).IsRequired();
            entity.Property(m => m.TipoRecurso).HasColumnName("tipo_recurso").HasMaxLength(40).IsRequired();
            entity.Property(m => m.UrlRecurso).HasColumnName("url_recurso").HasMaxLength(500).IsRequired();
            entity.Property(m => m.TamanoBytes).HasColumnName("tamano_bytes");
            entity.Property(m => m.HashSha256).HasColumnName("hash_sha256").HasMaxLength(64);
            entity.Property(m => m.OrdenVisualizacion).HasColumnName("orden_visualizacion");
            entity.Property(m => m.VisibleAlumnos).HasColumnName("visible_alumnos").HasDefaultValue(true);
            entity.Property(m => m.FechaPublicacionUtc).HasColumnName("fecha_publicacion_utc");

            entity.HasOne(m => m.Curso)
                .WithMany(c => c.Materiales)
                .HasForeignKey(m => m.CursoId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_materiales_cursos");

            entity.HasOne(m => m.Docente)
                .WithMany()
                .HasForeignKey(m => m.SubidoPorDocenteId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_materiales_docentes");
        });

        // 9. Configuración de TAREA (tareas)
        modelBuilder.Entity<Tarea>(entity =>
        {
            entity.ToTable("tareas");
            entity.HasKey(t => t.Id).HasName("pk_tareas");
            entity.Property(t => t.Id).HasColumnName("id");
            entity.Property(t => t.CursoId).HasColumnName("curso_id").IsRequired();
            entity.Property(t => t.CreadoPorDocenteId).HasColumnName("creado_por_docente_id").IsRequired();
            entity.Property(t => t.Titulo).HasColumnName("titulo").HasMaxLength(160).IsRequired();
            entity.Property(t => t.InstruccionesPedagogicas).HasColumnName("instrucciones_pedagogicas").HasMaxLength(2000).IsRequired();
            entity.Property(t => t.PuntosRecompensa).HasColumnName("puntos_recompensa").HasDefaultValue(50);
            entity.Property(t => t.NotaMaxima).HasColumnName("nota_maxima").HasPrecision(4, 2).HasDefaultValue(5.00m);
            entity.Property(t => t.FechaPublicacionUtc).HasColumnName("fecha_publicacion_utc");
            entity.Property(t => t.FechaLimiteUtc).HasColumnName("fecha_limite_utc").IsRequired();
            entity.Property(t => t.PermiteEntregaTardia).HasColumnName("permite_entrega_tardia").HasDefaultValue(true);
            entity.Property(t => t.FormatosPermitidos).HasColumnName("formatos_permitidos").HasMaxLength(80);
            entity.Property(t => t.Activa).HasColumnName("activa").HasDefaultValue(true);

            entity.HasIndex(t => new { t.CursoId, t.Activa }).HasDatabaseName("ix_tareas_curso_activa");

            entity.HasOne(t => t.Curso)
                .WithMany(c => c.Tareas)
                .HasForeignKey(t => t.CursoId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_tareas_cursos");

            entity.HasOne(t => t.Docente)
                .WithMany()
                .HasForeignKey(t => t.CreadoPorDocenteId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_tareas_docentes");
        });

        // 10. Configuración de ENTREGA (entregas)
        modelBuilder.Entity<Entrega>(entity =>
        {
            entity.ToTable("entregas");
            entity.HasKey(e => e.Id).HasName("pk_entregas");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TareaId).HasColumnName("tarea_id").IsRequired();
            entity.Property(e => e.AlumnoId).HasColumnName("alumno_id").IsRequired();
            entity.Property(e => e.EstadoKanban).HasColumnName("estado_kanban").HasMaxLength(30).IsRequired();
            entity.Property(e => e.ComentarioAlumno).HasColumnName("comentario_alumno").HasMaxLength(1000);
            entity.Property(e => e.CalificacionNumerica).HasColumnName("calificacion_numerica").HasPrecision(4, 2);
            entity.Property(e => e.EscalaCualitativaMen).HasColumnName("escala_cualitativa_men").HasMaxLength(30);
            entity.Property(e => e.RetroalimentacionDocente).HasColumnName("retroalimentacion_docente").HasMaxLength(1000);
            entity.Property(e => e.CalificadoPorDocenteId).HasColumnName("calificado_por_docente_id");
            entity.Property(e => e.EsEntregaTardia).HasColumnName("es_entrega_tardia").HasDefaultValue(false);
            entity.Property(e => e.FechaEnvioUtc).HasColumnName("fecha_envio_utc");
            entity.Property(e => e.FechaCalificacionUtc).HasColumnName("fecha_calificacion_utc");

            entity.HasIndex(e => new { e.TareaId, e.AlumnoId }).IsUnique().HasDatabaseName("ux_entregas_tarea_alumno");
            entity.HasIndex(e => new { e.AlumnoId, e.EstadoKanban }).HasDatabaseName("ix_entregas_alumno_estado");

            entity.HasOne(e => e.Tarea)
                .WithMany(t => t.Entregas)
                .HasForeignKey(e => e.TareaId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_entregas_tareas");

            entity.HasOne(e => e.Alumno)
                .WithMany(a => a.Entregas)
                .HasForeignKey(e => e.AlumnoId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_entregas_alumnos");

            entity.HasOne(e => e.DocenteCalificador)
                .WithMany()
                .HasForeignKey(e => e.CalificadoPorDocenteId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_entregas_docente_calificador");
        });

        // 11. Configuración de ARCHIVO_ENTREGA (archivos_entrega)
        modelBuilder.Entity<ArchivoEntrega>(entity =>
        {
            entity.ToTable("archivos_entrega");
            entity.HasKey(ae => ae.Id).HasName("pk_archivos_entrega");
            entity.Property(ae => ae.Id).HasColumnName("id");
            entity.Property(ae => ae.EntregaId).HasColumnName("entrega_id").IsRequired();
            entity.Property(ae => ae.NombreOriginalSanitizado).HasColumnName("nombre_original_sanitizado").HasMaxLength(180).IsRequired();
            entity.Property(ae => ae.NombreAlmacenamientoGuid).HasColumnName("nombre_almacenamiento_guid").HasMaxLength(80).IsRequired();
            entity.Property(ae => ae.RutaRelativaStorage).HasColumnName("ruta_relativa_storage").HasMaxLength(400).IsRequired();
            entity.Property(ae => ae.MimeTypeVerificado).HasColumnName("mime_type_verificado").HasMaxLength(60).IsRequired();
            entity.Property(ae => ae.Extension).HasColumnName("extension").HasMaxLength(12).IsRequired();
            entity.Property(ae => ae.TamanoBytes).HasColumnName("tamano_bytes").IsRequired();
            entity.Property(ae => ae.ChecksumSha256).HasColumnName("checksum_sha256").HasMaxLength(64).IsRequired();
            entity.Property(ae => ae.MagicBytesVerificados).HasColumnName("magic_bytes_verificados").HasDefaultValue(true);
            entity.Property(ae => ae.FechaSubidaUtc).HasColumnName("fecha_subida_utc");

            entity.HasIndex(ae => ae.NombreAlmacenamientoGuid).IsUnique().HasDatabaseName("ux_archivos_entrega_guid");

            entity.HasOne(ae => ae.Entrega)
                .WithMany(e => e.Archivos)
                .HasForeignKey(ae => ae.EntregaId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_archivos_entrega_entregas");
        });

        // 12. Configuración de SEMAFORO_STATUS (semaforo_estados)
        modelBuilder.Entity<SemaforoStatus>(entity =>
        {
            entity.ToTable("semaforo_estados");
            entity.HasKey(s => s.Id).HasName("pk_semaforo_estados");
            entity.Property(s => s.Id).HasColumnName("id");
            entity.Property(s => s.AlumnoId).HasColumnName("alumno_id").IsRequired();
            entity.Property(s => s.CursoId).HasColumnName("curso_id").IsRequired();
            entity.Property(s => s.Color).HasColumnName("color").HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(s => s.PorcentajeCumplimiento).HasColumnName("porcentaje_cumplimiento").HasPrecision(5, 2);
            entity.Property(s => s.TareasEntregadas).HasColumnName("tareas_entregadas");
            entity.Property(s => s.TareasPendientes).HasColumnName("tareas_pendientes");
            entity.Property(s => s.TareasVencidas).HasColumnName("tareas_vencidas");
            entity.Property(s => s.PromedioCurso).HasColumnName("promedio_curso").HasPrecision(4, 2);
            entity.Property(s => s.ObservacionDocente).HasColumnName("observacion_docente").HasMaxLength(500);
            entity.Property(s => s.RequiereAtencionAcudiente).HasColumnName("requiere_atencion_acudiente").HasDefaultValue(false);
            entity.Property(s => s.FechaCalculoUtc).HasColumnName("fecha_calculo_utc");

            entity.HasIndex(s => new { s.CursoId, s.AlumnoId }).IsUnique().HasDatabaseName("ux_semaforo_curso_alumno");

            entity.HasOne(s => s.Alumno)
                .WithMany(a => a.EstadosSemaforo)
                .HasForeignKey(s => s.AlumnoId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_semaforo_alumnos");

            entity.HasOne(s => s.Curso)
                .WithMany(c => c.EstadosSemaforo)
                .HasForeignKey(s => s.CursoId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_semaforo_cursos");
        });
    }
}
