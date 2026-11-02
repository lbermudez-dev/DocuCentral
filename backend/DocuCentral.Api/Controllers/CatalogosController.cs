using System.Text.RegularExpressions;
using DocuCentral.Api.Data;
using DocuCentral.Api.Domain;
using DocuCentral.Api.Dtos;
using DocuCentral.Api.Security;
using DocuCentral.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocuCentral.Api.Controllers;

[ApiController]
[Route("api/areas")]
[Authorize]
public class AreasController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditoriaService _auditoria;
    private readonly UsuarioActual _usuario;

    public AreasController(AppDbContext db, AuditoriaService auditoria, UsuarioActual usuario)
    {
        _db = db;
        _auditoria = auditoria;
        _usuario = usuario;
    }

    /// <summary>Áreas. Con soloAccesibles=true devuelve solo las que el usuario puede usar (para formularios y filtros).</summary>
    [HttpGet]
    public async Task<List<AreaDto>> Listar([FromQuery] bool soloAccesibles = false)
    {
        var q = _db.Areas.AsNoTracking();
        if (soloAccesibles)
        {
            var visibles = await _usuario.AreasVisiblesAsync();
            q = q.Where(a => a.Activo);
            if (visibles != null) q = q.Where(a => visibles.Contains(a.Id));
        }
        return await q.OrderBy(a => a.Nombre).Select(a => new AreaDto(a.Id, a.Codigo, a.Nombre, a.Activo)).ToListAsync();
    }

    [HttpPost]
    [RequierePermiso(Permisos.AreasAdministrar)]
    public async Task<ActionResult<AreaDto>> Crear(AreaGuardarDto dto)
    {
        var error = Validar(dto);
        if (error != null) return BadRequest(new { mensaje = error });
        var codigo = dto.Codigo.Trim().ToUpperInvariant();
        if (await _db.Areas.AnyAsync(a => a.Codigo == codigo)) return BadRequest(new { mensaje = "Ya existe un área con ese código." });
        var a = new Area { Codigo = codigo, Nombre = dto.Nombre.Trim(), Activo = dto.Activo };
        _db.Areas.Add(a);
        await _db.SaveChangesAsync();
        await _auditoria.RegistrarYGuardarAsync(AccionesAuditoria.Crear, "Area", a.Id, $"{a.Codigo} - {a.Nombre}");
        return new AreaDto(a.Id, a.Codigo, a.Nombre, a.Activo);
    }

    [HttpPut("{id:int}")]
    [RequierePermiso(Permisos.AreasAdministrar)]
    public async Task<IActionResult> Editar(int id, AreaGuardarDto dto)
    {
        var a = await _db.Areas.FindAsync(id);
        if (a == null) return NotFound();
        var error = Validar(dto);
        if (error != null) return BadRequest(new { mensaje = error });
        var codigo = dto.Codigo.Trim().ToUpperInvariant();
        if (await _db.Areas.AnyAsync(x => x.Codigo == codigo && x.Id != id)) return BadRequest(new { mensaje = "Ya existe un área con ese código." });
        _auditoria.Registrar(AccionesAuditoria.Modificar, "Area", id, $"{a.Codigo} - {a.Nombre} → {codigo} - {dto.Nombre}");
        a.Codigo = codigo;
        a.Nombre = dto.Nombre.Trim();
        a.Activo = dto.Activo;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static string? Validar(AreaGuardarDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Nombre)) return "El nombre es obligatorio.";
        if (string.IsNullOrWhiteSpace(dto.Codigo) || !Regex.IsMatch(dto.Codigo.Trim(), "^[A-Za-z0-9_-]{2,30}$"))
            return "El código debe tener de 2 a 30 caracteres: letras, números, guion o guion bajo (se usa como nombre de carpeta).";
        return null;
    }
}

