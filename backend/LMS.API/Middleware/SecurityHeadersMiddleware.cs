using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LMS.API.Middleware;

/// <summary>
/// Middleware Enterprise de Cabeceras HTTP de Seguridad (OWASP ASVS Nivel 2 & Lineamientos MEN para AVA).
/// Protege contra ataques XSS, Clickjacking, MIME-Sniffing, degradación de protocolo TLS y filtración de cabeceras del servidor.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            IHeaderDictionary headers = context.Response.Headers;

            // 1. Prevención estricta de Clickjacking
            headers["X-Frame-Options"] = "DENY";

            // 2. Bloqueo de inferencia MIME (MIME-Sniffing)
            headers["X-Content-Type-Options"] = "nosniff";

            // 3. HTTP Strict Transport Security (HSTS) - 2 años, subdominios y preload (TLS 1.3 forzado)
            headers["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains; preload";

            // 4. Content Security Policy (CSP) estricta para API y recursos de Supabase
            headers["Content-Security-Policy"] =
                "default-src 'none'; " +
                "script-src 'self'; " +
                "style-src 'self'; " +
                "img-src 'self' data: https://*.supabase.co; " +
                "media-src 'self' https://*.supabase.co; " +
                "connect-src 'self' https://*.supabase.co; " +
                "font-src 'self'; " +
                "frame-ancestors 'none'; " +
                "base-uri 'self'; " +
                "form-action 'self'; " +
                "upgrade-insecure-requests;";

            // 5. Políticas de aislamiento de origen y privacidad de referencia
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["X-Permitted-Cross-Domain-Policies"] = "none";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-site";

            // 6. Restricción de APIs sensibles del navegador en plataformas infantiles (Ley 1098)
            headers["Permissions-Policy"] =
                "geolocation=(), camera=(), payment=(), usb=(), magnetometer=(), gyroscope=(), microphone=(self)";

            // 7. No almacenar en caché de disco respuestas con datos sensibles de menores (Habeas Data)
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
                headers["Pragma"] = "no-cache";
            }

            // 8. Eliminación de cabeceras que revelan fingerprinting de infraestructura
            headers.Remove("Server");
            headers.Remove("X-Powered-By");
            headers.Remove("X-AspNet-Version");

            return Task.CompletedTask;
        });

        return _next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSciKidsSecurityHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
