using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using LMS.API.Controllers;
using LMS.API.Middleware;
using LMS.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// 1. Configuración y registro de servicios de seguridad (JWT, Archivos Magic Bytes, Habeas Data)
builder.Services.Configure<JwtSecurityOptions>(
    builder.Configuration.GetSection(JwtSecurityOptions.SectionName));

builder.Services.AddDistributedMemoryCache();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IFileUploadValidationService, FileUploadValidationService>();
builder.Services.AddScoped<IHabeasDataComplianceService, HabeasDataComplianceService>();

// 2. Configuración de Rate Limiting mediante Token Bucket por frontera (IP + UserId)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        var problem = new ProblemDetails
        {
            Type = "https://httpstatuses.io/429",
            Title = "Límite de Solicitudes Excedido",
            Status = StatusCodes.Status429TooManyRequests,
            Detail = "Se ha alcanzado el umbral máximo de peticiones permitido por las políticas de protección de SciKids LMS.",
            Instance = context.HttpContext.Request.Path
        };
        context.HttpContext.Response.ContentType = "application/problem+json; charset=utf-8";
        await context.HttpContext.Response.WriteAsJsonAsync(problem, token);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        string ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";
        string userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                        ?? "anonymous";

        return RateLimitPartition.GetTokenBucketLimiter($"{ip}:{userId}", _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 40,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 2,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            TokensPerPeriod = 15,
            AutoReplenishment = true
        });
    });
});

// 3. Autenticación JWT con verificación en lista de revocación distribuida
JwtSecurityOptions jwtOptions = builder.Configuration
    .GetSection(JwtSecurityOptions.SectionName)
    .Get<JwtSecurityOptions>() ?? new JwtSecurityOptions
{
    Issuer = "https://scikids-api.onrender.com",
    Audience = "https://scikids-lms.github.io",
    SecretKey = "CHANGE_THIS_IN_PRODUCTION_WITH_MINIMUM_32_BYTES_SECRET_KEY_2026!"
};

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(15),
            RoleClaimType = ClaimTypes.Role
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var jwtService = context.HttpContext.RequestServices.GetRequiredService<IJwtTokenService>();
                string jti = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti) ?? string.Empty;
                string? rawUserId = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                string? sessionFamilyId = context.Principal?.FindFirstValue("sid");

                Guid.TryParse(rawUserId, out Guid userId);

                if (await jwtService.IsTokenRevokedAsync(jti, userId, sessionFamilyId, context.HttpContext.RequestAborted))
                {
                    context.Fail("La sesión o el token JWT han sido revocados.");
                }
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

// 4. Pipeline de Middlewares DevSecOps (Cabeceras -> Excepciones RFC 7807 -> HTTPS -> Auth -> RateLimiting)
app.UseSciKidsSecurityHeaders();
app.UseSciKidsGlobalExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();
