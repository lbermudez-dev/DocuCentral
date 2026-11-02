using DocuCentral.Api.Data;
using DocuCentral.Api.Domain;
using DocuCentral.Api.Dtos;
using DocuCentral.Api.Security;
using DocuCentral.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocuCentral.Api.Controllers;

[ApiController]
[Route("api/usuarios")]
[RequierePermiso(Permisos.UsuariosAdministrar)]
public class UsuariosController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditoriaService _auditoria;
    private readonly UsuarioActual _actual;

    public UsuariosController(AppDbContext db, AuditoriaService auditoria, UsuarioActual actual)
    {
        _db = db;
        _auditoria = auditoria;
        _actual = actual;
    }

    [HttpGet]
    public async Task<List<UsuarioDto>> Listar([FromQuery] string? q)
    {
        var consulta = _db.Usuarios.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLower();
            consulta = consulta.Where(u => u.NombreUsuario.Contains(t) || u.NombreCompleto.ToLower().Contains(t));
        }
        var ahora = DateTime.UtcNow;
        return await consulta.OrderBy(u => u.NombreUsuario)
            .Select(u => new UsuarioDto(u.Id, u.NombreUsuario, u.NombreCompleto, u.Email, u.TipoAutenticacion, u.Activo,
                u.BloqueadoHasta != null && u.BloqueadoHasta > ahora, u.AreaId, u.Area != null ? u.Area.Nombre : null,
                u.Roles.Select(r => r.RolId).ToList(), u.AreasAdicionales.Select(a => a.AreaId).ToList(), u.UltimoAcceso))
            .ToListAsync();
    }

    [HttpPost]
    public async Task<IActionResult> Crear(UsuarioGuardarDto dto)
    {
        var nombre = (dto.NombreUsuario ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(nombre) || string.IsNullOrWhiteSpace(dto.NombreCompleto))
            return BadRequest(new { mensaje = "Usuario y nombre completo son obligatorios." });
        if (await _db.Usuarios.AnyAsync(u => u.NombreUsuario == nombre))
            return BadRequest(new { mensaje = "Ya existe un usuario con ese nombre." });

        var u = new Usuario
        {
            NombreUsuario = nombre,
            NombreCompleto = dto.NombreCompleto.Trim(),
            Email = dto.Email?.Trim(),
            TipoAutenticacion = dto.TipoAutenticacion,
            Activo = dto.Activo,
            AreaId = dto.AreaId
        };
        if (dto.TipoAutenticacion == TipoAutenticacion.Local)
        {
            var error = PoliticaPassword.Validar(dto.Password, nombre);
            if (error != null) return BadRequest(new { mensaje = error });
            u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12);
            u.DebeCambiarPassword = true;
        }
        AsignarRolesYAreas(u, dto);
        _db.Usuarios.Add(u);
        await _db.SaveChangesAsync();
        await _auditoria.RegistrarYGuardarAsync(AccionesAuditoria.Crear, "Usuario", u.Id, $"{u.NombreUsuario} ({u.TipoAutenticacion})");
        return Ok(new { id = u.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Editar(int id, UsuarioGuardarDto dto)
    {
        var u = await _db.Usuarios.Include(x => x.Roles).Include(x => x.AreasAdicionales).FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return NotFound();
        if (id == _actual.Id && !dto.Activo) return BadRequest(new { mensaje = "No puede desactivar su propio usuario." });
        if (string.IsNullOrWhiteSpace(dto.NombreCompleto)) return BadRequest(new { mensaje = "El nombre completo es obligatorio." });

        // Evitar quedarse sin ningún administrador activo
        var rolAdmin = await _db.Roles.FirstAsync(r => r.Nombre == "Administrador");
        var eraAdmin = u.Roles.Any(r => r.RolId == rolAdmin.Id) && u.Activo;
        var seraAdmin = dto.RolIds.Contains(rolAdmin.Id) && dto.Activo;
        if (eraAdmin && !seraAdmin &&
            !await _db.UsuarioRoles.AnyAsync(r => r.RolId == rolAdmin.Id && r.UsuarioId != id && r.Usuario!.Activo))
            return BadRequest(new { mensaje = "Debe quedar al menos un administrador activo." });

        if (u.TipoAutenticacion != dto.TipoAutenticacion && dto.TipoAutenticacion == TipoAutenticacion.Local)
        {
            var error = PoliticaPassword.Validar(dto.Password, u.NombreUsuario);
            if (error != null) return BadRequest(new { mensaje = "Al cambiar a autenticación local debe asignar una contraseña. " + error });
            u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12);
            u.DebeCambiarPassword = true;
        }
        if (dto.TipoAutenticacion == TipoAutenticacion.Directorio) u.PasswordHash = null;

        u.NombreCompleto = dto.NombreCompleto.Trim();
        u.Email = dto.Email?.Trim();
        u.TipoAutenticacion = dto.TipoAutenticacion;
        u.Activo = dto.Activo;
        u.AreaId = dto.AreaId;
        u.Roles.Clear();
        u.AreasAdicionales.Clear();
        AsignarRolesYAreas(u, dto);
        u.VersionSeguridad++; // cierra las sesiones abiertas para que apliquen los nuevos permisos
        _auditoria.Registrar(AccionesAuditoria.Modificar, "Usuario", id,
            $"{u.NombreUsuario}: activo={u.Activo}, roles=[{string.Join(",", dto.RolIds)}], área={dto.AreaId}, áreas extra=[{string.Join(",", dto.AreasAdicionalesIds)}]");
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/restablecer-password")]
    public async Task<IActionResult> RestablecerPassword(int id, RestablecerPasswordDto dto)
    {
        var u = await _db.Usuarios.FindAsync(id);
        if (u == null) return NotFound();
        if (u.TipoAutenticacion == TipoAutenticacion.Directorio)
            return BadRequest(new { mensaje = "Los usuarios de dominio restablecen su contraseña en Active Directory." });
        var error = PoliticaPassword.Validar(dto.PasswordNueva, u.NombreUsuario);
        if (error != null) return BadRequest(new { mensaje = error });
        u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.PasswordNueva, 12);
        u.DebeCambiarPassword = true;
        u.IntentosFallidos = 0;
        u.BloqueadoHasta = null;
        u.VersionSeguridad++;
        _auditoria.Registrar(AccionesAuditoria.CambioPassword, "Usuario", id, "Restablecida por administrador");
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/desbloquear")]
    public async Task<IActionResult> Desbloquear(int id)
    {
        var u = await _db.Usuarios.FindAsync(id);
        if (u == null) return NotFound();
        u.BloqueadoHasta = null;
        u.IntentosFallidos = 0;
        _auditoria.Registrar(AccionesAuditoria.Modificar, "Usuario", id, "Desbloqueado");
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static void AsignarRolesYAreas(Usuario u, UsuarioGuardarDto dto)
    {
        foreach (var r in dto.RolIds.Distinct()) u.Roles.Add(new UsuarioRol { RolId = r });
        foreach (var a in dto.AreasAdicionalesIds.Distinct().Where(a => a != dto.AreaId))
            u.AreasAdicionales.Add(new UsuarioAreaAcceso { AreaId = a });
    }
}

[ApiController]
[Route("api/roles")]
[Microsoft.AspNetCore.Authorization.Authorize]
public class RolesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditoriaService _auditoria;

    private readonly UsuarioActual _actual;

    public RolesController(AppDbContext db, AuditoriaService auditoria, UsuarioActual actual)
    {
        _db = db;
        _auditoria = auditoria;
        _actual = actual;
    }

    /// <summary>Lo necesita quien administra roles y también quien asigna roles a usuarios.</summary>
    [HttpGet]
    public async Task<ActionResult<List<RolDto>>> Listar()
    {
        if (!_actual.Tiene(Permisos.RolesAdministrar) && !_actual.Tiene(Permisos.UsuariosAdministrar)) return Forbid();
        return await _db.Roles.AsNoTracking().OrderBy(r => r.Nombre)
            .Select(r => new RolDto(r.Id, r.Nombre, r.Descripcion, r.EsSistema,
                r.Permisos.Select(p => p.Permiso).ToList(),
                _db.UsuarioRoles.Count(ur => ur.RolId == r.Id)))
            .ToListAsync();
    }

    [HttpGet("permisos")]
    [RequierePermiso(Permisos.RolesAdministrar)]
    public IEnumerable<Permisos.DefinicionPermiso> CatalogoPermisos() => Permisos.Todos;

    [HttpPost]
    [RequierePermiso(Permisos.RolesAdministrar)]
    public async Task<IActionResult> Crear(RolGuardarDto dto)
    {
        var error = await Validar(dto, null);
        if (error != null) return BadRequest(new { mensaje = error });
        var r = new Rol
        {
            Nombre = dto.Nombre.Trim(),
            Descripcion = dto.Descripcion?.Trim(),
            Permisos = dto.Permisos.Distinct().Select(p => new RolPermiso { Permiso = p }).ToList()
        };
        _db.Roles.Add(r);
        await _db.SaveChangesAsync();
        await _auditoria.RegistrarYGuardarAsync(AccionesAuditoria.Crear, "Rol", r.Id, $"{r.Nombre}: {string.Join(", ", dto.Permisos)}");
        return Ok(new { id = r.Id });
    }

    [HttpPut("{id:int}")]
    [RequierePermiso(Permisos.RolesAdministrar)]
    public async Task<IActionResult> Editar(int id, RolGuardarDto dto)
    {
        var r = await _db.Roles.Include(x => x.Permisos).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        if (r.EsSistema) return BadRequest(new { mensaje = "El rol Administrador no se puede modificar." });
        var error = await Validar(dto, id);
        if (error != null) return BadRequest(new { mensaje = error });

        r.Nombre = dto.Nombre.Trim();
        r.Descripcion = dto.Descripcion?.Trim();
        r.Permisos.Clear();
        foreach (var p in dto.Permisos.Distinct()) r.Permisos.Add(new RolPermiso { Permiso = p });

        // Invalida las sesiones de quienes tienen el rol para que tomen los nuevos permisos
        await _db.Usuarios.Where(u => u.Roles.Any(x => x.RolId == id))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.VersionSeguridad, u => u.VersionSeguridad + 1));
        _auditoria.Registrar(AccionesAuditoria.Modificar, "Rol", id, $"{r.Nombre}: {string.Join(", ", dto.Permisos)}");
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [RequierePermiso(Permisos.RolesAdministrar)]
    public async Task<IActionResult> Eliminar(int id)
    {
        var r = await _db.Roles.FindAsync(id);
        if (r == null) return NotFound();
        if (r.EsSistema) return BadRequest(new { mensaje = "Este rol no se puede eliminar." });
        if (await _db.UsuarioRoles.AnyAsync(x => x.RolId == id))
            return BadRequest(new { mensaje = "El rol tiene usuarios asignados. Quíteselo antes de eliminarlo." });
        _db.Roles.Remove(r);
        _auditoria.Registrar(AccionesAuditoria.Eliminar, "Rol", id, r.Nombre);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<string?> Validar(RolGuardarDto dto, int? id)
    {
        if (string.IsNullOrWhiteSpace(dto.Nombre)) return "El nombre es obligatorio.";
        var invalido = dto.Permisos.FirstOrDefault(p => !Permisos.Existe(p));
        if (invalido != null) return $"Permiso desconocido: {invalido}";
        if (await _db.Roles.AnyAsync(r => r.Nombre == dto.Nombre.Trim() && r.Id != id)) return "Ya existe un rol con ese nombre.";
        return null;
    }
}

[ApiController]
[Route("api/configuracion")]
[RequierePermiso(Permisos.ConfiguracionAdministrar)]
public class ConfiguracionController : ControllerBase
{
    private readonly ParametrosService _parametros;
    private readonly AuditoriaService _auditoria;
    private readonly EstadoIngesta _estado;

    public ConfiguracionController(ParametrosService parametros, AuditoriaService auditoria, EstadoIngesta estado)
    {
        _parametros = parametros;
        _auditoria = auditoria;
        _estado = estado;
    }

    [HttpGet]
    public Task<Dictionary<string, string?>> Obtener() => _parametros.ObtenerValoresAsync();

    /// <summary>Guarda los parámetros. Antes verifica que el servicio pueda crear y escribir en cada ruta.</summary>
    [HttpPut]
    public async Task<IActionResult> Guardar(Dictionary<string, string?> valores)
    {
        var rutas = new[] { ClavesParametro.RutaRepositorio, ClavesParametro.RutaEntrada, ClavesParametro.RutaErrores, ClavesParametro.RutaProcesados };
        foreach (var clave in rutas)
        {
            if (!valores.TryGetValue(clave, out var ruta) || string.IsNullOrWhiteSpace(ruta)) continue;
            var error = ProbarRuta(ruta!);
            if (error != null) return BadRequest(new { mensaje = $"{clave.Split('.').Last()}: {error}" });
        }
        var antes = await _parametros.ObtenerValoresAsync();
        await _parametros.GuardarAsync(valores);
        var cambios = valores.Where(v => antes.TryGetValue(v.Key, out var a) && a != v.Value)
            .Select(v => $"{v.Key}: '{antes[v.Key]}' → '{v.Value}'");
        await _auditoria.RegistrarYGuardarAsync(AccionesAuditoria.Modificar, "Configuracion", null, string.Join("; ", cambios));
        _estado.SolicitarEjecucion();
        return NoContent();
    }

    /// <summary>Prueba de escritura sin guardar nada.</summary>
    [HttpPost("probar-ruta")]
    public IActionResult Probar([FromBody] string ruta)
    {
        var error = ProbarRuta(ruta);
        return error == null ? Ok(new { ok = true, mensaje = "La ruta es accesible y se puede escribir." }) : BadRequest(new { mensaje = error });
    }

    private static string? ProbarRuta(string ruta)
    {
        try
        {
            if (!Path.IsPathFullyQualified(ruta)) return "Debe ser una ruta absoluta (ej. D:\\Repositorio, \\\\servidor\\docs o /data/docs).";
            Directory.CreateDirectory(ruta);
            var prueba = Path.Combine(ruta, $".prueba_{Guid.NewGuid():N}");
            System.IO.File.WriteAllText(prueba, "ok");
            System.IO.File.Delete(prueba);
            return null;
        }
        catch (Exception ex)
        {
            return $"No se pudo escribir en '{ruta}': {ex.Message} Verifique que la cuenta del servicio tenga permisos de lectura y escritura.";
        }
    }
}

[ApiController]
[Route("api/auditoria")]
[RequierePermiso(Permisos.AuditoriaVer)]
public class AuditoriaController : ControllerBase
{
    private readonly AppDbContext _db;
    public AuditoriaController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<PaginaDto<AuditoriaDto>> Listar([FromQuery] string? usuario, [FromQuery] string? accion,
        [FromQuery] string? entidad, [FromQuery] string? texto, [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta,
        [FromQuery] int pagina = 1, [FromQuery] int tamano = 50)
    {
        var q = _db.Auditoria.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(usuario)) q = q.Where(a => a.NombreUsuario.Contains(usuario.Trim().ToLower()));
        if (!string.IsNullOrWhiteSpace(accion)) q = q.Where(a => a.Accion == accion);
        if (!string.IsNullOrWhiteSpace(entidad)) q = q.Where(a => a.Entidad == entidad);
        if (!string.IsNullOrWhiteSpace(texto))
        {
            var t = texto.Trim().ToLower();
            q = q.Where(a => (a.Detalle != null && a.Detalle.ToLower().Contains(t)) || (a.EntidadId != null && a.EntidadId.ToLower().Contains(t)));
        }
        if (desde.HasValue) q = q.Where(a => a.Fecha >= desde.Value.Date);
        if (hasta.HasValue) q = q.Where(a => a.Fecha < hasta.Value.Date.AddDays(1));

        var total = await q.CountAsync();
        tamano = Math.Clamp(tamano, 1, 500);
        pagina = Math.Max(1, pagina);
        var items = await q.OrderByDescending(a => a.Id).Skip((pagina - 1) * tamano).Take(tamano)
            .Select(a => new AuditoriaDto(a.Id, a.Fecha, a.NombreUsuario, a.Accion, a.Entidad, a.EntidadId, a.Detalle, a.DireccionIp))
            .ToListAsync();
        return new PaginaDto<AuditoriaDto>(items, total, pagina, tamano);
    }

    [HttpGet("acciones")]
    public IEnumerable<string> Acciones() =>
        typeof(AccionesAuditoria).GetFields().Select(f => (string)f.GetValue(null)!);
}
