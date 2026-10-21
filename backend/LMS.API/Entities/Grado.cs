using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LMS.API.Entities;

/// <summary>
/// Grados de Educación Básica Primaria (1° a 5° de Primaria según estructura MEN Colombia).
/// </summary>
[Table("grados")]
public class Grado
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Range(1, 5)]
    [Column("numero_nivel")]
    public int NumeroNivel { get; set; }

    [Required]
    [MaxLength(60)]
    [Column("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    [MaxLength(250)]
    [Column("descripcion_etapa")]
    public string DescripcionEtapa { get; set; } = string.Empty;

    [Range(5, 12)]
    [Column("edad_recomendada_min")]
    public int EdadRecomendadaMin { get; set; }

    [Range(6, 13)]
    [Column("edad_recomendada_max")]
    public int EdadRecomendadaMax { get; set; }

    [Required]
    [Column("activo")]
    public bool Activo { get; set; } = true;

    // Propiedades de navegación
    public virtual ICollection<Alumno> Alumnos { get; set; } = new HashSet<Alumno>();
    public virtual ICollection<Curso> Cursos { get; set; } = new HashSet<Curso>();
}
