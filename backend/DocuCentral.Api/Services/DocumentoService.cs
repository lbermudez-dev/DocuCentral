using System.Globalization;
using DocuCentral.Api.Data;
using DocuCentral.Api.Domain;
using DocuCentral.Api.Dtos;
using DocuCentral.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace DocuCentral.Api.Services;

public class ValidacionException : Exception
{
    public ValidacionException(string mensaje) : base(mensaje) { }
}

public class BusquedaDto
{
    public string? Texto { get; set; }
    public string? Identificador { get; set; }
    public string? NombreTitular { get; set; }
    public int? TipoDocumentoId { get; set; }
    public int? AreaId { get; set; }
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }
    /// <summary>Filtros por campos dinámicos: id del campo → texto a buscar.</summary>
    public Dictionary<int, string>? Campos { get; set; }
    public string Orden { get; set; } = "recientes"; // recientes | antiguos | titulo | identificador
    public int Pagina { get; set; } = 1;
    public int TamanoPagina { get; set; } = 25;
}

public class DocumentoService
{
    private readonly AppDbContext _db;
    private readonly UsuarioActual _usuario;

    public DocumentoService(AppDbContext db, UsuarioActual usuario)
    {
        _db = db;
        _usuario = usuario;
    }

    /// <summary>Documentos en el estado indicado, restringidos a las áreas que el usuario puede ver.</summary>
    public async Task<IQueryable<Documento>> ConsultaVisibleAsync(EstadoDocumento estado = EstadoDocumento.Activo)
    {
        var q = _db.Documentos.AsNoTracking().Where(d => d.Estado == estado);
        var areas = await _usuario.AreasVisiblesAsync();
        if (areas != null)
        {
            q = estado == EstadoDocumento.PendienteIndexar
                ? q.Where(d => d.AreaId == null || areas.Contains(d.AreaId.Value))
                : q.Where(d => d.AreaId != null && areas.Contains(d.AreaId.Value));
        }
        return q;
    }

    public async Task<PaginaDto<DocumentoResumenDto>> BuscarAsync(BusquedaDto f)
    {
        var q = await ConsultaVisibleAsync();

        if (!string.IsNullOrWhiteSpace(f.Texto))
        {
            // Cada palabra debe aparecer en algún campo (título, identificador, titular, etiquetas, descripción o valores)
            foreach (var t in f.Texto.Trim().ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(8))
            {
                var term = t;
                q = q.Where(d =>
                    d.Titulo.ToLower().Contains(term) ||
                    (d.Identificador != null && d.Identificador.ToLower().Contains(term)) ||
                    (d.NombreTitular != null && d.NombreTitular.ToLower().Contains(term)) ||
                    (d.Etiquetas != null && d.Etiquetas.ToLower().Contains(term)) ||
                    (d.Descripcion != null && d.Descripcion.ToLower().Contains(term)) ||
                    d.Valores.Any(v => v.Valor != null && v.Valor.ToLower().Contains(term)));
            }
        }
        if (!string.IsNullOrWhiteSpace(f.Identificador))
        {
            var id = f.Identificador.Trim().ToLower();
            q = q.Where(d => d.Identificador != null && d.Identificador.ToLower().Contains(id));
        }
        if (!string.IsNullOrWhiteSpace(f.NombreTitular))
        {
            var n = f.NombreTitular.Trim().ToLower();
            q = q.Where(d => d.NombreTitular != null && d.NombreTitular.ToLower().Contains(n));
        }
        if (f.TipoDocumentoId.HasValue) q = q.Where(d => d.TipoDocumentoId == f.TipoDocumentoId);
        if (f.AreaId.HasValue) q = q.Where(d => d.AreaId == f.AreaId);
        if (f.FechaDesde.HasValue)
        {
            var desde = f.FechaDesde.Value.Date;
            q = q.Where(d => (d.FechaDocumento ?? d.CreadoEn) >= desde);
        }
        if (f.FechaHasta.HasValue)
        {
            var hasta = f.FechaHasta.Value.Date.AddDays(1);
            q = q.Where(d => (d.FechaDocumento ?? d.CreadoEn) < hasta);
        }
        if (f.Campos != null)
        {
            foreach (var (campoId, valor) in f.Campos)
            {
                if (string.IsNullOrWhiteSpace(valor)) continue;
                var cid = campoId;
                var v = valor.Trim().ToLower();
                q = q.Where(d => d.Valores.Any(x => x.CampoId == cid && x.Valor != null && x.Valor.ToLower().Contains(v)));
            }
        }

        var total = await q.CountAsync();
        q = f.Orden switch
        {
            "antiguos" => q.OrderBy(d => d.CreadoEn),
            "titulo" => q.OrderBy(d => d.Titulo),
            "identificador" => q.OrderBy(d => d.Identificador).ThenByDescending(d => d.CreadoEn),
            _ => q.OrderByDescending(d => d.CreadoEn)
        };
        var tam = Math.Clamp(f.TamanoPagina, 1, 200);
        var pag = Math.Max(1, f.Pagina);
        var items = await Proyectar(q.Skip((pag - 1) * tam).Take(tam)).ToListAsync();
        return new PaginaDto<DocumentoResumenDto>(items, total, pag, tam);
    }

    public static IQueryable<DocumentoResumenDto> Proyectar(IQueryable<Documento> q) =>
        q.Select(d => new DocumentoResumenDto(
            d.Id, d.Titulo, d.Identificador, d.NombreTitular,
            d.TipoDocumentoId, d.TipoDocumento != null ? d.TipoDocumento.Nombre : null,
            d.AreaId, d.Area != null ? d.Area.Nombre : null,
            d.FechaDocumento, d.CreadoEn, d.VersionActual,
            d.Versiones.Where(v => v.NumeroVersion == d.VersionActual).Select(v => v.Extension).FirstOrDefault(),
            d.Versiones.Where(v => v.NumeroVersion == d.VersionActual).Select(v => v.TamanoBytes).FirstOrDefault(),
            d.Estado, d.Origen));

