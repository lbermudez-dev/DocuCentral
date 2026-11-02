using DocuCentral.Api.Data;
using DocuCentral.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocuCentral.Api.Services;

/// <summary>Estado de la última ejecución, para mostrarlo en la bandeja de entrada.</summary>
public class EstadoIngesta
{
    public DateTime? UltimaEjecucion { get; set; }
    public int Importados { get; set; }
    public int Errores { get; set; }
    public string? UltimoError { get; set; }
    private readonly SemaphoreSlim _senal = new(0, 1);

    public void SolicitarEjecucion()
    {
        if (_senal.CurrentCount == 0) _senal.Release();
    }

    public async Task EsperarAsync(TimeSpan intervalo, CancellationToken ct)
    {
        try { await _senal.WaitAsync(intervalo, ct); }
        catch (OperationCanceledException) { }
    }
}

/// <summary>
/// Revisa periódicamente la carpeta de entrada (ruta configurable, local o UNC) e importa los archivos
/// que encuentre al repositorio central. Cada archivo queda como documento "pendiente de indexar"
/// en la bandeja de entrada, donde un usuario captura sus metadatos.
///
///   {RutaEntrada}/archivo.pdf          → pendiente, sin área (lo ve quien tenga acceso a todas las áreas)
///   {RutaEntrada}/RRHH/archivo.pdf     → pendiente, asignado al área con código RRHH
///
/// Después de importar, el archivo se elimina de la entrada (o se mueve a RutaProcesados si está configurada).
/// Los archivos rechazados se mueven a RutaErrores junto con un .txt que explica el motivo.
/// Se usa sondeo y no FileSystemWatcher porque este último no es confiable en carpetas de red.
/// </summary>
public class CarpetaVigiladaService : BackgroundService
{
    private static readonly string[] Ignorados = { "thumbs.db", "desktop.ini", ".ds_store" };
    private static readonly TimeSpan EsperaEstabilidad = TimeSpan.FromSeconds(10);
    private const string Reintentar = "__REINTENTAR__";

    private readonly IServiceScopeFactory _scopes;
    private readonly ParametrosService _parametros;
    private readonly AlmacenamientoService _almacen;
    private readonly EstadoIngesta _estado;
    private readonly ILogger<CarpetaVigiladaService> _log;

    public CarpetaVigiladaService(IServiceScopeFactory scopes, ParametrosService parametros, AlmacenamientoService almacen,
        EstadoIngesta estado, ILogger<CarpetaVigiladaService> log)
    {
        _scopes = scopes;
        _parametros = parametros;
        _almacen = almacen;
        _estado = estado;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct); // dejar que termine el arranque y la creación de la BD
        while (!ct.IsCancellationRequested)
        {
            var cfg = await _parametros.ObtenerAsync();
            if (cfg.CarpetaVigiladaActiva)
            {
                try { await ProcesarAsync(cfg, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _estado.UltimoError = ex.Message;
                    _log.LogError(ex, "Error revisando la carpeta de entrada {Ruta}", cfg.RutaEntrada);
                }
            }
            await _estado.EsperarAsync(TimeSpan.FromSeconds(cfg.IntervaloSegundos), ct);
        }
    }

    private async Task ProcesarAsync(ConfiguracionAlmacenamiento cfg, CancellationToken ct)
    {
        Directory.CreateDirectory(cfg.RutaEntrada);
        Directory.CreateDirectory(cfg.RutaErrores);
        Directory.CreateDirectory(cfg.RutaRepositorio);

        var excluir = new[] { cfg.RutaErrores, cfg.RutaProcesados, cfg.RutaRepositorio }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .Where(p => !Path.GetFullPath(cfg.RutaEntrada).StartsWith(p, StringComparison.OrdinalIgnoreCase)) // la entrada puede estar dentro del repositorio
            .ToList();

        var archivos = Directory.EnumerateFiles(cfg.RutaEntrada, "*", SearchOption.AllDirectories)
            .Where(f => !excluir.Any(e => Path.GetFullPath(f).StartsWith(e, StringComparison.OrdinalIgnoreCase)))
            .Where(f =>
            {
                var n = Path.GetFileName(f).ToLowerInvariant();
                return !Ignorados.Contains(n) && !n.StartsWith("~$") && !n.StartsWith('.') && !n.EndsWith(".tmp") && !n.EndsWith(".error.txt");
            })
            .Take(500) // por ciclo, para no bloquear con lotes enormes
            .ToList();

        int importados = 0, errores = 0;
        if (archivos.Count > 0)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var areas = await db.Areas.AsNoTracking().Where(a => a.Activo).ToListAsync(ct);

            foreach (var ruta in archivos)
            {
                ct.ThrowIfCancellationRequested();
                var info = new FileInfo(ruta);
                if (!info.Exists || DateTime.UtcNow - info.LastWriteTimeUtc < EsperaEstabilidad) continue; // aún se está copiando
                if (!SePuedeAbrirExclusivo(ruta)) continue;

                try
                {
                    var resultado = await ImportarAsync(db, cfg, info, areas, ct);
                    if (resultado == Reintentar) continue;
                    if (resultado == null) importados++;
                    else { errores++; MoverAErrores(cfg, info, resultado); }
                }
                catch (Exception ex)
                {
                    errores++;
                    _log.LogError(ex, "No se pudo importar {Archivo}", ruta);
                    db.ChangeTracker.Clear();
                    try { MoverAErrores(cfg, info, ex.Message); } catch { /* se reintentará en el próximo ciclo */ }
                }
            }
        }

