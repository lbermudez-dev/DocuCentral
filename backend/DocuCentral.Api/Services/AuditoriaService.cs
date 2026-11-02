using DocuCentral.Api.Data;
using DocuCentral.Api.Domain;
using DocuCentral.Api.Security;

namespace DocuCentral.Api.Services;

/// <summary>Bitácora de auditoría. Los registros se agregan al DbContext de la petición
/// y se guardan junto con el resto de cambios (o con GuardarAsync si no hay otros).</summary>
public class AuditoriaService
{
    private readonly AppDbContext _db;
    private readonly UsuarioActual _usuario;

    public AuditoriaService(AppDbContext db, UsuarioActual usuario)
    {
        _db = db;
        _usuario = usuario;
    }

    public void Registrar(string accion, string entidad, object? entidadId, string? detalle = null,
        int? usuarioId = null, string? nombreUsuario = null)
    {
        _db.Auditoria.Add(new RegistroAuditoria
        {
            Fecha = DateTime.UtcNow,
            UsuarioId = usuarioId ?? _usuario.Id,
            NombreUsuario = nombreUsuario ?? _usuario.NombreUsuario,
            Accion = accion,
            Entidad = entidad,
            EntidadId = entidadId?.ToString(),
            Detalle = detalle is { Length: > 2000 } ? detalle[..2000] : detalle,
            DireccionIp = _usuario.Ip
        });
    }

    public async Task RegistrarYGuardarAsync(string accion, string entidad, object? entidadId, string? detalle = null)
    {
        Registrar(accion, entidad, entidadId, detalle);
        await _db.SaveChangesAsync();
    }
}
