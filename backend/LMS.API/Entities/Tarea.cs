using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LMS.API.Entities;

/// <summary>
/// Misión o Tarea académica asignada a un Curso de Primaria con recompensas de gamificación.
/// </summary>
[Table("tareas")]
public class Tarea
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("curso_id")]
    public Guid CursoId { get; set; }

    [Required]
    [Column("creado_por_docente_id")]
    public Guid CreadoPorDocenteId { get; set; }

    [Required]
    [MaxLength(160)]
    [Column("titulo")]
    public string Titulo { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    [Column("instrucciones_pedagogicas")]
    public string InstruccionesPedagogicas { get; set; } = string.Empty;

    [Range(0, 1000)]
    [Column("puntos_recompensa")]
    public int PuntosRecompensa { get; set; } = 50;

    [Range(1, 5)]
    [Column("nota_maxima", TypeName = "numeric(4,2)")]
    public decimal NotaMaxima { get; set; } = 5.00m;

    [Required]
    [Column("fecha_publicacion_utc")]
    public DateTime FechaPublicacionUtc { get; set; } = DateTime.UtcNow;

    [Required]
    [Column("fecha_limite_utc")]
    public DateTime FechaLimiteUtc { get; set; }

    [Required]
    [Column("permite_entrega_tardia")]
    public bool PermiteEntregaTardia { get; set; } = true;

    [Required]
    [MaxLength(80)]
    [Column("formatos_permitidos")]
    public string FormatosPermitidos { get; set; } = ".pdf,.png,.jpg";

    [Required]
    [Column("activa")]
    public bool Activa { get; set; } = true;

    // Propiedades de navegación
    public virtual Curso Curso { get; set; } = null!;
    public virtual Usuario Docente { get; set; } = null!;
    public virtual ICollection<Entrega> Entregas { get; set; } = new HashSet<Entrega>();
}
