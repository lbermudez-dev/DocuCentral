using System.Security.Claims;
using System.Threading.RateLimiting;
using DocuCentral.Api.Data;
using DocuCentral.Api.Security;
using DocuCentral.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

// PostgreSQL: permite guardar DateTime sin zona (igual que SQL Server y MySQL)
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Permite correr como servicio de Windows además de IIS/Docker/consola
builder.Host.UseWindowsService();

// ── Base de datos (SqlServer | PostgreSQL | MySQL según Database:Provider)
builder.Services.AddBaseDeDatos(config);
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

// ── Servicios de la aplicación
builder.Services.AddSingleton<ParametrosService>();
builder.Services.AddSingleton<AlmacenamientoService>();
builder.Services.AddSingleton<EstadoIngesta>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<LdapService>();
builder.Services.AddScoped<UsuarioActual>();
builder.Services.AddScoped<AuditoriaService>();
builder.Services.AddScoped<DocumentoService>();
builder.Services.AddHostedService<CarpetaVigiladaService>();

// ── Autenticación JWT
var jwt = config.GetSection("Jwt").Get<JwtOpciones>() ?? new JwtOpciones();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = TokenService.ClaveFirma(jwt.Key),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.Name
        };
        o.Events = new JwtBearerEvents
        {
            // Un token deja de valer si el usuario se desactivó o cambió su contraseña/roles (VersionSeguridad)
            OnTokenValidated = async ctx =>
            {
                var id = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var sv = ctx.Principal?.FindFirstValue(ClaimsApp.VersionSeguridad);
                if (!int.TryParse(id, out var uid)) { ctx.Fail("Token inválido"); return; }

                var cache = ctx.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
                var clave = $"sv_{uid}";
                if (!cache.TryGetValue(clave, out (bool activo, int version) estado))
                {
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var u = await db.Usuarios.AsNoTracking().Where(x => x.Id == uid)
                        .Select(x => new { x.Activo, x.VersionSeguridad }).FirstOrDefaultAsync();
                    estado = (u?.Activo ?? false, u?.VersionSeguridad ?? -1);
                    cache.Set(clave, estado, TimeSpan.FromSeconds(30));
                }
                if (!estado.activo || sv != estado.version.ToString())
                    ctx.Fail("La sesión ya no es válida.");
            }
        };
    });
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermisoPolicyProvider>();
builder.Services.AddAuthorization();

// ── Límite de intentos de login por IP (además del bloqueo por usuario)
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

// ── Archivos grandes: el límite real lo define Almacenamiento:TamanoMaximoMB
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 1024L * 1024 * 1024);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 1024L * 1024 * 1024);

builder.Services.AddControllers();
builder.Services.AddCors(o => o.AddPolicy("frontend", p =>
    p.WithOrigins(config.GetSection("Cors:Origenes").Get<string[]>() ?? Array.Empty<string>())
     .AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Content-Disposition")));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "DocuCentral API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

var app = builder.Build();

await DatabaseSetup.InicializarAsync(app.Services);

// Detrás de IIS / nginx / proxy: IP real del cliente para la auditoría
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseExceptionHandler(e => e.Run(async ctx =>
{
    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new { mensaje = "Ocurrió un error inesperado. Consulte el registro del servidor." });
}));

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});

if (app.Environment.IsDevelopment() || config.GetValue("Swagger:Habilitado", false))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Frontend compilado (wwwroot) servido por la misma aplicación: un solo sitio en IIS
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("frontend");
app.UseRateLimiter();
app.UseAuthentication();

// Mientras el usuario deba cambiar su contraseña, solo puede usar /api/auth/*
app.Use(async (ctx, next) =>
{
    if (ctx.User.HasClaim(ClaimsApp.DebeCambiarPassword, "1") &&
        ctx.Request.Path.StartsWithSegments("/api") &&
        !ctx.Request.Path.StartsWithSegments("/api/auth"))
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new { mensaje = "Debe cambiar su contraseña antes de continuar.", codigo = "DEBE_CAMBIAR_PASSWORD" });
        return;
    }
    await next();
});

app.UseAuthorization();
app.MapControllers();
app.Map("/api/{**resto}", () => Results.NotFound(new { mensaje = "Recurso no encontrado." }));
app.MapFallbackToFile("index.html"); // rutas del frontend (React Router)

app.Run();
