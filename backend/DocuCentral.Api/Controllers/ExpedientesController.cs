using System.IO.Compression;
using DocuCentral.Api.Data;
using DocuCentral.Api.Domain;
using DocuCentral.Api.Dtos;
using DocuCentral.Api.Security;
using DocuCentral.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocuCentral.Api.Controllers;

/// <summary>Un expediente es el conjunto de todos los documentos que comparten el mismo identificador
/// (cédula, número de socio, expediente...). Es la vista "todo lo de esta persona" en un solo lugar.</summary>
[ApiController]
[Route("api/expedientes")]
public class ExpedientesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly DocumentoService _docs;
    private readonly AlmacenamientoService _almacen;
    private readonly AuditoriaService _auditoria;

    public ExpedientesController(AppDbContext db, DocumentoService docs, AlmacenamientoService almacen, AuditoriaService auditoria)
    {
        _db = db;
        _docs = docs;
        _almacen = almacen;
        _auditoria = auditoria;
    }

    /// <summary>Busca expedientes por identificador o nombre del titular.</summary>
    [HttpGet]
    [RequierePermiso(Permisos.DocumentosVer)]
    public async Task<ActionResult<List<ExpedienteResumenDto>>> Buscar([FromQuery] string? q)
    {
        var consulta = (await _docs.ConsultaVisibleAsync()).Where(d => d.Identificador != null);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLower();
            consulta = consulta.Where(d => d.Identificador!.ToLower().Contains(t) ||
                                           (d.NombreTitular != null && d.NombreTitular.ToLower().Contains(t)));
        }

        var grupos = await consulta
            .GroupBy(d => d.Identificador!)
            .Select(g => new
            {
                Identificador = g.Key,
                Titular = g.Max(x => x.NombreTitular),
                Cantidad = g.Count(),
                Ultima = g.Max(x => x.CreadoEn)
            })
            .OrderByDescending(g => g.Ultima)
            .Take(50)
            .ToListAsync();

        var ids = grupos.Select(g => g.Identificador).ToList();
        var tipos = await consulta
            .Where(d => ids.Contains(d.Identificador!) && d.TipoDocumento != null)
            .Select(d => new { d.Identificador, Tipo = d.TipoDocumento!.Nombre })
            .Distinct()
            .ToListAsync();

        return grupos.Select(g => new ExpedienteResumenDto(g.Identificador, g.Titular, g.Cantidad, g.Ultima,
            tipos.Where(t => t.Identificador == g.Identificador).Select(t => t.Tipo).OrderBy(t => t).ToList())).ToList();
    }

    [HttpGet("{identificador}")]
    [RequierePermiso(Permisos.DocumentosVer)]
    public async Task<ActionResult<ExpedienteDto>> Detalle(string identificador)
    {
        var id = identificador.Trim().ToUpperInvariant();
        var q = (await _docs.ConsultaVisibleAsync()).Where(d => d.Identificador == id);
        var docs = await DocumentoService.Proyectar(q.OrderBy(d => d.TipoDocumento!.Nombre).ThenByDescending(d => d.CreadoEn)).ToListAsync();
        if (docs.Count == 0) return NotFound();
        var titular = docs.Select(d => d.NombreTitular).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        await _auditoria.RegistrarYGuardarAsync(AccionesAuditoria.Ver, "Expediente", id, $"{docs.Count} documentos");
        return new ExpedienteDto(id, titular, docs);
    }

    /// <summary>Descarga en un ZIP la versión vigente de todos los documentos del expediente, en carpetas por tipo.</summary>
    [HttpGet("{identificador}/zip")]
    [RequierePermiso(Permisos.DocumentosDescargar)]
    public async Task<IActionResult> Zip(string identificador)
    {
        var id = identificador.Trim().ToUpperInvariant();
        var q = (await _docs.ConsultaVisibleAsync()).Where(d => d.Identificador == id);
        var archivos = await q.Select(d => new
            {
                d.Titulo,
                Tipo = d.TipoDocumento != null ? d.TipoDocumento.Nombre : "Sin tipo",
                Version = d.Versiones.Where(v => v.NumeroVersion == d.VersionActual)
                    .Select(v => new { v.RutaRelativa, v.Extension, v.NumeroVersion }).FirstOrDefault()
            })
            .ToListAsync();
        if (archivos.Count == 0) return NotFound();

        // Se arma en un temporal (se borra solo al cerrar) para no cargar todo en memoria
        var temporal = Path.Combine(Path.GetTempPath(), $"exp_{Guid.NewGuid():N}.zip");
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var faltantes = 0;
        await using (var zipFs = new FileStream(temporal, FileMode.Create))
        using (var zip = new ZipArchive(zipFs, ZipArchiveMode.Create))
        {
            foreach (var a in archivos)
            {
                if (a.Version == null) continue;
                await using var origen = await _almacen.AbrirAsync(a.Version.RutaRelativa);
                if (origen == null) { faltantes++; continue; }
                var nombre = $"{NombreSeguro(a.Tipo)}/{NombreSeguro(a.Titulo)}{a.Version.Extension}";
                var n = 2;
                while (!usados.Add(nombre))
                    nombre = $"{NombreSeguro(a.Tipo)}/{NombreSeguro(a.Titulo)} ({n++}){a.Version.Extension}";
                var entrada = zip.CreateEntry(nombre, CompressionLevel.Fastest);
                await using var destino = entrada.Open();
                await origen.CopyToAsync(destino);
            }
        }

        await _auditoria.RegistrarYGuardarAsync(AccionesAuditoria.DescargarExpediente, "Expediente", id,
            $"{archivos.Count - faltantes} archivos" + (faltantes > 0 ? $", {faltantes} no encontrados en disco" : ""));

        var fs = new FileStream(temporal, FileMode.Open, FileAccess.Read, FileShare.None, 81920, FileOptions.DeleteOnClose);
        return File(fs, "application/zip", $"Expediente_{NombreSeguro(id)}_{DateTime.Now:yyyyMMdd}.zip");
    }

    private static string NombreSeguro(string s)
    {
        var invalidos = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
        var limpio = new string(s.Select(c => invalidos.Contains(c) ? '_' : c).ToArray()).Trim();
        return limpio.Length > 80 ? limpio[..80] : (limpio.Length == 0 ? "documento" : limpio);
    }
}
