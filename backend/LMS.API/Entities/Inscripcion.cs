using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LMS.API.Entities;

/// <summary>
/// Matrícula o inscripción académica de un Alumno en un Curso específico.
/// </summary>
[Table("inscripciones")]
public class Inscripcion
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("curso_id")]
    public Guid CursoId { get; set; }

    [Required]
    [Column("alumno_id")]
    public Guid AlumnoId { get; set; }

    [Required]
    [MaxLength(25)]
    [Column("estado")]
    public string Estado { get; set; } = "ACTIVA"; // ACTIVA, RETIRADA, FINALIZADA

    [Range(0, 5)]
    [Column("promedio_acumulado", TypeName = "numeric(4,2)")]
    public decimal PromedioAcumulado { get; set; } = 5.00m;

    [Range(0, 100)]
    [Column("porcentaje_asistencia", TypeName = "numeric(5,2)")]
    public decimal PorcentajeAsistencia { get; set; } = 100.00m;

    [Required]
    [Column("fecha_inscripcion_utc")]
    public DateTime FechaInscripcionUtc { get; set; } = DateTime.UtcNow;

    [Column("fecha_baja_utc")]
    public DateTime? FechaBajaUtc { get; set; }

    // Propiedades de navegación
    public virtual Curso Curso { get; set; } = null!;
    public virtual Alumno Alumno { get; set; } = null!;
}
