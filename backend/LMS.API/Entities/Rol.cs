using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LMS.API.Entities;

/// <summary>
/// Catálogo de roles RBAC del sistema SciKids LMS (ADMINISTRADOR, DOCENTE, ALUMNO, ACUDIENTE).
/// </summary>
[Table("roles")]
public class Rol
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [MaxLength(40)]
    [Column("codigo")]
    public string Codigo { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    [Column("nombre")]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    [MaxLength(250)]
    [Column("descripcion")]
    public string Descripcion { get; set; } = string.Empty;

    [Required]
    [Column("activo")]
    public bool Activo { get; set; } = true;

    [Required]
    [Column("fecha_creacion_utc")]
    public DateTime FechaCreacionUtc { get; set; } = DateTime.UtcNow;

    // Propiedades de navegación
    public virtual ICollection<Usuario> Usuarios { get; set; } = new HashSet<Usuario>();
}
