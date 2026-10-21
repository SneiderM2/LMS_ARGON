using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LMS.API.Entities;

/// <summary>
/// Material pedagógico o recurso multimedia publicado por un docente dentro de un Curso.
/// </summary>
[Table("materiales")]
public class Material
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("curso_id")]
    public Guid CursoId { get; set; }

    [Required]
    [Column("subido_por_docente_id")]
    public Guid SubidoPorDocenteId { get; set; }

    [Required]
    [MaxLength(160)]
    [Column("titulo")]
    public string Titulo { get; set; } = string.Empty;

    [Required]
    [MaxLength(800)]
    [Column("descripcion")]
    public string Descripcion { get; set; } = string.Empty;

    [Required]
    [MaxLength(40)]
    [Column("tipo_recurso")]
    public string TipoRecurso { get; set; } = "PDF"; // PDF, IMAGEN, AUDIO, GUIA_INTERACTIVA

    [Required]
    [MaxLength(500)]
    [Column("url_recurso")]
    public string UrlRecurso { get; set; } = string.Empty;

    [Column("tamano_bytes")]
    public long? TamanoBytes { get; set; }

    [MaxLength(64)]
    [Column("hash_sha256")]
    public string? HashSha256 { get; set; }

    [Range(1, 500)]
    [Column("orden_visualizacion")]
    public int OrdenVisualizacion { get; set; } = 1;

    [Required]
    [Column("visible_alumnos")]
    public bool VisibleAlumnos { get; set; } = true;

    [Required]
    [Column("fecha_publicacion_utc")]
    public DateTime FechaPublicacionUtc { get; set; } = DateTime.UtcNow;

    // Propiedades de navegación
    public virtual Curso Curso { get; set; } = null!;
    public virtual Usuario Docente { get; set; } = null!;
}
