using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LMS.API.Entities;

/// <summary>
/// Entrega o envío de una Tarea por parte de un Alumno, incluyendo su estado en el Tablero Kanban.
/// </summary>
[Table("entregas")]
public class Entrega
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("tarea_id")]
    public Guid TareaId { get; set; }

    [Required]
    [Column("alumno_id")]
    public Guid AlumnoId { get; set; }

    [Required]
    [MaxLength(30)]
    [Column("estado_kanban")]
    public string EstadoKanban { get; set; } = "PENDIENTE"; // PENDIENTE, EN_PROGRESO, ENTREGADA, CALIFICADA

    [MaxLength(1000)]
    [Column("comentario_alumno")]
    public string? ComentarioAlumno { get; set; }

    [Range(0, 5)]
    [Column("calificacion_numerica", TypeName = "numeric(4,2)")]
    public decimal? CalificacionNumerica { get; set; }

    [MaxLength(30)]
    [Column("escala_cualitativa_men")]
    public string? EscalaCualitativaMen { get; set; } // SUPERIOR, ALTO, BASICO, BAJO

    [MaxLength(1000)]
    [Column("retroalimentacion_docente")]
    public string? RetroalimentacionDocente { get; set; }

    [Column("calificado_por_docente_id")]
    public Guid? CalificadoPorDocenteId { get; set; }

    [Required]
    [Column("es_entrega_tardia")]
    public bool EsEntregaTardia { get; set; }

    [Required]
    [Column("fecha_envio_utc")]
    public DateTime FechaEnvioUtc { get; set; } = DateTime.UtcNow;

    [Column("fecha_calificacion_utc")]
    public DateTime? FechaCalificacionUtc { get; set; }

    // Propiedades de navegación
    public virtual Tarea Tarea { get; set; } = null!;
    public virtual Alumno Alumno { get; set; } = null!;
    public virtual Usuario? DocenteCalificador { get; set; }
    public virtual ICollection<ArchivoEntrega> Archivos { get; set; } = new HashSet<ArchivoEntrega>();
}
