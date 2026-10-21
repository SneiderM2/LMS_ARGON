using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LMS.API.Entities;

/// <summary>
/// Niveles de alerta temprana del Semáforo Escolar Pedagógico (Lineamientos MEN Primaria).
/// </summary>
public enum SemaforoColor
{
    VERDE = 1,
    AMARILLO = 2,
    ROJO = 3
}

/// <summary>
/// Entidad que registra el estado del semáforo de seguimiento académico de un alumno en un curso.
/// </summary>
[Table("semaforo_estados")]
public class SemaforoStatus
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("alumno_id")]
    public Guid AlumnoId { get; set; }

    [Required]
    [Column("curso_id")]
    public Guid CursoId { get; set; }

    [Required]
    [Column("color")]
    public SemaforoColor Color { get; set; } = SemaforoColor.VERDE;

    [Range(0, 100)]
    [Column("porcentaje_cumplimiento", TypeName = "numeric(5,2)")]
    public decimal PorcentajeCumplimiento { get; set; } = 100.00m;

    [Range(0, 500)]
    [Column("tareas_entregadas")]
    public int TareasEntregadas { get; set; }

    [Range(0, 500)]
    [Column("tareas_pendientes")]
    public int TareasPendientes { get; set; }

    [Range(0, 500)]
    [Column("tareas_vencidas")]
    public int TareasVencidas { get; set; }

    [Range(0, 5)]
    [Column("promedio_curso", TypeName = "numeric(4,2)")]
    public decimal PromedioCurso { get; set; } = 5.00m;

    [MaxLength(500)]
    [Column("observacion_docente")]
    public string? ObservacionDocente { get; set; }

    [Required]
    [Column("requiere_atencion_acudiente")]
    public bool RequiereAtencionAcudiente { get; set; }

    [Required]
    [Column("fecha_calculo_utc")]
    public DateTime FechaCalculoUtc { get; set; } = DateTime.UtcNow;

    // Propiedades de navegación
    public virtual Alumno Alumno { get; set; } = null!;
    public virtual Curso Curso { get; set; } = null!;
}
