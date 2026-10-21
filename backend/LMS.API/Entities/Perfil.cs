using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LMS.API.Entities;

/// <summary>
/// Perfil extendido de usuario con configuración de accesibilidad (NTC 5854 / WCAG 2.1 AA)
/// y metadatos de consentimiento Habeas Data (Ley 1581 de 2012).
/// </summary>
[Table("perfiles")]
public class Perfil
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("usuario_id")]
    public Guid UsuarioId { get; set; }

    [MaxLength(300)]
    [Column("avatar_url")]
    public string? AvatarUrl { get; set; }

    [MaxLength(400)]
    [Column("biografia_corta")]
    public string? BiografiaCorta { get; set; }

    [Phone]
    [MaxLength(30)]
    [Column("telefono_contacto")]
    public string? TelefonoContacto { get; set; }

    [MaxLength(300)]
    [Column("direccion_cifrada")]
    public string? DireccionCifrada { get; set; }

    [Required]
    [Column("preferencia_alto_contraste")]
    public bool PreferenciaAltoContraste { get; set; }

    [Required]
    [Column("preferencia_lectura_voz")]
    public bool PreferenciaLecturaVoz { get; set; } = true;

    [Required]
    [Column("consentimiento_habeas_data_firmado")]
    public bool ConsentimientoHabeasDataFirmado { get; set; } = true;

    [Column("fecha_consentimiento_utc")]
    public DateTime? FechaConsentimientoUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(64)]
    [Column("hash_documento_consentimiento")]
    public string? HashDocumentoConsentimiento { get; set; }

    [Required]
    [Column("fecha_actualizacion_utc")]
    public DateTime FechaActualizacionUtc { get; set; } = DateTime.UtcNow;

    // Propiedades de navegación
    public virtual Usuario Usuario { get; set; } = null!;
}
