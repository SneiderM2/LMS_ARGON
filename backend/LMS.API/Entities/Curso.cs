using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LMS.API.Entities;

/// <summary>
/// Entidad de Curso o Aula Virtual asignada a un Grado de Primaria y un Docente titular.
/// </summary>
[Table("cursos")]
public class Curso
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(30)]
    [Column("codigo_curso")]
    public string CodigoCurso { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    [Column("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    [MaxLength(600)]
    [Column("descripcion")]
    public string Descripcion { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Column("area_conocimiento")]
    public string AreaConocimiento { get; set; } = string.Empty;

    [Required]
    [Column("grado_id")]
    public int GradoId { get; set; }

    [Required]
    [Column("docente_id")]
    public Guid DocenteId { get; set; }

    [Required]
    [Range(2020, 2100)]
    [Column("anio_lectivo")]
    public int AnioLectivo { get; set; } = DateTime.UtcNow.Year;

    [Required]
    [MaxLength(12)]
    [Column("color_tema_hex")]
    public string ColorTemaHex { get; set; } = "#0284C7";

    [Required]
    [MaxLength(16)]
    [Column("icono_emoji")]
    public string IconoEmoji { get; set; } = "🔬";

    [Required]
    [Column("activo")]
    public bool Activo { get; set; } = true;

    [Required]
    [Column("fecha_creacion_utc")]
    public DateTime FechaCreacionUtc { get; set; } = DateTime.UtcNow;

    // Propiedades de navegación
    public virtual Grado Grado { get; set; } = null!;
    public virtual Usuario Docente { get; set; } = null!;
    public virtual ICollection<Inscripcion> Inscripciones { get; set; } = new HashSet<Inscripcion>();
    public virtual ICollection<Tarea> Tareas { get; set; } = new HashSet<Tarea>();
    public virtual ICollection<Material> Materiales { get; set; } = new HashSet<Material>();
    public virtual ICollection<SemaforoStatus> EstadosSemaforo { get; set; } = new HashSet<SemaforoStatus>();
}
