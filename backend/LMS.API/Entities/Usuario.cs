using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LMS.API.Entities;

/// <summary>
/// Entidad principal de identidad de usuario en SciKids LMS.
/// Soporta borrado lógico (Soft Delete), trazabilidad de acceso y cifrado de PII (Ley 1581 de 2012).
/// </summary>
[Table("usuarios")]
public class Usuario
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(60)]
    [Column("username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(150)]
    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    [Column("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Column("nombres")]
    public string Nombres { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Column("apellidos")]
    public string Apellidos { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    [Column("documento_identidad_cifrado")]
    public string DocumentoIdentidadCifrado { get; set; } = string.Empty;

    [Required]
    [Column("rol_id")]
    public int RolId { get; set; }

    [Required]
    [Column("activo")]
    public bool Activo { get; set; } = true;

    [Required]
    [Column("eliminado_logico")]
    public bool EliminadoLogico { get; set; }

    [Column("fecha_eliminacion_utc")]
    public DateTime? FechaEliminacionUtc { get; set; }

    [MaxLength(250)]
    [Column("motivo_eliminacion")]
    public string? MotivoEliminacion { get; set; }

    [Column("ultimo_acceso_utc")]
    public DateTime? UltimoAccesoUtc { get; set; }

    [Required]
    [Column("fecha_creacion_utc")]
    public DateTime FechaCreacionUtc { get; set; } = DateTime.UtcNow;

    // Propiedades de navegación
    public virtual Rol Rol { get; set; } = null!;
    public virtual Perfil? Perfil { get; set; }
    public virtual Alumno? AlumnoPerfil { get; set; }
    public virtual ICollection<Curso> CursosDictados { get; set; } = new HashSet<Curso>();
}