[ApiController]
[Route("api/tipos-documento")]
[Authorize]
public class TiposDocumentoController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditoriaService _auditoria;

    public TiposDocumentoController(AppDbContext db, AuditoriaService auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    [HttpGet]
    public async Task<List<TipoDocumentoDto>> Listar([FromQuery] bool soloActivos = false)
    {
        var q = _db.TiposDocumento.AsNoTracking().Include(t => t.Campos).AsQueryable();
        if (soloActivos) q = q.Where(t => t.Activo);
        var tipos = await q.OrderBy(t => t.Nombre).ToListAsync();
        return tipos.Select(t => Mapear(t, soloActivos)).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TipoDocumentoDto>> Obtener(int id)
    {
        var t = await _db.TiposDocumento.AsNoTracking().Include(x => x.Campos).FirstOrDefaultAsync(x => x.Id == id);
        return t == null ? NotFound() : Mapear(t, false);
    }

    [HttpPost]
    [RequierePermiso(Permisos.TiposAdministrar)]
    public async Task<ActionResult<TipoDocumentoDto>> Crear(TipoDocumentoGuardarDto dto)
    {
        var error = Validar(dto);
        if (error != null) return BadRequest(new { mensaje = error });
        if (await _db.TiposDocumento.AnyAsync(t => t.Nombre == dto.Nombre.Trim()))
            return BadRequest(new { mensaje = "Ya existe un tipo de documento con ese nombre." });

        var t = new TipoDocumento { Nombre = dto.Nombre.Trim(), Descripcion = dto.Descripcion?.Trim(), Activo = dto.Activo };
        foreach (var c in dto.Campos) t.Campos.Add(NuevoCampo(c));
        _db.TiposDocumento.Add(t);
        await _db.SaveChangesAsync();
        await _auditoria.RegistrarYGuardarAsync(AccionesAuditoria.Crear, "TipoDocumento", t.Id, t.Nombre);
        return Mapear(t, false);
    }

    /// <summary>Actualiza el tipo y sincroniza sus campos. Un campo que ya tiene valores guardados no se borra:
    /// se desactiva, para no perder información de documentos existentes.</summary>
    [HttpPut("{id:int}")]
    [RequierePermiso(Permisos.TiposAdministrar)]
    public async Task<IActionResult> Editar(int id, TipoDocumentoGuardarDto dto)
    {
        var t = await _db.TiposDocumento.Include(x => x.Campos).FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        var error = Validar(dto);
        if (error != null) return BadRequest(new { mensaje = error });
        if (await _db.TiposDocumento.AnyAsync(x => x.Nombre == dto.Nombre.Trim() && x.Id != id))
            return BadRequest(new { mensaje = "Ya existe un tipo de documento con ese nombre." });

        t.Nombre = dto.Nombre.Trim();
        t.Descripcion = dto.Descripcion?.Trim();
        t.Activo = dto.Activo;

        var idsRecibidos = dto.Campos.Where(c => c.Id.HasValue).Select(c => c.Id!.Value).ToHashSet();
        foreach (var existente in t.Campos.Where(c => !idsRecibidos.Contains(c.Id)).ToList())
        {
            var tieneValores = await _db.DocumentoValores.AnyAsync(v => v.CampoId == existente.Id);
            if (tieneValores) existente.Activo = false;
            else _db.CamposTipoDocumento.Remove(existente);
        }
        foreach (var c in dto.Campos)
        {
            var existente = c.Id.HasValue ? t.Campos.FirstOrDefault(x => x.Id == c.Id) : null;
            if (existente == null)
            {
                if (t.Campos.Any(x => x.Clave == NormalizarClave(c.Clave)))
                    return BadRequest(new { mensaje = $"La clave '{c.Clave}' ya existe en este tipo (puede estar desactivada)." });
                t.Campos.Add(NuevoCampo(c));
                continue;
            }
            // La clave no cambia una vez creada: es la referencia estable del campo
            existente.Etiqueta = c.Etiqueta.Trim();
            existente.TipoDato = c.TipoDato;
            existente.Obligatorio = c.Obligatorio;
            existente.Opciones = c.Opciones?.Trim();
            existente.Orden = c.Orden;
            existente.Activo = c.Activo;
        }
        _auditoria.Registrar(AccionesAuditoria.Modificar, "TipoDocumento", id, t.Nombre);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static CampoTipoDocumento NuevoCampo(CampoGuardarDto c) => new()
    {
        Clave = NormalizarClave(c.Clave),
        Etiqueta = c.Etiqueta.Trim(),
        TipoDato = c.TipoDato,
        Obligatorio = c.Obligatorio,
        Opciones = c.Opciones?.Trim(),
        Orden = c.Orden,
        Activo = c.Activo
    };

    private static string NormalizarClave(string clave) => clave.Trim().ToLowerInvariant();

    private static string? Validar(TipoDocumentoGuardarDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Nombre)) return "El nombre es obligatorio.";
        var claves = new HashSet<string>();
        foreach (var c in dto.Campos)
        {
            if (string.IsNullOrWhiteSpace(c.Etiqueta)) return "Todos los campos necesitan una etiqueta.";
            if (string.IsNullOrWhiteSpace(c.Clave) || !Regex.IsMatch(c.Clave.Trim(), "^[a-zA-Z][a-zA-Z0-9_]{0,59}$"))
                return $"Clave inválida en '{c.Etiqueta}': use letras, números y guion bajo, empezando con letra.";
            if (!claves.Add(NormalizarClave(c.Clave))) return $"La clave '{c.Clave}' está repetida.";
            if (c.TipoDato == TipoDatoCampo.Lista && string.IsNullOrWhiteSpace(c.Opciones))
                return $"El campo '{c.Etiqueta}' es de tipo lista y necesita opciones (separadas por |).";
        }
        return null;
    }

    private static TipoDocumentoDto Mapear(TipoDocumento t, bool soloCamposActivos) => new(
        t.Id, t.Nombre, t.Descripcion, t.Activo,
        t.Campos.Where(c => !soloCamposActivos || c.Activo).OrderBy(c => c.Orden)
            .Select(c => new CampoDto(c.Id, c.Clave, c.Etiqueta, c.TipoDato, c.Obligatorio, c.Opciones, c.Orden, c.Activo))
            .ToList());
}
