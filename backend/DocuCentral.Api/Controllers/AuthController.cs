using DocuCentral.Api.Data;
using DocuCentral.Api.Domain;
using DocuCentral.Api.Dtos;
using DocuCentral.Api.Security;
using DocuCentral.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DocuCentral.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    // Hash ficticio para igualar el tiempo de respuesta cuando el usuario no existe
    private static readonly string HashFicticio = BCrypt.Net.BCrypt.HashPassword("no-existe", 12);

    private readonly AppDbContext _db;
    private readonly TokenService _tokens;
    private readonly LdapService _ldap;
    private readonly AuditoriaService _auditoria;
    private readonly UsuarioActual _actual;
    private readonly IConfiguration _config;

    public AuthController(AppDbContext db, TokenService tokens, LdapService ldap, AuditoriaService auditoria,
        UsuarioActual actual, IConfiguration config)
    {
        _db = db;
        _tokens = tokens;
        _ldap = ldap;
        _auditoria = auditoria;
        _actual = actual;
        _config = config;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest req)
    {
        const string mensajeGenerico = "Usuario o contraseña incorrectos.";
        var nombre = (req.Usuario ?? "").Trim().ToLowerInvariant();
        if (nombre.Contains('\\')) nombre = nombre.Split('\\').Last();   // DOMINIO\usuario → usuario
        if (string.IsNullOrEmpty(nombre) || string.IsNullOrEmpty(req.Password))
            return Unauthorized(new { mensaje = mensajeGenerico });

        var u = await CargarUsuario(nombre);

        // Usuario de AD que todavía no existe en el sistema: alta automática si está habilitada
        if (u == null && _ldap.Opciones.Habilitado && _ldap.Opciones.AutoCrearUsuarios)
        {
            var r = await _ldap.AutenticarAsync(nombre, req.Password);
            if (r.Valido)
            {
                u = await CrearUsuarioDirectorio(nombre, r);
                await Auditar(AccionesAuditoria.Crear, u, "Alta automática desde Active Directory");
                return await EmitirToken(u);
            }
        }

        if (u == null)
        {
            BCrypt.Net.BCrypt.Verify(req.Password, HashFicticio);
            await Auditar(AccionesAuditoria.LoginFallido, null, "Usuario inexistente", nombre);
            return Unauthorized(new { mensaje = mensajeGenerico });
        }

        if (!u.Activo)
        {
            await Auditar(AccionesAuditoria.LoginFallido, u, "Usuario inactivo");
            return Unauthorized(new { mensaje = mensajeGenerico });
        }

        if (u.BloqueadoHasta > DateTime.UtcNow)
        {
            await Auditar(AccionesAuditoria.LoginFallido, u, "Intento con usuario bloqueado");
            return StatusCode(423, new { mensaje = $"Usuario bloqueado temporalmente por intentos fallidos. Intente después de {u.BloqueadoHasta:HH:mm} (UTC) o contacte al administrador." });
        }

        bool valido;
        string? errorDirectorio = null;
        if (u.TipoAutenticacion == TipoAutenticacion.Directorio)
        {
            var r = await _ldap.AutenticarAsync(nombre, req.Password);
            valido = r.Valido;
            errorDirectorio = r.Error;
            if (valido)
            {
                if (!string.IsNullOrWhiteSpace(r.NombreCompleto)) u.NombreCompleto = r.NombreCompleto!;
                if (!string.IsNullOrWhiteSpace(r.Email)) u.Email = r.Email;
            }
        }
        else
        {
            valido = !string.IsNullOrEmpty(u.PasswordHash) && BCrypt.Net.BCrypt.Verify(req.Password, u.PasswordHash);
        }

        if (!valido)
        {
            var max = _config.GetValue("Seguridad:IntentosMaximos", 5);
            var minutos = _config.GetValue("Seguridad:MinutosBloqueo", 15);
            u.IntentosFallidos++;
            var detalle = errorDirectorio ?? $"Contraseña incorrecta (intento {u.IntentosFallidos} de {max})";
            if (u.IntentosFallidos >= max)
            {
                u.BloqueadoHasta = DateTime.UtcNow.AddMinutes(minutos);
                u.IntentosFallidos = 0;
                _auditoria.Registrar(AccionesAuditoria.UsuarioBloqueado, "Usuario", u.Id,
                    $"Bloqueado {minutos} minutos", u.Id, u.NombreUsuario);
            }
            await Auditar(AccionesAuditoria.LoginFallido, u, detalle);
            return Unauthorized(new { mensaje = errorDirectorio ?? mensajeGenerico });
        }

        return await EmitirToken(u);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UsuarioSesionDto>> Me()
    {
        var u = await _db.Usuarios.AsNoTracking().Include(x => x.Area)
            .Include(x => x.Roles).ThenInclude(r => r.Rol!).ThenInclude(r => r.Permisos)
            .FirstOrDefaultAsync(x => x.Id == _actual.Id);
        if (u == null) return Unauthorized();
        return Sesion(u, PermisosDe(u));
    }

    [HttpPost("cambiar-password")]
    [Authorize]
    public async Task<ActionResult<LoginResponse>> CambiarPassword(CambiarPasswordRequest req)
    {
        var u = await _db.Usuarios.Include(x => x.Area)
            .Include(x => x.Roles).ThenInclude(r => r.Rol!).ThenInclude(r => r.Permisos)
            .FirstOrDefaultAsync(x => x.Id == _actual.Id);
        if (u == null) return Unauthorized();
        if (u.TipoAutenticacion == TipoAutenticacion.Directorio)
            return BadRequest(new { mensaje = "La contraseña de los usuarios de dominio se cambia en Active Directory." });
        if (string.IsNullOrEmpty(u.PasswordHash) || !BCrypt.Net.BCrypt.Verify(req.PasswordActual, u.PasswordHash))
            return BadRequest(new { mensaje = "La contraseña actual no es correcta." });

        var error = PoliticaPassword.Validar(req.PasswordNueva, u.NombreUsuario);
        if (error != null) return BadRequest(new { mensaje = error });
        if (BCrypt.Net.BCrypt.Verify(req.PasswordNueva, u.PasswordHash))
            return BadRequest(new { mensaje = "La nueva contraseña debe ser distinta de la actual." });

        u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.PasswordNueva, 12);
        u.DebeCambiarPassword = false;
        u.VersionSeguridad++;
        _auditoria.Registrar(AccionesAuditoria.CambioPassword, "Usuario", u.Id);
        await _db.SaveChangesAsync();

        // Los tokens anteriores quedan invalidados (VersionSeguridad); se entrega uno nuevo
        var permisos = PermisosDe(u);
        var (token, expira) = _tokens.Generar(u, permisos);
        return new LoginResponse(token, expira, Sesion(u, permisos));
    }

    [HttpGet("config")]
    [AllowAnonymous]
    public IActionResult ConfigPublica() =>
        Ok(new { directorioHabilitado = _ldap.Opciones.Habilitado, dominio = _ldap.Opciones.Dominio });

    // ─────────────────────────────────────────────────────────────

    private Task<Usuario?> CargarUsuario(string nombre) =>
        _db.Usuarios.Include(x => x.Area)
            .Include(x => x.Roles).ThenInclude(r => r.Rol!).ThenInclude(r => r.Permisos)
            .FirstOrDefaultAsync(x => x.NombreUsuario == nombre);

    private async Task<Usuario> CrearUsuarioDirectorio(string nombre, ResultadoLdap r)
    {
        var rol = await _db.Roles.FirstOrDefaultAsync(x => x.Nombre == _ldap.Opciones.RolPorDefecto);
        var area = string.IsNullOrWhiteSpace(_ldap.Opciones.AreaPorDefecto)
            ? null
            : await _db.Areas.FirstOrDefaultAsync(a => a.Codigo == _ldap.Opciones.AreaPorDefecto);
        var u = new Usuario
        {
            NombreUsuario = nombre,
            NombreCompleto = r.NombreCompleto ?? nombre,
            Email = r.Email,
            TipoAutenticacion = TipoAutenticacion.Directorio,
            AreaId = area?.Id
        };
        if (rol != null) u.Roles.Add(new UsuarioRol { RolId = rol.Id });
        _db.Usuarios.Add(u);
        await _db.SaveChangesAsync();
        return (await CargarUsuario(nombre))!;
    }

    private async Task<ActionResult<LoginResponse>> EmitirToken(Usuario u)
    {
        u.IntentosFallidos = 0;
        u.BloqueadoHasta = null;
        u.UltimoAcceso = DateTime.UtcNow;
        var permisos = PermisosDe(u);
        var (token, expira) = _tokens.Generar(u, permisos);
        await Auditar(AccionesAuditoria.Login, u, u.TipoAutenticacion == TipoAutenticacion.Directorio ? "Directorio" : "Local");
        return new LoginResponse(token, expira, Sesion(u, permisos));
    }

    private static List<string> PermisosDe(Usuario u) =>
        u.Roles.SelectMany(r => r.Rol!.Permisos.Select(p => p.Permiso)).Distinct().ToList();

    private static UsuarioSesionDto Sesion(Usuario u, IEnumerable<string> permisos) =>
        new(u.Id, u.NombreUsuario, u.NombreCompleto, u.Area?.Nombre, u.DebeCambiarPassword, u.TipoAutenticacion, permisos);

    private async Task Auditar(string accion, Usuario? u, string? detalle, string? nombreIntentado = null)
    {
        var nombre = u?.NombreUsuario ?? nombreIntentado ?? "desconocido";
        if (nombre.Length > 100) nombre = nombre[..100];
        _auditoria.Registrar(accion, "Sesion", u?.Id, detalle, u?.Id, nombre);
        await _db.SaveChangesAsync();
    }
}

public static class PoliticaPassword
{
    public static string? Validar(string? password, string? usuario)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            return "La contraseña debe tener al menos 8 caracteres.";
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
            return "La contraseña debe incluir mayúsculas, minúsculas y números.";
        if (!string.IsNullOrEmpty(usuario) && password.Contains(usuario, StringComparison.OrdinalIgnoreCase))
            return "La contraseña no puede contener el nombre de usuario.";
        return null;
    }
}