        _estado.UltimaEjecucion = DateTime.UtcNow;
        _estado.Importados = importados;
        _estado.Errores = errores;
        if (errores == 0) _estado.UltimoError = null;
        if (importados > 0 || errores > 0)
            _log.LogInformation("Carpeta de entrada: {Importados} importados, {Errores} con error", importados, errores);
    }

    /// <summary>Devuelve null si se importó, o el motivo del rechazo.</summary>
    private async Task<string?> ImportarAsync(AppDbContext db, ConfiguracionAlmacenamiento cfg, FileInfo info,
        List<Area> areas, CancellationToken ct)
    {
        try { await _almacen.ValidarAsync(info.Name, info.Length); }
        catch (ArchivoInvalidoException ex) { return ex.Message; }

        var hash = await AlmacenamientoService.CalcularHashAsync(info.FullName, ct);
        var duplicado = await db.Documentos.AsNoTracking()
            .Where(d => d.Estado != EstadoDocumento.Eliminado && d.Versiones.Any(v => v.HashSha256 == hash))
            .Select(d => new { d.Id, d.Titulo })
            .FirstOrDefaultAsync(ct);
        if (duplicado != null)
            return $"Archivo duplicado: ya existe en el repositorio como '{duplicado.Titulo}' (Id {duplicado.Id}).";

        // Área según la primera subcarpeta dentro de la entrada
        var relativa = Path.GetRelativePath(cfg.RutaEntrada, info.FullName);
        var partes = relativa.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var area = partes.Length > 1
            ? areas.FirstOrDefault(a => string.Equals(a.Codigo, partes[0], StringComparison.OrdinalIgnoreCase))
            : null;

        var d = new Documento
        {
            Titulo = Path.GetFileNameWithoutExtension(info.Name),
            Descripcion = partes.Length > 1 ? $"Importado desde: {relativa}" : null,
            AreaId = area?.Id,
            Estado = EstadoDocumento.PendienteIndexar,
            Origen = OrigenDocumento.CarpetaVigilada,
            CreadoEn = DateTime.UtcNow,
            VersionActual = 1
        };

        ArchivoGuardado g;
        await using (var s = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            g = await _almacen.GuardarAsync(s, info.Name, d.Id, 1, d.CreadoEn, ct);

        if (g.HashSha256 != hash)
        {
            // Cambió mientras se copiaba: se descarta la copia y se reintenta en el próximo ciclo
            try { File.Delete(AlmacenamientoService.RutaSegura(cfg.RutaRepositorio, g.RutaRelativa)); } catch { }
            return Reintentar;
        }

        d.Versiones.Add(new DocumentoVersion
        {
            NumeroVersion = 1,
            NombreOriginal = info.Name,
            RutaRelativa = g.RutaRelativa,
            Extension = g.Extension,
            TipoMime = g.TipoMime,
            TamanoBytes = g.TamanoBytes,
            HashSha256 = g.HashSha256,
            CreadoEn = DateTime.UtcNow
        });
        db.Documentos.Add(d);
        db.Auditoria.Add(new RegistroAuditoria
        {
            NombreUsuario = "sistema",
            Accion = AccionesAuditoria.Importar,
            Entidad = "Documento",
            EntidadId = d.Id.ToString(),
            Detalle = $"Carpeta de entrada: {relativa}"
        });
        await db.SaveChangesAsync(ct);

        // Solo después de confirmar en la BD se retira el archivo de la entrada
        if (!string.IsNullOrWhiteSpace(cfg.RutaProcesados))
        {
            var destino = DestinoLibre(Path.Combine(cfg.RutaProcesados!, DateTime.Now.ToString("yyyy-MM-dd"), relativa));
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            File.Move(info.FullName, destino);
        }
        else
        {
            File.Delete(info.FullName);
        }
        return null;
    }

    private static void MoverAErrores(ConfiguracionAlmacenamiento cfg, FileInfo info, string motivo)
    {
        if (!info.Exists) return;
        var relativa = Path.GetRelativePath(cfg.RutaEntrada, info.FullName);
        var destino = DestinoLibre(Path.Combine(cfg.RutaErrores, DateTime.Now.ToString("yyyy-MM-dd"), relativa));
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.Move(info.FullName, destino);
        File.WriteAllText(destino + ".error.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n{motivo}\r\n");
    }

    private static string DestinoLibre(string ruta)
    {
        if (!File.Exists(ruta)) return ruta;
        var dir = Path.GetDirectoryName(ruta)!;
        var nombre = Path.GetFileNameWithoutExtension(ruta);
        var ext = Path.GetExtension(ruta);
        for (var i = 1; ; i++)
        {
            var candidato = Path.Combine(dir, $"{nombre} ({i}){ext}");
            if (!File.Exists(candidato)) return candidato;
        }
    }

    private static bool SePuedeAbrirExclusivo(string ruta)
    {
        try
        {
            using var _ = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
