using DocuCentral.Api.Domain;

namespace DocuCentral.Api.Dtos;

// ── Autenticación
public record LoginRequest(string Usuario, string Password);
public record LoginResponse(string Token, DateTime Expira, UsuarioSesionDto Usuario);
public record UsuarioSesionDto(int Id, string NombreUsuario, string NombreCompleto, string? Area,
    bool DebeCambiarPassword, TipoAutenticacion TipoAutenticacion, IEnumerable<string> Permisos);
public record CambiarPasswordRequest(string PasswordActual, string PasswordNueva);

// ── Catálogos
public record AreaDto(int Id, string Codigo, string Nombre, bool Activo);
public record AreaGuardarDto(string Codigo, string Nombre, bool Activo);

public record CampoDto(int Id, string Clave, string Etiqueta, TipoDatoCampo TipoDato, bool Obligatorio,
    string? Opciones, int Orden, bool Activo);
public record TipoDocumentoDto(int Id, string Nombre, string? Descripcion, bool Activo, List<CampoDto> Campos);
public record CampoGuardarDto(int? Id, string Clave, string Etiqueta, TipoDatoCampo TipoDato, bool Obligatorio,
    string? Opciones, int Orden, bool Activo);
public record TipoDocumentoGuardarDto(string Nombre, string? Descripcion, bool Activo, List<CampoGuardarDto> Campos);

// ── Usuarios y roles
public record UsuarioDto(int Id, string NombreUsuario, string NombreCompleto, string? Email,
    TipoAutenticacion TipoAutenticacion, bool Activo, bool Bloqueado, int? AreaId, string? Area,
    List<int> RolIds, List<int> AreasAdicionalesIds, DateTime? UltimoAcceso);
public record UsuarioGuardarDto(string NombreUsuario, string NombreCompleto, string? Email,
    TipoAutenticacion TipoAutenticacion, bool Activo, int? AreaId, List<int> RolIds,
    List<int> AreasAdicionalesIds, string? Password);
public record RestablecerPasswordDto(string PasswordNueva);

public record RolDto(int Id, string Nombre, string? Descripcion, bool EsSistema, List<string> Permisos, int Usuarios);
public record RolGuardarDto(string Nombre, string? Descripcion, List<string> Permisos);

// ── Documentos
public record ValorCampoDto(int CampoId, string Clave, string Etiqueta, TipoDatoCampo TipoDato, string? Valor);

public record VersionDto(long Id, int NumeroVersion, string NombreOriginal, string Extension, string TipoMime,
    long TamanoBytes, string HashSha256, string? Comentario, string? CreadoPor, DateTime CreadoEn);

public record DocumentoResumenDto(Guid Id, string Titulo, string? Identificador, string? NombreTitular,
    int? TipoDocumentoId, string? TipoDocumento, int? AreaId, string? Area, DateTime? FechaDocumento,
    DateTime CreadoEn, int VersionActual, string? Extension, long TamanoBytes, EstadoDocumento Estado,
    OrigenDocumento Origen);

public record DocumentoDetalleDto(Guid Id, string Titulo, string? Descripcion, string? Identificador,
    string? NombreTitular, string? Etiquetas, DateTime? FechaDocumento, int? TipoDocumentoId, string? TipoDocumento,
    int? AreaId, string? Area, EstadoDocumento Estado, OrigenDocumento Origen, int VersionActual,
    string? CreadoPor, DateTime CreadoEn, DateTime? ModificadoEn, List<ValorCampoDto> Valores, List<VersionDto> Versiones);

/// <summary>Metadatos para crear, indexar o editar un documento. Valores: clave del campo → valor.</summary>
public class DocumentoMetadatosDto
{
    public int TipoDocumentoId { get; set; }
    public int AreaId { get; set; }
    public string Titulo { get; set; } = "";
    public string? Descripcion { get; set; }
    public string? Identificador { get; set; }
    public string? NombreTitular { get; set; }
    public string? Etiquetas { get; set; }
    public DateTime? FechaDocumento { get; set; }
    public Dictionary<string, string?> Valores { get; set; } = new();
}

public record PaginaDto<T>(IEnumerable<T> Items, int Total, int Pagina, int TamanoPagina);

public record ExpedienteResumenDto(string Identificador, string? NombreTitular, int CantidadDocumentos,
    DateTime UltimaModificacion, List<string> Tipos);

public record ExpedienteDto(string Identificador, string? NombreTitular, List<DocumentoResumenDto> Documentos);

// ── Auditoría
public record AuditoriaDto(long Id, DateTime Fecha, string NombreUsuario, string Accion, string Entidad,
    string? EntidadId, string? Detalle, string? DireccionIp);

// ── Estado de la carpeta vigilada
public record EstadoIngestaDto(bool Activa, string RutaEntrada, DateTime? UltimaEjecucion, int ImportadosUltimaEjecucion,
    int ErroresUltimaEjecucion, string? UltimoError, int PendientesIndexar);
