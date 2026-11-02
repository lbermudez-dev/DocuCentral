using DocuCentral.Api.Data;
using DocuCentral.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DocuCentral.Api.Services;

/// <summary>Parámetros de almacenamiento e ingesta. Los valores iniciales salen de appsettings
/// (sección "Almacenamiento") y un administrador puede cambiarlos desde la pantalla de configuración;
/// lo guardado en la BD tiene prioridad.</summary>
public record ConfiguracionAlmacenamiento(
    string RutaRepositorio,
    string RutaEntrada,
    string? RutaProcesados,
    string RutaErrores,
    IReadOnlyList<string> ExtensionesPermitidas,
    int TamanoMaximoMB,
    bool CarpetaVigiladaActiva,
    int IntervaloSegundos);

public static class ClavesParametro
{
    public const string RutaRepositorio = "Almacenamiento.RutaRepositorio";
    public const string RutaEntrada = "Almacenamiento.RutaEntrada";
    public const string RutaProcesados = "Almacenamiento.RutaProcesados";
    public const string RutaErrores = "Almacenamiento.RutaErrores";
    public const string ExtensionesPermitidas = "Almacenamiento.ExtensionesPermitidas";
    public const string TamanoMaximoMB = "Almacenamiento.TamanoMaximoMB";
    public const string CarpetaVigiladaActiva = "Almacenamiento.CarpetaVigiladaActiva";
    public const string IntervaloSegundos = "Almacenamiento.IntervaloSegundos";

    public static readonly string[] Todas =
    {
        RutaRepositorio, RutaEntrada, RutaProcesados, RutaErrores,
        ExtensionesPermitidas, TamanoMaximoMB, CarpetaVigiladaActiva, IntervaloSegundos
    };
}

public class ParametrosService
{
    private const string CacheKey = "parametros_almacenamiento";
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;

    public ParametrosService(IServiceScopeFactory scopes, IConfiguration config, IMemoryCache cache)
    {
        _scopes = scopes;
        _config = config;
        _cache = cache;
    }

    public async Task<ConfiguracionAlmacenamiento> ObtenerAsync()
    {
        if (_cache.TryGetValue(CacheKey, out ConfiguracionAlmacenamiento? c) && c != null) return c;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var guardados = await db.Parametros.AsNoTracking().ToDictionaryAsync(p => p.Clave, p => p.Valor);

        string Valor(string clave)
        {
            if (guardados.TryGetValue(clave, out var v) && !string.IsNullOrWhiteSpace(v)) return v!;
            return _config[clave.Replace('.', ':')] ?? "";
        }

        var raiz = Valor(ClavesParametro.RutaRepositorio);
        if (string.IsNullOrWhiteSpace(raiz)) raiz = Path.Combine(AppContext.BaseDirectory, "repositorio");
        var entrada = Valor(ClavesParametro.RutaEntrada);
        if (string.IsNullOrWhiteSpace(entrada)) entrada = Path.Combine(raiz, "_entrada");
        var errores = Valor(ClavesParametro.RutaErrores);
        if (string.IsNullOrWhiteSpace(errores)) errores = Path.Combine(raiz, "_errores");
        var procesados = Valor(ClavesParametro.RutaProcesados);

        var ext = Valor(ClavesParametro.ExtensionesPermitidas);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".pdf,.jpg,.jpeg,.png,.tif,.tiff,.gif,.bmp,.doc,.docx,.xls,.xlsx,.txt";

        c = new ConfiguracionAlmacenamiento(
            RutaRepositorio: raiz,
            RutaEntrada: entrada,
            RutaProcesados: string.IsNullOrWhiteSpace(procesados) ? null : procesados,
            RutaErrores: errores,
            ExtensionesPermitidas: ext.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()).Distinct().ToList(),
            TamanoMaximoMB: int.TryParse(Valor(ClavesParametro.TamanoMaximoMB), out var mb) && mb > 0 ? mb : 50,
            CarpetaVigiladaActiva: !bool.TryParse(Valor(ClavesParametro.CarpetaVigiladaActiva), out var act) || act,
            IntervaloSegundos: int.TryParse(Valor(ClavesParametro.IntervaloSegundos), out var s) && s >= 5 ? s : 30);

        _cache.Set(CacheKey, c, TimeSpan.FromMinutes(5));
        return c;
    }

    public async Task<Dictionary<string, string?>> ObtenerValoresAsync()
    {
        var c = await ObtenerAsync();
        return new Dictionary<string, string?>
        {
            [ClavesParametro.RutaRepositorio] = c.RutaRepositorio,
            [ClavesParametro.RutaEntrada] = c.RutaEntrada,
            [ClavesParametro.RutaProcesados] = c.RutaProcesados,
            [ClavesParametro.RutaErrores] = c.RutaErrores,
            [ClavesParametro.ExtensionesPermitidas] = string.Join(",", c.ExtensionesPermitidas),
            [ClavesParametro.TamanoMaximoMB] = c.TamanoMaximoMB.ToString(),
            [ClavesParametro.CarpetaVigiladaActiva] = c.CarpetaVigiladaActiva.ToString().ToLowerInvariant(),
            [ClavesParametro.IntervaloSegundos] = c.IntervaloSegundos.ToString(),
        };
    }

    public async Task GuardarAsync(IDictionary<string, string?> valores)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (clave, valor) in valores)
        {
            if (!ClavesParametro.Todas.Contains(clave)) continue;
            var p = await db.Parametros.FindAsync(clave);
            if (p == null) db.Parametros.Add(new ParametroSistema { Clave = clave, Valor = valor?.Trim() });
            else p.Valor = valor?.Trim();
        }
        await db.SaveChangesAsync();
        _cache.Remove(CacheKey);
    }
}
