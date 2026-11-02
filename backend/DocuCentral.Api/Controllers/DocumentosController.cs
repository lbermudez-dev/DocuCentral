using System.Text.Json;
using DocuCentral.Api.Data;
using DocuCentral.Api.Domain;
using DocuCentral.Api.Dtos;
using DocuCentral.Api.Security;
using DocuCentral.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocuCentral.Api.Controllers;

[ApiController]
[Route("api/documentos")]
public class DocumentosController : ControllerBase
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly DocumentoService _docs;
    private readonly AlmacenamientoService _almacen;
    private readonly AuditoriaService _auditoria;
    private readonly UsuarioActual _usuario;

    public DocumentosController(AppDbContext db, DocumentoService docs, AlmacenamientoService almacen,
        AuditoriaService auditoria, UsuarioActual usuario)
    {
        _db = db;
        _docs = docs;
        _almacen = almacen;
        _auditoria = auditoria;
        _usuario = usuario;
    }

    /// <summary>Búsqueda con filtros por texto, identificador, titular, tipo, área, fechas y campos dinámicos.</summary>
    [HttpPost("buscar")]
    [RequierePermiso(Permisos.DocumentosVer)]
    public async Task<ActionResult<PaginaDto<DocumentoResumenDto>>> Buscar(BusquedaDto filtro) =>
        await _docs.BuscarAsync(filtro);

    [HttpGet("{id:guid}")]
    [RequierePermiso(Permisos.DocumentosVer)]
    public async Task<ActionResult<DocumentoDetalleDto>> Detalle(Guid id)
    {
        var d = await _docs.DetalleAsync(id);
        if (d == null) return NotFound();
        await _auditoria.RegistrarYGuardarAsync(AccionesAuditoria.Ver, "Documento", id, d.Titulo);
        return d;
    }

    /// <summary>Contenido del archivo. inline=true para el visor (permiso Ver); inline=false descarga (permiso Descargar).</summary>
    [HttpGet("{id:guid}/archivo")]
    [RequierePermiso(Permisos.DocumentosVer)]
    public async Task<IActionResult> Archivo(Guid id, [FromQuery] int? version, [FromQuery] bool inline = true)
    {
        if (!inline && !_usuario.Tiene(Permisos.DocumentosDescargar)) return Forbid();

        var d = await _db.Documentos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (d == null || d.Estado == EstadoDocumento.Eliminado) return NotFound();
        if (d.Estado == EstadoDocumento.PendienteIndexar && !_usuario.Tiene(Permisos.BandejaIndexar)) return NotFound();
        if (!await _usuario.PuedeVerAreaAsync(d.AreaId)) return NotFound();

        var num = version ?? d.VersionActual;
        var v = await _db.DocumentoVersiones.AsNoTracking()
            .FirstOrDefaultAsync(x => x.DocumentoId == id && x.NumeroVersion == num);
        if (v == null) return NotFound();

        var fs = await _almacen.AbrirAsync(v.RutaRelativa);
        if (fs == null) return Problem("El archivo no se encuentra en el repositorio. Verifique la ruta configurada.", statusCode: 410);

        await _auditoria.RegistrarYGuardarAsync(inline ? AccionesAuditoria.Ver : AccionesAuditoria.Descargar,
            "Documento", id, $"{v.NombreOriginal} (v{v.NumeroVersion})");

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (inline)
        {
            Response.Headers["Content-Disposition"] = "inline";
            return File(fs, v.TipoMime, enableRangeProcessing: true);
        }
        return File(fs, v.TipoMime, v.NombreOriginal, enableRangeProcessing: true);
    }

    /// <summary>Carga un documento nuevo. multipart/form-data: archivo + metadatos (JSON de DocumentoMetadatosDto).</summary>
    [HttpPost]
    [RequierePermiso(Permisos.DocumentosCargar)]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<object>> Cargar([FromForm] IFormFile archivo, [FromForm] string metadatos)
    {
        var m = JsonSerializer.Deserialize<DocumentoMetadatosDto>(metadatos, Json);
        if (m == null) return BadRequest(new { mensaje = "Metadatos inválidos." });

        try
        {
            await _almacen.ValidarAsync(archivo.FileName, archivo.Length);
            var d = new Documento
            {
                Origen = OrigenDocumento.CargaWeb,
                Estado = EstadoDocumento.Activo,
                CreadoPorId = _usuario.Id,
                CreadoEn = DateTime.UtcNow,
                VersionActual = 1
            };
            _db.Documentos.Add(d);
            await _docs.AplicarMetadatosAsync(d, m);

            await using var s = archivo.OpenReadStream();
            var g = await _almacen.GuardarAsync(s, archivo.FileName, d.Id, 1, d.CreadoEn);
            d.Versiones.Add(NuevaVersion(g, archivo.FileName, 1, null));

            _auditoria.Registrar(AccionesAuditoria.Cargar, "Documento", d.Id, $"{d.Titulo} — {archivo.FileName}");
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(Detalle), new { id = d.Id },
                new { id = d.Id, duplicados = await Duplicados(g.HashSha256, d.Id) });
        }
        catch (Exception ex) when (ex is ValidacionException or ArchivoInvalidoException)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [RequierePermiso(Permisos.DocumentosEditar)]
    public async Task<IActionResult> Editar(Guid id, DocumentoMetadatosDto m)
    {
        var d = await _db.Documentos.FirstOrDefaultAsync(x => x.Id == id && x.Estado == EstadoDocumento.Activo);
        if (d == null || !await _usuario.PuedeVerAreaAsync(d.AreaId)) return NotFound();
        try
        {
            var antes = $"Título: {d.Titulo}; Identificador: {d.Identificador}; Titular: {d.NombreTitular}; TipoId: {d.TipoDocumentoId}; AreaId: {d.AreaId}";
            await _docs.AplicarMetadatosAsync(d, m);
            d.ModificadoEn = DateTime.UtcNow;
            d.ModificadoPorId = _usuario.Id;
            _auditoria.Registrar(AccionesAuditoria.Editar, "Documento", id, $"Antes → {antes}");
            await _db.SaveChangesAsync();
            return NoContent();
        }
        catch (ValidacionException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Sube una nueva versión del archivo. Las versiones anteriores se conservan.</summary>
    [HttpPost("{id:guid}/versiones")]
    [RequierePermiso(Permisos.DocumentosEditar)]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> NuevaVersionArchivo(Guid id, [FromForm] IFormFile archivo, [FromForm] string? comentario)
    {
        var d = await _db.Documentos.FirstOrDefaultAsync(x => x.Id == id && x.Estado == EstadoDocumento.Activo);
        if (d == null || !await _usuario.PuedeVerAreaAsync(d.AreaId)) return NotFound();
        try
        {
            await _almacen.ValidarAsync(archivo.FileName, archivo.Length);
            var num = await _db.DocumentoVersiones.Where(v => v.DocumentoId == id).MaxAsync(v => (int?)v.NumeroVersion) ?? 0;
            num++;
            await using var s = archivo.OpenReadStream();
            var g = await _almacen.GuardarAsync(s, archivo.FileName, d.Id, num, d.CreadoEn);
            _db.DocumentoVersiones.Add(NuevaVersion(g, archivo.FileName, num, comentario, d.Id));
            d.VersionActual = num;
            d.ModificadoEn = DateTime.UtcNow;
            d.ModificadoPorId = _usuario.Id;
            _auditoria.Registrar(AccionesAuditoria.NuevaVersion, "Documento", id, $"v{num}: {archivo.FileName}. {comentario}");
            await _db.SaveChangesAsync();
            return Ok(new { version = num });
        }
        catch (ArchivoInvalidoException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Eliminación lógica: el documento deja de aparecer, pero el archivo y su historial se conservan.</summary>
    [HttpDelete("{id:guid}")]
    [RequierePermiso(Permisos.DocumentosEliminar)]
    public async Task<IActionResult> Eliminar(Guid id, [FromQuery] string? motivo)
    {
        var d = await _db.Documentos.FirstOrDefaultAsync(x => x.Id == id && x.Estado != EstadoDocumento.Eliminado);
        if (d == null || !await _usuario.PuedeVerAreaAsync(d.AreaId)) return NotFound();
        if (string.IsNullOrWhiteSpace(motivo)) return BadRequest(new { mensaje = "Indique el motivo de la eliminación." });
        d.Estado = EstadoDocumento.Eliminado;
        d.ModificadoEn = DateTime.UtcNow;
        d.ModificadoPorId = _usuario.Id;
        _auditoria.Registrar(AccionesAuditoria.Eliminar, "Documento", id, $"{d.Titulo}. Motivo: {motivo}");
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:guid}/historial")]
    [RequierePermiso(Permisos.DocumentosVer)]
    public async Task<ActionResult<IEnumerable<AuditoriaDto>>> Historial(Guid id)
    {
        if (await _docs.DetalleAsync(id, incluirPendientes: true) == null) return NotFound();
        var clave = id.ToString();
        var q = _db.Auditoria.AsNoTracking().Where(a => a.Entidad == "Documento" && a.EntidadId == clave);
        // Sin permiso de auditoría solo se ven los cambios, no quién consultó o descargó
        if (!_usuario.Tiene(Permisos.AuditoriaVer))
            q = q.Where(a => a.Accion != AccionesAuditoria.Ver && a.Accion != AccionesAuditoria.Descargar);
        return await q.OrderByDescending(a => a.Fecha).Take(200)
            .Select(a => new AuditoriaDto(a.Id, a.Fecha, a.NombreUsuario, a.Accion, a.Entidad, a.EntidadId, a.Detalle, a.DireccionIp))
            .ToListAsync();
    }

    // ─────────────────────────────────────────────────────────────

    private DocumentoVersion NuevaVersion(ArchivoGuardado g, string nombre, int num, string? comentario, Guid? docId = null)
    {
        var v = new DocumentoVersion
        {
            NumeroVersion = num,
            NombreOriginal = Path.GetFileName(nombre),
            RutaRelativa = g.RutaRelativa,
            Extension = g.Extension,
            TipoMime = g.TipoMime,
            TamanoBytes = g.TamanoBytes,
            HashSha256 = g.HashSha256,
            Comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim(),
            CreadoPorId = _usuario.Id,
            CreadoEn = DateTime.UtcNow
        };
        if (docId.HasValue) v.DocumentoId = docId.Value;
        return v;
    }

    /// <summary>Otros documentos activos con exactamente el mismo archivo (mismo hash).</summary>
    private async Task<List<object>> Duplicados(string hash, Guid excepto)
    {
        var lista = await _db.Documentos.AsNoTracking()
            .Where(d => d.Id != excepto && d.Estado == EstadoDocumento.Activo && d.Versiones.Any(v => v.HashSha256 == hash))
            .Select(d => new { id = d.Id, titulo = d.Titulo })
            .Take(5).ToListAsync();
        return lista.Cast<object>().ToList();
    }
}
