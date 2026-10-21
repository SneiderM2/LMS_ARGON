using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LMS.API.Entities;

/// <summary>
/// Entidad académica de Estudiante de Básica Primaria (6-11 años).
/// Implementa seudonimización (Art. 47 Ley 1098 de 2006) y vinculación obligatoria con Acudiente.
/// </summary>
[Table("alumnos")]
public class Alumno
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("usuario_id")]
    public Guid UsuarioId { get; set; }

    [Required]
    [Column("grado_id")]
    public int GradoId { get; set; }

    [Required]
    [MaxLength(40)]
    [Column("codigo_seudonimo")]
    public string CodigoSeudonimo { get; set; } = string.Empty;

    [Required]
    [Column("fecha_nacimiento")]
    public DateOnly FechaNacimiento { get; set; }

    [Required]
    [MaxLength(150)]
    [Column("nombre_acudiente")]
    public string NombreAcudiente { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(150)]
    [Column("correo_acudiente")]
    public string CorreoAcudiente { get; set; } = string.Empty;

    [Required]
    [Phone]
    [MaxLength(30)]
    [Column("telefono_acudiente")]
    public string TelefonoAcudiente { get; set; } = string.Empty;

    [Required]
    [Column("autorizacion_ley1581_activa")]
    public bool AutorizacionLey1581Activa { get; set; } = true;

    [Range(0, 1000000)]
    [Column("puntos_gamificacion")]
    public int PuntosGamificacion { get; set; }

    [Range(0, 3650)]
    [Column("racha_dias_activos")]
    public int RachaDiasActivos { get; set; } = 1;

    [Required]
    [Column("activo")]
    public bool Activo { get; set; } = true;

    [Required]
    [Column("fecha_registro_utc")]
    public DateTime FechaRegistroUtc { get; set; } = DateTime.UtcNow;

    // Propiedades de navegación
    public virtual Usuario Usuario { get; set; } = null!;
    public virtual Grado Grado { get; set; } = null!;
    public virtual ICollection<Inscripcion> Inscripciones { get; set; } = new HashSet<Inscripcion>();
    public virtual ICollection<Entrega> Entregas { get; set; } = new HashSet<Entrega>();
    public virtual ICollection<SemaforoStatus> EstadosSemaforo { get; set; } = new HashSet<SemaforoStatus>();
}