    public async Task<DocumentoDetalleDto?> DetalleAsync(Guid id, bool incluirPendientes = false)
    {
        var d = await _db.Documentos.AsNoTracking()
            .Include(x => x.TipoDocumento)
            .Include(x => x.Area)
            .Include(x => x.CreadoPor)
            .Include(x => x.Valores).ThenInclude(v => v.Campo)
            .Include(x => x.Versiones).ThenInclude(v => v.CreadoPor)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (d == null || d.Estado == EstadoDocumento.Eliminado) return null;
        if (d.Estado == EstadoDocumento.PendienteIndexar && !incluirPendientes) return null;
        if (!await _usuario.PuedeVerAreaAsync(d.AreaId)) return null;

        return new DocumentoDetalleDto(
            d.Id, d.Titulo, d.Descripcion, d.Identificador, d.NombreTitular, d.Etiquetas, d.FechaDocumento,
            d.TipoDocumentoId, d.TipoDocumento?.Nombre, d.AreaId, d.Area?.Nombre, d.Estado, d.Origen, d.VersionActual,
            d.CreadoPor?.NombreCompleto, d.CreadoEn, d.ModificadoEn,
            d.Valores.Where(v => v.Campo != null).OrderBy(v => v.Campo!.Orden)
                .Select(v => new ValorCampoDto(v.CampoId, v.Campo!.Clave, v.Campo.Etiqueta, v.Campo.TipoDato, v.Valor)).ToList(),
            d.Versiones.OrderByDescending(v => v.NumeroVersion)
                .Select(v => new VersionDto(v.Id, v.NumeroVersion, v.NombreOriginal, v.Extension, v.TipoMime,
                    v.TamanoBytes, v.HashSha256, v.Comentario, v.CreadoPor?.NombreCompleto, v.CreadoEn)).ToList());
    }

    /// <summary>Valida y aplica metadatos (incluidos campos dinámicos) a un documento con seguimiento.</summary>
    public async Task AplicarMetadatosAsync(Documento d, DocumentoMetadatosDto m)
    {
        if (string.IsNullOrWhiteSpace(m.Titulo)) throw new ValidacionException("El título es obligatorio.");

        var tipo = await _db.TiposDocumento.Include(t => t.Campos)
                       .FirstOrDefaultAsync(t => t.Id == m.TipoDocumentoId && t.Activo)
                   ?? throw new ValidacionException("Tipo de documento no válido.");
        var area = await _db.Areas.FirstOrDefaultAsync(a => a.Id == m.AreaId && a.Activo)
                   ?? throw new ValidacionException("Área no válida.");
        if (!await _usuario.PuedeVerAreaAsync(area.Id))
            throw new ValidacionException("No tiene acceso al área seleccionada.");

        d.TipoDocumentoId = tipo.Id;
        d.AreaId = area.Id;
        d.Titulo = m.Titulo.Trim();
        d.Descripcion = Limpio(m.Descripcion);
        d.Identificador = Limpio(m.Identificador)?.ToUpperInvariant();
        d.NombreTitular = Limpio(m.NombreTitular);
        d.Etiquetas = Limpio(m.Etiquetas);
        d.FechaDocumento = m.FechaDocumento?.Date;

        // Campos dinámicos: se reemplazan todos los valores según la definición del tipo
        if (_db.Entry(d).State != EntityState.Added)
            await _db.Entry(d).Collection(x => x.Valores).LoadAsync();
        _db.DocumentoValores.RemoveRange(d.Valores);
        d.Valores.Clear();

        var errores = new List<string>();
        foreach (var campo in tipo.Campos.Where(c => c.Activo).OrderBy(c => c.Orden))
        {
            m.Valores.TryGetValue(campo.Clave, out var crudo);
            crudo = crudo?.Trim();
            if (string.IsNullOrEmpty(crudo))
            {
                if (campo.Obligatorio) errores.Add($"'{campo.Etiqueta}' es obligatorio.");
                continue;
            }
            var (ok, normalizado) = Normalizar(campo, crudo);
            if (!ok) { errores.Add($"'{campo.Etiqueta}' no tiene un valor válido."); continue; }
            d.Valores.Add(new DocumentoValor { CampoId = campo.Id, Valor = normalizado });
        }
        if (errores.Count > 0) throw new ValidacionException(string.Join(" ", errores));
    }

    private static (bool ok, string valor) Normalizar(CampoTipoDocumento c, string v)
    {
        switch (c.TipoDato)
        {
            case TipoDatoCampo.Numero:
                return decimal.TryParse(v.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
                    ? (true, n.ToString(CultureInfo.InvariantCulture)) : (false, v);
            case TipoDatoCampo.Fecha:
                return DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
                    ? (true, f.ToString("yyyy-MM-dd")) : (false, v);
            case TipoDatoCampo.SiNo:
                var l = v.ToLowerInvariant();
                if (l is "true" or "si" or "sí" or "1") return (true, "true");
                if (l is "false" or "no" or "0") return (true, "false");
                return (false, v);
            case TipoDatoCampo.Lista:
                var opciones = (c.Opciones ?? "").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var match = opciones.FirstOrDefault(o => string.Equals(o, v, StringComparison.OrdinalIgnoreCase));
                return match != null ? (true, match) : (false, v);
            default:
                return (true, v.Length > 1000 ? v[..1000] : v);
        }
    }

    private static string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
