using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LMS.API.Entities;

/// <summary>
/// Evidencia física adjunta a una Entrega, validada por firma binaria (Magic Bytes) y renombrada con GUID.
/// </summary>
[Table("archivos_entrega")]
public class ArchivoEntrega
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [Column("entrega_id")]
    public Guid EntregaId { get; set; }

    [Required]
    [MaxLength(180)]
    [Column("nombre_original_sanitizado")]
    public string NombreOriginalSanitizado { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    [Column("nombre_almacenamiento_guid")]
    public string NombreAlmacenamientoGuid { get; set; } = string.Empty;

    [Required]
    [MaxLength(400)]
    [Column("ruta_relativa_storage")]
    public string RutaRelativaStorage { get; set; } = string.Empty;

    [Required]
    [MaxLength(60)]
    [Column("mime_type_verificado")]
    public string MimeTypeVerificado { get; set; } = string.Empty;

    [Required]
    [MaxLength(12)]
    [Column("extension")]
    public string Extension { get; set; } = string.Empty;

    [Range(1, 10485760)]
    [Column("tamano_bytes")]
    public long TamanoBytes { get; set; }

    [Required]
    [MaxLength(64)]
    [Column("checksum_sha256")]
    public string ChecksumSha256 { get; set; } = string.Empty;

    [Required]
    [Column("magic_bytes_verificados")]
    public bool MagicBytesVerificados { get; set; } = true;

    [Required]
    [Column("fecha_subida_utc")]
    public DateTime FechaSubidaUtc { get; set; } = DateTime.UtcNow;

    // Propiedades de navegación
    public virtual Entrega Entrega { get; set; } = null!;
}
