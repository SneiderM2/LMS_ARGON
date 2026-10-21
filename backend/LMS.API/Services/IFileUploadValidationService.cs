using Microsoft.AspNetCore.Http;

namespace LMS.API.Services;

/// <summary>
/// Resultado inmutable del pipeline de validación y sanitización de evidencias escolares.
/// </summary>
public sealed record FileUploadValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorMessage,
    string OriginalFileNameSanitized,
    string SanitizedFileName,
    string Extension,
    string VerifiedMimeType,
    string Sha256Checksum,
    long FileSizeBytes
)
{
    public static FileUploadValidationResult Failure(string errorCode, string errorMessage) =>
        new(
            IsValid: false,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage,
            OriginalFileNameSanitized: string.Empty,
            SanitizedFileName: string.Empty,
            Extension: string.Empty,
            VerifiedMimeType: string.Empty,
            Sha256Checksum: string.Empty,
            FileSizeBytes: 0);
}

/// <summary>
/// Contrato de validación multicapa para subida de archivos/evidencias mediante Magic Bytes y renombrado GUID.
/// </summary>
public interface IFileUploadValidationService
{
    /// <summary>
    /// Valida un archivo subido verificando tamaño máximo (10 MB), extensión en lista blanca (.pdf, .png, .jpg),
    /// firma binaria (Magic Bytes), ausencia de scripts maliciosos embebidos y genera un nombre GUID contra Path Traversal.
    /// </summary>
    Task<FileUploadValidationResult> ValidateAndSanitizeAsync(
        IFormFile? file,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Construye una ruta de objeto aislada para almacenamiento sin emplear ningún carácter provisto por el usuario.
    /// </summary>
    string BuildSafeStorageObjectPath(Guid cursoId, Guid tareaId, Guid alumnoId, string sanitizedFileName);
}
