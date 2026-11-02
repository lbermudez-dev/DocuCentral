using DocuCentral.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocuCentral.Api.Data;

public static class DatabaseSetup
{
    /// <summary>Registra el DbContext con el motor indicado en Database:Provider
    /// (SqlServer | PostgreSQL | MySQL) y la cadena ConnectionStrings:Default.</summary>
    public static IServiceCollection AddBaseDeDatos(this IServiceCollection services, IConfiguration config)
    {
        var proveedor = (config["Database:Provider"] ?? "SqlServer").Trim().ToLowerInvariant();
        var cadena = config.GetConnectionString("Default")
                     ?? throw new InvalidOperationException("Falta ConnectionStrings:Default en la configuración.");

        services.AddDbContext<AppDbContext>(opt =>
        {
            switch (proveedor)
            {
                case "sqlserver":
                case "mssql":
                    opt.UseSqlServer(cadena, o => o.EnableRetryOnFailure());
                    break;
                case "postgresql":
                case "postgres":
                case "npgsql":
                    opt.UseNpgsql(cadena, o => o.EnableRetryOnFailure());
                    break;
                case "mysql":
                case "mariadb":
                    var version = config["Database:ServerVersion"];
                    var serverVersion = string.IsNullOrWhiteSpace(version)
                        ? new MySqlServerVersion(new Version(8, 0, 36))
                        : ServerVersion.Parse(version);
                    opt.UseMySql(cadena, serverVersion, o => o.EnableRetryOnFailure());
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Proveedor de base de datos no soportado: '{proveedor}'. Use SqlServer, PostgreSQL o MySQL.");
            }
        });

        return services;
    }

    /// <summary>Crea el esquema (si no existe) y carga los datos iniciales.</summary>
    public static async Task InicializarAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseSetup");

        // EnsureCreated funciona igual en los tres motores sin migraciones por proveedor.
        // Para producción con cambios de esquema futuros, ver README (sección Migraciones).
        if (config.GetValue("Database:AutoCreate", true))
            await db.Database.EnsureCreatedAsync();

        if (!await db.Areas.AnyAsync())
        {
            db.Areas.Add(new Area { Codigo = "GEN", Nombre = "General" });
            await db.SaveChangesAsync();
        }

        // Rol Administrador: siempre con todos los permisos (se re-sincroniza al iniciar)
        var admin = await db.Roles.Include(r => r.Permisos).FirstOrDefaultAsync(r => r.Nombre == "Administrador");
        if (admin == null)
        {
            admin = new Rol { Nombre = "Administrador", Descripcion = "Acceso total al sistema", EsSistema = true };
            db.Roles.Add(admin);
        }
        foreach (var p in Permisos.Todos)
            if (admin.Permisos.All(x => x.Permiso != p.Codigo))
                admin.Permisos.Add(new RolPermiso { Permiso = p.Codigo });

        if (!await db.Roles.AnyAsync(r => r.Nombre == "Archivista"))
        {
            db.Roles.Add(new Rol
            {
                Nombre = "Archivista",
                Descripcion = "Digitaliza, indexa y mantiene documentos de su área",
                Permisos = new[] { Permisos.DocumentosVer, Permisos.DocumentosCargar, Permisos.DocumentosEditar,
                                   Permisos.DocumentosDescargar, Permisos.BandejaIndexar }
                    .Select(c => new RolPermiso { Permiso = c }).ToList()
            });
        }
        if (!await db.Roles.AnyAsync(r => r.Nombre == "Consulta"))
        {
            db.Roles.Add(new Rol
            {
                Nombre = "Consulta",
                Descripcion = "Busca, visualiza y descarga documentos de su área",
                Permisos = new[] { Permisos.DocumentosVer, Permisos.DocumentosDescargar }
                    .Select(c => new RolPermiso { Permiso = c }).ToList()
            });
        }
        await db.SaveChangesAsync();

        if (!await db.Usuarios.AnyAsync())
        {
            var pwd = config["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(pwd)) pwd = "Admin123*";
            var area = await db.Areas.FirstAsync();
            var u = new Usuario
            {
                NombreUsuario = "admin",
                NombreCompleto = "Administrador del sistema",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(pwd, 12),
                TipoAutenticacion = TipoAutenticacion.Local,
                DebeCambiarPassword = true,
                AreaId = area.Id
            };
            u.Roles.Add(new UsuarioRol { RolId = admin.Id });
            db.Usuarios.Add(u);
            await db.SaveChangesAsync();
            log.LogWarning("Se creó el usuario 'admin'. Cambie la contraseña en el primer inicio de sesión.");
        }

        if (!await db.TiposDocumento.AnyAsync())
        {
            db.TiposDocumento.AddRange(
                new TipoDocumento
                {
                    Nombre = "Cédula de identidad",
                    Descripcion = "Copia digital del documento de identidad",
                    Campos =
                    {
                        new CampoTipoDocumento { Clave = "fecha_emision", Etiqueta = "Fecha de emisión", TipoDato = TipoDatoCampo.Fecha, Orden = 1 },
                        new CampoTipoDocumento { Clave = "fecha_vencimiento", Etiqueta = "Fecha de vencimiento", TipoDato = TipoDatoCampo.Fecha, Orden = 2 },
                    }
                },
                new TipoDocumento
                {
                    Nombre = "Contrato",
                    Campos =
                    {
                        new CampoTipoDocumento { Clave = "numero_contrato", Etiqueta = "Número de contrato", TipoDato = TipoDatoCampo.Texto, Obligatorio = true, Orden = 1 },
                        new CampoTipoDocumento { Clave = "tipo_contrato", Etiqueta = "Tipo de contrato", TipoDato = TipoDatoCampo.Lista, Opciones = "Laboral|Servicios|Crédito|Arrendamiento|Otro", Orden = 2 },
                        new CampoTipoDocumento { Clave = "monto", Etiqueta = "Monto", TipoDato = TipoDatoCampo.Numero, Orden = 3 },
                        new CampoTipoDocumento { Clave = "vigente", Etiqueta = "Vigente", TipoDato = TipoDatoCampo.SiNo, Orden = 4 },
                    }
                },
                new TipoDocumento { Nombre = "Otro documento", Descripcion = "Documentos sin clasificación específica" }
            );
            await db.SaveChangesAsync();
        }
    }
}
