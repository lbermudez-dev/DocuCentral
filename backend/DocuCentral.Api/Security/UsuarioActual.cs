using System.Security.Claims;
using DocuCentral.Api.Data;
using DocuCentral.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocuCentral.Api.Security;

public static class ClaimsApp
{
    public const string Permiso = "perm";
    public const string VersionSeguridad = "sv";
    public const string NombreCompleto = "fullname";
    public const string DebeCambiarPassword = "pwd_change";
}

/// <summary>Datos del usuario autenticado en la petición actual.</summary>
public class UsuarioActual
{
    private readonly IHttpContextAccessor _http;
    private readonly AppDbContext _db;
    private List<int>? _areasCache;
    private bool _areasCargadas;

    public UsuarioActual(IHttpContextAccessor http, AppDbContext db)
    {
        _http = http;
        _db = db;
    }

    private ClaimsPrincipal? Principal => _http.HttpContext?.User;

    public int? Id => int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string NombreUsuario => Principal?.FindFirstValue(ClaimTypes.Name) ?? "sistema";
    public string? Ip => _http.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool Tiene(string permiso) => Principal?.HasClaim(ClaimsApp.Permiso, permiso) ?? false;

    /// <summary>Áreas cuyos documentos puede ver el usuario. null = todas las áreas.</summary>
    public async Task<List<int>?> AreasVisiblesAsync()
    {
        if (_areasCargadas) return _areasCache;
        _areasCargadas = true;
        if (Tiene(Permisos.DocumentosTodasAreas) || Id == null)
        {
            _areasCache = Id == null ? new List<int>() : null;
            return _areasCache;
        }
        var u = await _db.Usuarios.AsNoTracking()
            .Where(x => x.Id == Id)
            .Select(x => new { x.AreaId, Extra = x.AreasAdicionales.Select(a => a.AreaId).ToList() })
            .FirstOrDefaultAsync();
        var lista = new List<int>();
        if (u?.AreaId != null) lista.Add(u.AreaId.Value);
        if (u != null) lista.AddRange(u.Extra);
        _areasCache = lista.Distinct().ToList();
        return _areasCache;
    }

    /// <summary>Puede ver un documento del área indicada. Los documentos sin área (bandeja de entrada)
    /// solo los ve quien tiene acceso a todas las áreas o permiso de indexar.</summary>
    public async Task<bool> PuedeVerAreaAsync(int? areaId)
    {
        var areas = await AreasVisiblesAsync();
        if (areas == null) return true;
        if (areaId == null) return Tiene(Permisos.BandejaIndexar);
        return areas.Contains(areaId.Value);
    }
}
