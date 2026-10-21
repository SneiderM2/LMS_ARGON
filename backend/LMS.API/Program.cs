using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using LMS.API.Controllers;
using LMS.API.Data;
using LMS.API.Middleware;
using LMS.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// 1. Registro de LMSDbContext: usa Npgsql (Supabase PostgreSQL) cuando la cadena real está configurada,
//    o activa automáticamente la base de datos local en memoria para ejecución inmediata de 1 clic.
string? connectionString = builder.Configuration.GetConnectionString("SupabasePostgres");
bool useLocalInMemoryDb = string.IsNullOrWhiteSpace(connectionString)
    || connectionString.Contains("YOUR_PROJECT_REF", StringComparison.OrdinalIgnoreCase)
    || connectionString.Contains("YOUR_SUPABASE_PASSWORD", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<LMSDbContext>(options =>
{
    if (useLocalInMemoryDb)
    {
        options.UseInMemoryDatabase("SciKidsLocalEnterpriseDb");
    }
    else
    {
        options.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null);
        });
    }
});

// 2. Configuración y registro de servicios de seguridad (JWT, Magic Bytes, Habeas Data)
builder.Services.Configure<JwtSecurityOptions>(
    builder.Configuration.GetSection(JwtSecurityOptions.SectionName));

builder.Services.AddDistributedMemoryCache();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IFileUploadValidationService, FileUploadValidationService>();
builder.Services.AddScoped<IHabeasDataComplianceService, HabeasDataComplianceService>();

// 3. Configuración de Rate Limiting mediante Token Bucket por frontera (IP + UserId)
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
            TokenLimit = 60,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 4,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            TokensPerPeriod = 25,
            AutoReplenishment = true
        });
    });
});

// 4. Autenticación JWT con verificación en lista de revocación distribuida
JwtSecurityOptions jwtOptions = builder.Configuration
    .GetSection(JwtSecurityOptions.SectionName)
    .Get<JwtSecurityOptions>() ?? new JwtSecurityOptions
{
    Issuer = "https://scikids-api.onrender.com",
    Audience = "https://sneiderm2.github.io",
    SecretKey = "SCIKIDS_ENTERPRISE_256BIT_SECRET_KEY_COLOMBIA_LEY1581_2026!"
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

// 5. CORS estricto con credenciales habilitadas para cookies HttpOnly
string[] allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:4200", "https://localhost:4200", "https://sneiderm2.github.io" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("SciKidsSpaCors", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowCredentials()
              .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
              .WithHeaders("Authorization", "Content-Type", "X-Requested-With");
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 6. Pipeline de Middlewares DevSecOps (Cabeceras -> Excepciones RFC 7807 -> Swagger -> CORS -> Auth -> RateLimiting)
app.UseSciKidsSecurityHeaders();
app.UseSciKidsGlobalExceptionHandler();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SciKids LMS Enterprise API v1");
    c.RoutePrefix = "swagger";
});

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("SciKidsSpaCors");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapControllers();

// 7. Inicialización y Seeding automático (Roles, Grados 1° a 5°, Usuarios Demo y Cursos)
await DbInitializer.InitializeAndSeedAsync(app.Services);

if (useLocalInMemoryDb)
{
    app.Logger.LogInformation(
        "MODO LOCAL 1-CLIC ACTIVO: Base de datos en memoria inicializada con Roles, Grados (1° a 5°) y cuentas semilla (mateo.explorador, profe.laura, admin.scikids).");
}

app.Run();
