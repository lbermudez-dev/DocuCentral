namespace DocuCentral.Api.Domain;

// ───────────────────────────── Seguridad ─────────────────────────────

public enum TipoAutenticacion
{
    Local = 0,      // usuario y contraseña guardados en la BD (hash BCrypt)
    Directorio = 1  // Active Directory / LDAP
}

public class Area
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";      // ej. RRHH, CRED. También es el nombre de la subcarpeta en la carpeta de entrada
    public string Nombre { get; set; } = "";
    public bool Activo { get; set; } = true;
}

public class Usuario
{
    public int Id { get; set; }
    public string NombreUsuario { get; set; } = "";
    public string NombreCompleto { get; set; } = "";
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }
    public TipoAutenticacion TipoAutenticacion { get; set; } = TipoAutenticacion.Local;
    public bool Activo { get; set; } = true;
    public bool DebeCambiarPassword { get; set; }
    public int IntentosFallidos { get; set; }
    public DateTime? BloqueadoHasta { get; set; }
    public DateTime? UltimoAcceso { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;

    /// <summary>Área a la que pertenece el usuario. Siempre puede ver los documentos de su área.</summary>
    public int? AreaId { get; set; }
    public Area? Area { get; set; }

    /// <summary>Incrementa al cambiar contraseña, roles o estado; invalida los tokens emitidos antes.</summary>
    public int VersionSeguridad { get; set; } = 1;

    public List<UsuarioRol> Roles { get; set; } = new();
    public List<UsuarioAreaAcceso> AreasAdicionales { get; set; } = new();
}

public class Rol
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string? Descripcion { get; set; }
    public bool EsSistema { get; set; }  // los roles del sistema no se pueden eliminar
    public List<RolPermiso> Permisos { get; set; } = new();
}

public class RolPermiso
{
    public int RolId { get; set; }
    public Rol? Rol { get; set; }
    public string Permiso { get; set; } = ""; // código de Permisos.*
}

public class UsuarioRol
{
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public int RolId { get; set; }
    public Rol? Rol { get; set; }
}

/// <summary>Áreas extra (además de la propia) cuyos documentos puede ver el usuario.</summary>
public class UsuarioAreaAcceso
{
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public int AreaId { get; set; }
    public Area? Area { get; set; }
}

// ───────────────────────────── Tipos de documento ─────────────────────────────

public enum TipoDatoCampo
{
    Texto = 0,
    Numero = 1,
    Fecha = 2,
    Lista = 3,
    SiNo = 4
}

public class TipoDocumento
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string? Descripcion { get; set; }
    public bool Activo { get; set; } = true;
    public List<CampoTipoDocumento> Campos { get; set; } = new();
}

public class CampoTipoDocumento
{
    public int Id { get; set; }
    public int TipoDocumentoId { get; set; }
    public TipoDocumento? TipoDocumento { get; set; }
    public string Clave { get; set; } = "";      // ej. numero_contrato
    public string Etiqueta { get; set; } = "";   // ej. Número de contrato
    public TipoDatoCampo TipoDato { get; set; }
    public bool Obligatorio { get; set; }
    public string? Opciones { get; set; }         // para Lista: valores separados por "|"
    public int Orden { get; set; }
    public bool Activo { get; set; } = true;
}

// ───────────────────────────── Documentos ─────────────────────────────

public enum EstadoDocumento
{
    PendienteIndexar = 0, // llegó por la carpeta vigilada y aún no tiene metadatos
    Activo = 1,
    Eliminado = 2         // eliminación lógica: el archivo se conserva
}

public enum OrigenDocumento
{
    CargaWeb = 0,
    CarpetaVigilada = 1
}

public class Documento
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? TipoDocumentoId { get; set; }
    public TipoDocumento? TipoDocumento { get; set; }
    public int? AreaId { get; set; }
    public Area? Area { get; set; }

    public string Titulo { get; set; } = "";
    public string? Descripcion { get; set; }

    /// <summary>Identificador del titular/expediente: cédula, RUC, número de socio, número de expediente...
    /// Agrupa todos los documentos que pertenecen a una misma persona o caso.</summary>
    public string? Identificador { get; set; }
    public string? NombreTitular { get; set; }
    public string? Etiquetas { get; set; }       // separadas por coma
    public DateTime? FechaDocumento { get; set; } // fecha propia del documento (emisión, firma...)

    public EstadoDocumento Estado { get; set; } = EstadoDocumento.Activo;
    public OrigenDocumento Origen { get; set; } = OrigenDocumento.CargaWeb;
    public int VersionActual { get; set; } = 1;

    public int? CreadoPorId { get; set; }
    public Usuario? CreadoPor { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public int? ModificadoPorId { get; set; }
    public DateTime? ModificadoEn { get; set; }

    public List<DocumentoValor> Valores { get; set; } = new();
    public List<DocumentoVersion> Versiones { get; set; } = new();
}

/// <summary>Valor de un campo dinámico (definido por el tipo de documento).</summary>
public class DocumentoValor
{
    public long Id { get; set; }
    public Guid DocumentoId { get; set; }
    public Documento? Documento { get; set; }
    public int CampoId { get; set; }
    public CampoTipoDocumento? Campo { get; set; }
    public string? Valor { get; set; }
}

public class DocumentoVersion
{
    public long Id { get; set; }
    public Guid DocumentoId { get; set; }
    public Documento? Documento { get; set; }
    public int NumeroVersion { get; set; }
    public string NombreOriginal { get; set; } = "";
    /// <summary>Ruta relativa a la raíz del repositorio. Nunca se guarda la ruta absoluta,
    /// así la raíz se puede mover cambiando solo la configuración.</summary>
    public string RutaRelativa { get; set; } = "";
    public string Extension { get; set; } = "";
    public string TipoMime { get; set; } = "application/octet-stream";
    public long TamanoBytes { get; set; }
    public string HashSha256 { get; set; } = "";
    public string? Comentario { get; set; }
    public int? CreadoPorId { get; set; }
    public Usuario? CreadoPor { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}

// ───────────────────────────── Auditoría y configuración ─────────────────────────────

public class RegistroAuditoria
{
    public long Id { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public int? UsuarioId { get; set; }
    public string NombreUsuario { get; set; } = "";
    public string Accion { get; set; } = "";
    public string Entidad { get; set; } = "";
    public string? EntidadId { get; set; }
    public string? Detalle { get; set; }
    public string? DireccionIp { get; set; }
}

/// <summary>Parámetros editables desde la pantalla de administración (rutas, extensiones, etc.).</summary>
public class ParametroSistema
{
    public string Clave { get; set; } = "";
    public string? Valor { get; set; }
}
