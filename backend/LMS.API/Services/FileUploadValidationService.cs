using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LMS.API.Services;

/// <summary>
/// Servicio DevSecOps de validación multicapa para subida de archivos/evidencias escolares en SciKids LMS.
/// Verifica firmas binarias (Magic Bytes), lista blanca de extensiones y renombrado GUID anti-Path Traversal.
/// </summary>
public sealed class FileUploadValidationService : IFileUploadValidationService
{
    /// <summary>
    /// Tamaño máximo permitido por archivo: 10 MB (10,485,760 bytes).
    /// </summary>
    public const long MaxAllowedFileSizeBytes = 10 * 1024 * 1024;

    private static readonly byte[] EicarTestSignature =
        "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"u8.ToArray();

    private static readonly byte[] DangerousPdfJsToken = "/JavaScript"u8.ToArray();
    private static readonly byte[] DangerousPdfLaunchToken = "/Launch"u8.ToArray();
    private static readonly byte[] DangerousPdfOpenActionToken = "/OpenAction"u8.ToArray();

    private static readonly Dictionary<string, (string CanonicalExtension, string MimeType, byte[][] MagicSignatures)>
        WhitelistedFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = (".pdf", "application/pdf", new[]
            {
                new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D } // %PDF-
            }),
            [".png"] = (".png", "image/png", new[]
            {
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } // Firma oficial PNG 8 bytes
            }),
            [".jpg"] = (".jpg", "image/jpeg", new[]
            {
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, // JFIF
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 }, // EXIF
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE2 }, // CanonICC
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE8 }, // SPIFF
                new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }  // Raw JPEG Quantization Table
            }),
            [".jpeg"] = (".jpg", "image/jpeg", new[]
            {
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 },
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 },
                new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }
            }),
            [".mp3"] = (".mp3", "audio/mpeg", new[]
            {
                new byte[] { 0x49, 0x44, 0x33 }, // ID3v2 tag header ("ID3")
                new byte[] { 0xFF, 0xFB },       // MPEG-1 Layer 3 frame sync
                new byte[] { 0xFF, 0xF3 },       // MPEG-2 Layer 3 frame sync
                new byte[] { 0xFF, 0xF2 }        // MPEG-2.5 Layer 3 frame sync
            })
        };

    private readonly ILogger<FileUploadValidationService> _logger;

    public FileUploadValidationService(ILogger<FileUploadValidationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FileUploadValidationResult> ValidateAndSanitizeAsync(
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
        {
            return FileUploadValidationResult.Failure(
                "FILE_EMPTY",
                "El archivo adjunto está vacío o no fue recibido.");
        }

        if (file.Length > MaxAllowedFileSizeBytes)
        {
            return FileUploadValidationResult.Failure(
                "FILE_TOO_LARGE",
                $"El archivo excede el tamaño máximo permitido de 10 MB ({file.Length} bytes recibidos).");
        }

        string originalFileName = file.FileName ?? string.Empty;

        // 1. Detección temprana de intentos de Path Traversal o inyección de bytes nulos
        if (originalFileName.Contains('\0') ||
            originalFileName.Contains("..", StringComparison.Ordinal) ||
            originalFileName.Contains('/') ||
            originalFileName.Contains('\\'))
        {
            _logger.LogWarning(
                "Intento de Path Traversal bloqueado en subida de archivo. Nombre recibido: {FileName}",
                originalFileName);

            return FileUploadValidationResult.Failure(
                "PATH_TRAVERSAL_DETECTED",
                "El nombre del archivo contiene secuencias de ruta no permitidas.");
        }

        // 2. Bloqueo de doble extensión peligrosa (ej: evidencia.pdf.exe, foto.png.js)
        string safeLeafName = Path.GetFileName(originalFileName);
        if (safeLeafName.Count(c => c == '.') != 1)
        {
            return FileUploadValidationResult.Failure(
                "MULTIPLE_EXTENSIONS_FORBIDDEN",
                "Por seguridad, el archivo no puede contener múltiples extensiones.");
        }

        string extension = Path.GetExtension(safeLeafName).ToLowerInvariant();
        if (!WhitelistedFormats.TryGetValue(extension, out var formatSpec))
        {
            return FileUploadValidationResult.Failure(
                "EXTENSION_NOT_WHITELISTED",
                $"La extensión '{extension}' no está permitida. Formatos autorizados: .pdf, .png, .jpg, .mp3.");
        }

        await using Stream fileStream = file.OpenReadStream();

        // 3. Inspección de Magic Bytes (verificación binaria de cabecera)
        int maxMagicLength = formatSpec.MagicSignatures.Max(sig => sig.Length);
        byte[] headerBytes = new byte[maxMagicLength];
        int bytesRead = await fileStream.ReadAsync(headerBytes.AsMemory(0, maxMagicLength), cancellationToken);

        bool magicMatched = formatSpec.MagicSignatures.Any(signature =>
            bytesRead >= signature.Length &&
            headerBytes.AsSpan(0, signature.Length).SequenceEqual(signature));

        if (!magicMatched)
        {
            _logger.LogWarning(
                "Falsificación de firma binaria (Magic Bytes) detectada. Extensión declarada: {Extension}",
                extension);

            return FileUploadValidationResult.Failure(
                "MAGIC_BYTES_MISMATCH",
                "La firma binaria interna del archivo (Magic Bytes) no coincide con el formato declarado.");
        }

        // 4. Escaneo heurístico de contenido malicioso y cálculo de Hash SHA-256
        fileStream.Position = 0;
        using var memoryBuffer = new MemoryStream((int)file.Length);
        await fileStream.CopyToAsync(memoryBuffer, cancellationToken);

        (bool isCleanPayload, string? threatCode, string? threatMessage, string sha256Hex) =
            InspectPayloadAndComputeSha256(
                memoryBuffer.GetBuffer(),
                (int)memoryBuffer.Length,
                formatSpec.CanonicalExtension);

        if (!isCleanPayload)
        {
            return FileUploadValidationResult.Failure(threatCode!, threatMessage!);
        }

        // 5. Renombrado seguro con GUID v4 para erradicar por completo Path Traversal y colisiones
        string sanitizedFileName = $"{Guid.NewGuid():N}{formatSpec.CanonicalExtension}";

        return new FileUploadValidationResult(
            IsValid: true,
            ErrorCode: null,
            ErrorMessage: null,
            OriginalFileNameSanitized: safeLeafName,
            SanitizedFileName: sanitizedFileName,
            Extension: formatSpec.CanonicalExtension,
            VerifiedMimeType: formatSpec.MimeType,
            Sha256Checksum: sha256Hex,
            FileSizeBytes: file.Length);
    }

    private (bool IsClean, string? ErrorCode, string? ErrorMessage, string Sha256Hex) InspectPayloadAndComputeSha256(
        byte[] rawBuffer,
        int length,
        string canonicalExtension)
    {
        ReadOnlySpan<byte> payloadSpan = rawBuffer.AsSpan(0, length);

        if (payloadSpan.IndexOf(EicarTestSignature) >= 0)
        {
            _logger.LogCritical("Firma antimalware EICAR detectada en subida de archivo.");
            return (
                false,
                "MALWARE_SIGNATURE_DETECTED",
                "El archivo fue bloqueado por contener una firma de código malicioso.",
                string.Empty);
        }

        if (canonicalExtension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            if (payloadSpan.IndexOf(DangerousPdfJsToken) >= 0 ||
                payloadSpan.IndexOf(DangerousPdfLaunchToken) >= 0 ||
                payloadSpan.IndexOf(DangerousPdfOpenActionToken) >= 0)
            {
                _logger.LogWarning("PDF bloqueado por contener acciones ejecutables o JavaScript embebido.");
                return (
                    false,
                    "PDF_ACTIVE_CONTENT_FORBIDDEN",
                    "No se permiten archivos PDF con scripts o acciones automáticas embebidas.",
                    string.Empty);
            }
        }

        byte[] sha256Bytes = SHA256.HashData(payloadSpan);
        string sha256Hex = Convert.ToHexString(sha256Bytes);

        return (true, null, null, sha256Hex);
    }

    public string BuildSafeStorageObjectPath(
        Guid cursoId,
        Guid tareaId,
        Guid alumnoId,
        string sanitizedFileName)
    {
        if (cursoId == Guid.Empty || tareaId == Guid.Empty || alumnoId == Guid.Empty)
        {
            throw new ArgumentException("Los identificadores de Curso, Tarea y Alumno deben ser GUIDs válidos.");
        }

        string cleanFileName = Path.GetFileName(sanitizedFileName);
        return $"cursos/{cursoId:N}/tareas/{tareaId:N}/alumnos/{alumnoId:N}/{cleanFileName}";
    }
}
