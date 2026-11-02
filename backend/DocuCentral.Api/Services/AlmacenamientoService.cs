using System.Security.Cryptography;
using Microsoft.AspNetCore.StaticFiles;

namespace DocuCentral.Api.Services;

public record ArchivoGuardado(string RutaRelativa, long TamanoBytes, string HashSha256, string Extension, string TipoMime);

public class ArchivoInvalidoException : Exception
{
    public ArchivoInvalidoException(string mensaje) : base(mensaje) { }
}

/// <summary>Guarda y lee los archivos del repositorio central.
/// Estructura: {RutaRepositorio}/{aaaa}/{mm}/{idDocumento}/v001.pdf
/// Los nombres en disco no dependen del nombre original (evita caracteres inválidos y colisiones);
/// el nombre original se guarda en la BD y se usa al descargar.</summary>
public class AlmacenamientoService
{
    private static readonly FileExtensionContentTypeProvider Mimes = new();
    private readonly ParametrosService _parametros;

    public AlmacenamientoService(ParametrosService parametros) => _parametros = parametros;

    public async Task ValidarAsync(string nombreArchivo, long tamano)
    {
        var cfg = await _parametros.ObtenerAsync();
        var ext = Path.GetExtension(nombreArchivo).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !cfg.ExtensionesPermitidas.Contains(ext))
            throw new ArchivoInvalidoException(
                $"Extensión '{ext}' no permitida. Permitidas: {string.Join(", ", cfg.ExtensionesPermitidas)}");
        if (tamano <= 0)
            throw new ArchivoInvalidoException("El archivo está vacío.");
        if (tamano > cfg.TamanoMaximoMB * 1024L * 1024L)
            throw new ArchivoInvalidoException($"El archivo supera el tamaño máximo de {cfg.TamanoMaximoMB} MB.");
    }

    public async Task<ArchivoGuardado> GuardarAsync(Stream origen, string nombreOriginal, Guid documentoId,
        int numeroVersion, DateTime fecha, CancellationToken ct = default)
    {
        var cfg = await _parametros.ObtenerAsync();
        var ext = Path.GetExtension(nombreOriginal).ToLowerInvariant();
        var relativa = Path.Combine(fecha.ToString("yyyy"), fecha.ToString("MM"), documentoId.ToString("N"),
            $"v{numeroVersion:000}{ext}");
        var destino = RutaSegura(cfg.RutaRepositorio, relativa);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

        // Se escribe a un temporal y luego se renombra: un corte a medias nunca deja un archivo "válido" incompleto
        var temporal = destino + ".tmp";
        long tamano;
        string hash;
        using (var sha = SHA256.Create())
        {
            await using (var salida = new FileStream(temporal, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            await using (var cripto = new CryptoStream(salida, sha, CryptoStreamMode.Write))
            {
                await origen.CopyToAsync(cripto, 81920, ct);
                await cripto.FlushFinalBlockAsync(ct);
                tamano = salida.Length;
            }
            hash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }
        File.Move(temporal, destino, overwrite: true);

        return new ArchivoGuardado(relativa.Replace('\\', '/'), tamano, hash, ext, TipoMime(nombreOriginal));
    }

    public async Task<FileStream?> AbrirAsync(string rutaRelativa)
    {
        var cfg = await _parametros.ObtenerAsync();
        var ruta = RutaSegura(cfg.RutaRepositorio, rutaRelativa);
        if (!File.Exists(ruta)) return null;
        return new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
    }

    public static string TipoMime(string nombre) =>
        Mimes.TryGetContentType(nombre, out var m) ? m : "application/octet-stream";

    public static async Task<string> CalcularHashAsync(string ruta, CancellationToken ct = default)
    {
        await using var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var sha = SHA256.Create();
        var h = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(h).ToLowerInvariant();
    }

    /// <summary>Combina raíz + relativa y verifica que el resultado quede dentro de la raíz (evita "..\..\").</summary>
    public static string RutaSegura(string raiz, string relativa)
    {
        var raizCompleta = Path.GetFullPath(raiz);
        if (!raizCompleta.EndsWith(Path.DirectorySeparatorChar)) raizCompleta += Path.DirectorySeparatorChar;
        var relNormalizada = relativa.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var completa = Path.GetFullPath(Path.Combine(raizCompleta, relNormalizada));
        if (!completa.StartsWith(raizCompleta, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Ruta fuera del repositorio.");
        return completa;
    }
}
