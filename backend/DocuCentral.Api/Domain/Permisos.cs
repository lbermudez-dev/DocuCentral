namespace DocuCentral.Api.Domain;

/// <summary>Catálogo de permisos por acción. Los roles agrupan estos códigos.</summary>
public static class Permisos
{
    public const string DocumentosVer = "documentos.ver";
    public const string DocumentosCargar = "documentos.cargar";
    public const string DocumentosEditar = "documentos.editar";
    public const string DocumentosDescargar = "documentos.descargar";
    public const string DocumentosEliminar = "documentos.eliminar";
    /// <summary>Ve documentos de todas las áreas, no solo de la suya y las asignadas.</summary>
    public const string DocumentosTodasAreas = "documentos.todas_areas";
    public const string BandejaIndexar = "bandeja.indexar";

    public const string TiposAdministrar = "tipos.administrar";
    public const string AreasAdministrar = "areas.administrar";
    public const string UsuariosAdministrar = "usuarios.administrar";
    public const string RolesAdministrar = "roles.administrar";
    public const string ConfiguracionAdministrar = "configuracion.administrar";
    public const string AuditoriaVer = "auditoria.ver";

    public record DefinicionPermiso(string Codigo, string Descripcion, string Grupo);

    public static readonly IReadOnlyList<DefinicionPermiso> Todos = new List<DefinicionPermiso>
    {
        new(DocumentosVer, "Buscar y ver documentos", "Documentos"),
        new(DocumentosCargar, "Cargar documentos nuevos", "Documentos"),
        new(DocumentosEditar, "Editar metadatos y subir nuevas versiones", "Documentos"),
        new(DocumentosDescargar, "Descargar archivos y expedientes", "Documentos"),
        new(DocumentosEliminar, "Eliminar documentos", "Documentos"),
        new(DocumentosTodasAreas, "Acceso a documentos de todas las áreas", "Documentos"),
        new(BandejaIndexar, "Indexar documentos de la bandeja de entrada", "Documentos"),
        new(TiposAdministrar, "Administrar tipos de documento y campos", "Administración"),
        new(AreasAdministrar, "Administrar áreas", "Administración"),
        new(UsuariosAdministrar, "Administrar usuarios", "Administración"),
        new(RolesAdministrar, "Administrar roles y permisos", "Administración"),
        new(ConfiguracionAdministrar, "Configurar rutas y parámetros", "Administración"),
        new(AuditoriaVer, "Consultar la bitácora de auditoría", "Administración"),
    };

    public static bool Existe(string codigo) => Todos.Any(p => p.Codigo == codigo);
}

public static class AccionesAuditoria
{
    public const string Login = "LOGIN";
    public const string LoginFallido = "LOGIN_FALLIDO";
    public const string UsuarioBloqueado = "USUARIO_BLOQUEADO";
    public const string CambioPassword = "CAMBIO_PASSWORD";
    public const string Ver = "VER";
    public const string Descargar = "DESCARGAR";
    public const string DescargarExpediente = "DESCARGAR_EXPEDIENTE";
    public const string Cargar = "CARGAR";
    public const string Importar = "IMPORTAR";
    public const string Indexar = "INDEXAR";
    public const string Editar = "EDITAR";
    public const string NuevaVersion = "NUEVA_VERSION";
    public const string Eliminar = "ELIMINAR";
    public const string Crear = "CREAR";
    public const string Modificar = "MODIFICAR";
}
