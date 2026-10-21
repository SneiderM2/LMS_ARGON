using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace LMS.API.Middleware;

/// <summary>
/// Middleware Global de Manejo de Excepciones estandarizado bajo RFC 7807 (Problem Details for HTTP APIs).
/// Garantiza que ningún Stack Trace, consulta SQL ni detalle de infraestructura sea expuesto al cliente en producción.
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsRfc7807Async(context, ex);
        }
    }

    private async Task HandleExceptionAsRfc7807Async(HttpContext context, Exception exception)
    {
        string traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        _logger.LogError(
            exception,
            "Excepción interceptada por GlobalExceptionMiddleware | TraceId: {TraceId} | Method: {Method} | Path: {Path}",
            traceId,
            context.Request.Method,
            context.Request.Path);

        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "La respuesta HTTP ya había iniciado para TraceId {TraceId}; no es posible sobrescribir el cuerpo RFC 7807.",
                traceId);
            return;
        }

        (int statusCode, string errorCode, string title, string safeDetail) = MapExceptionToProblemDescriptor(exception);

        var problemDetails = new ProblemDetails
        {
            Type = $"https://httpstatuses.io/{statusCode}",
            Title = title,
            Status = statusCode,
            Detail = safeDetail,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["traceId"] = traceId;
        problemDetails.Extensions["errorCode"] = errorCode;
        problemDetails.Extensions["timestampUtc"] = DateTimeOffset.UtcNow;

        // Solo en entorno local de Desarrollo se incluye el diagnóstico controlado; jamás en Staging/Production
        if (_environment.IsDevelopment())
        {
            problemDetails.Extensions["debugExceptionType"] = exception.GetType().FullName;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json; charset=utf-8";

        string jsonPayload = JsonSerializer.Serialize(problemDetails, JsonOptions);
        await context.Response.WriteAsync(jsonPayload, context.RequestAborted);
    }

    private static (int StatusCode, string ErrorCode, string Title, string SafeDetail) MapExceptionToProblemDescriptor(
        Exception exception)
    {
        return exception switch
        {
            SecurityTokenException => (
                StatusCodes.Status401Unauthorized,
                "AUTH_INVALID_OR_EXPIRED_TOKEN",
                "Token de Autenticación Inválido",
                "Su sesión ha expirado o el token de seguridad no pudo ser validado."),

            UnauthorizedAccessException uaEx => (
                StatusCodes.Status403Forbidden,
                "SECURITY_ACCESS_DENIED_LEY_1581_1098",
                "Acceso Restringido por Política de Protección de Datos",
                string.IsNullOrWhiteSpace(uaEx.Message)
                    ? "No cuenta con los permisos o el consentimiento parental requerido para ejecutar esta operación."
                    : uaEx.Message),

            CryptographicException => (
                StatusCodes.Status400BadRequest,
                "CRYPTO_INTEGRITY_VERIFICATION_FAILED",
                "Fallo de Integridad Criptográfica",
                "No fue posible verificar la integridad criptográfica de los datos protegidos."),

            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "RESOURCE_NOT_FOUND",
                "Recurso No Encontrado",
                "El recurso académico o registro solicitado no existe o no está disponible para su rol."),

            ArgumentException argEx => (
                StatusCodes.Status400BadRequest,
                "DOMAIN_VALIDATION_ERROR",
                "Solicitud Inválida",
                argEx.Message),

            InvalidOperationException invEx => (
                StatusCodes.Status409Conflict,
                "DOMAIN_STATE_CONFLICT",
                "Conflicto en el Estado de la Operación",
                invEx.Message),

            OperationCanceledException => (
                499,
                "CLIENT_CLOSED_REQUEST",
                "Solicitud Cancelada",
                "La operación fue cancelada antes de completarse."),

            _ => (
                StatusCodes.Status500InternalServerError,
                "INTERNAL_SERVER_ERROR",
                "Error Interno de Plataforma",
                "Ha ocurrido un error inesperado al procesar la solicitud. Por favor conserve el código traceId para soporte.")
        };
    }
}

public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseSciKidsGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionMiddleware>();
    }
}
