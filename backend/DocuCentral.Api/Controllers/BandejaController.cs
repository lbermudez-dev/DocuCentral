using DocuCentral.Api.Data;
using DocuCentral.Api.Domain;
using DocuCentral.Api.Dtos;
using DocuCentral.Api.Security;
using DocuCentral.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocuCentral.Api.Controllers;

/// <summary>Bandeja de entrada: documentos importados desde la carpeta vigilada que esperan sus metadatos.</summary>
[ApiController]
[Route("api/bandeja")]
[RequierePermiso(Permisos.BandejaIndexar)]
public class BandejaController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly DocumentoService _docs;
    private readonly AuditoriaService _auditoria;
    private readonly UsuarioActual _usuario;
    private readonly EstadoIngesta _estado;
    private readonly ParametrosService _parametros;

    public BandejaController(AppDbContext db, DocumentoService docs, AuditoriaService auditoria, UsuarioActual usuario,
        EstadoIngesta estado, ParametrosService parametros)
    {
        _db = db;
        _docs = docs;
        _auditoria = auditoria;
        _usuario = usuario;
        _estado = estado;
        _parametros = parametros;
    }

    [HttpGet]
    public async Task<ActionResult<PaginaDto<DocumentoResumenDto>>> Listar([FromQuery] int pagina = 1, [FromQuery] int tamano = 50)
    {
        var q = await _docs.ConsultaVisibleAsync(EstadoDocumento.PendienteIndexar);
        var total = await q.CountAsync();
        tamano = Math.Clamp(tamano, 1, 200);
        pagina = Math.Max(1, pagina);
        var items = await DocumentoService.Proyectar(q.OrderBy(d => d.CreadoEn).Skip((pagina - 1) * tamano).Take(tamano)).ToListAsync();
        return new PaginaDto<DocumentoResumenDto>(items, total, pagina, tamano);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentoDetalleDto>> Detalle(Guid id)
    {
        var d = await _docs.DetalleAsync(id, incluirPendientes: true);
        return d == null ? NotFound() : d;
    }

    /// <summary>Captura los metadatos de un documento pendiente y lo publica en el repositorio.</summary>
    [HttpPost("{id:guid}/indexar")]
    public async Task<IActionResult> Indexar(Guid id, DocumentoMetadatosDto m)
    {
        var d = await _db.Documentos.FirstOrDefaultAsync(x => x.Id == id && x.Estado == EstadoDocumento.PendienteIndexar);
        if (d == null || !await _usuario.PuedeVerAreaAsync(d.AreaId)) return NotFound();
        try
        {
            await _docs.AplicarMetadatosAsync(d, m);
            d.Estado = EstadoDocumento.Activo;
            d.CreadoPorId ??= _usuario.Id;
            d.ModificadoEn = DateTime.UtcNow;
            d.ModificadoPorId = _usuario.Id;
            _auditoria.Registrar(AccionesAuditoria.Indexar, "Documento", id, d.Titulo);
            await _db.SaveChangesAsync();
            return NoContent();
        }
        catch (ValidacionException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Descarta un documento de la bandeja (escaneo erróneo, página en blanco...). El archivo se conserva.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Descartar(Guid id, [FromQuery] string? motivo)
    {
        var d = await _db.Documentos.FirstOrDefaultAsync(x => x.Id == id && x.Estado == EstadoDocumento.PendienteIndexar);
        if (d == null || !await _usuario.PuedeVerAreaAsync(d.AreaId)) return NotFound();
        d.Estado = EstadoDocumento.Eliminado;
        d.ModificadoEn = DateTime.UtcNow;
        d.ModificadoPorId = _usuario.Id;
        _auditoria.Registrar(AccionesAuditoria.Eliminar, "Documento", id, $"Descartado de la bandeja. {motivo}");
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("estado")]
    public async Task<ActionResult<EstadoIngestaDto>> Estado()
    {
        var cfg = await _parametros.ObtenerAsync();
        var pendientes = await (await _docs.ConsultaVisibleAsync(EstadoDocumento.PendienteIndexar)).CountAsync();
        return new EstadoIngestaDto(cfg.CarpetaVigiladaActiva, cfg.RutaEntrada, _estado.UltimaEjecucion,
            _estado.Importados, _estado.Errores, _estado.UltimoError, pendientes);
    }

    [HttpPost("procesar-ahora")]
    public IActionResult ProcesarAhora()
    {
        _estado.SolicitarEjecucion();
        return Accepted();
    }
}
