using System.Text;
using Novell.Directory.Ldap;

namespace DocuCentral.Api.Security;

public class LdapOpciones
{
    public bool Habilitado { get; set; }
    public string Servidor { get; set; } = "";       // ej. dc01.institucion.local
    public int Puerto { get; set; } = 389;            // 636 con SSL
    public bool UsarSsl { get; set; }
    public string Dominio { get; set; } = "";         // ej. INSTITUCION.LOCAL (se usa usuario@dominio para autenticar)
    public string BaseDn { get; set; } = "";          // ej. DC=institucion,DC=local
    /// <summary>Si el usuario existe en AD pero no en el sistema, crearlo automáticamente.</summary>
    public bool AutoCrearUsuarios { get; set; }
    public string RolPorDefecto { get; set; } = "Consulta";
    public string? AreaPorDefecto { get; set; }      // código de área
    /// <summary>Opcional: grupo de AD obligatorio para entrar (DN completo).</summary>
    public string? GrupoRequerido { get; set; }
}

public record ResultadoLdap(bool Valido, string? NombreCompleto = null, string? Email = null, string? Error = null);

/// <summary>Valida credenciales contra Active Directory / LDAP con un bind del propio usuario.
/// La contraseña nunca se guarda en el sistema.</summary>
public class LdapService
{
    private readonly LdapOpciones _op;
    private readonly ILogger<LdapService> _log;

    public LdapService(IConfiguration config, ILogger<LdapService> log)
    {
        _op = config.GetSection("Ldap").Get<LdapOpciones>() ?? new LdapOpciones();
        _log = log;
    }

    public LdapOpciones Opciones => _op;

    public Task<ResultadoLdap> AutenticarAsync(string usuario, string password) =>
        Task.Run(() => Autenticar(usuario, password));

    private ResultadoLdap Autenticar(string usuario, string password)
    {
        if (!_op.Habilitado) return new ResultadoLdap(false, Error: "La autenticación por directorio no está habilitada.");
        if (string.IsNullOrWhiteSpace(password)) return new ResultadoLdap(false); // evita "bind anónimo" exitoso

        var cuenta = usuario.Contains('@') || usuario.Contains('\\') ? usuario : $"{usuario}@{_op.Dominio}";
        var sam = usuario.Split('\\').Last().Split('@').First();

        try
        {
            using var cn = new LdapConnection { SecureSocketLayer = _op.UsarSsl };
            cn.Connect(_op.Servidor, _op.Puerto);
            cn.Bind(cuenta, password);
            if (!cn.Bound) return new ResultadoLdap(false);

            string? nombre = null, email = null;
            var grupos = new List<string>();
            if (!string.IsNullOrWhiteSpace(_op.BaseDn))
            {
                var filtro = $"(&(objectClass=user)(sAMAccountName={Escapar(sam)}))";
                var resultados = cn.Search(_op.BaseDn, LdapConnection.ScopeSub, filtro,
                    new[] { "displayName", "mail", "memberOf" }, false);
                while (resultados.HasMore())
                {
                    var entrada = resultados.Next();
                    nombre = Atributo(entrada, "displayName")?.StringValue;
                    email = Atributo(entrada, "mail")?.StringValue;
                    var g = Atributo(entrada, "memberOf");
                    if (g != null) grupos.AddRange(g.StringValueArray);
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(_op.GrupoRequerido) &&
                !grupos.Any(g => string.Equals(g, _op.GrupoRequerido, StringComparison.OrdinalIgnoreCase)))
                return new ResultadoLdap(false, Error: "El usuario no pertenece al grupo autorizado.");

            return new ResultadoLdap(true, nombre, email);
        }
        catch (LdapException ex) when (ex.ResultCode == LdapException.InvalidCredentials)
        {
            return new ResultadoLdap(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error al conectar con el servidor LDAP {Servidor}", _op.Servidor);
            return new ResultadoLdap(false, Error: "No se pudo contactar el servidor de directorio.");
        }
    }

    private static LdapAttribute? Atributo(LdapEntry entrada, string nombre)
    {
        try { return entrada.GetAttribute(nombre); }
        catch (KeyNotFoundException) { return null; } // la librería lanza excepción si el atributo no existe
    }

    /// <summary>Escapa caracteres especiales de filtros LDAP (RFC 4515) para evitar inyección.</summary>
    private static string Escapar(string valor)
    {
        var sb = new StringBuilder();
        foreach (var c in valor)
        {
            sb.Append(c switch
            {
                '\\' => @"\5c",
                '*' => @"\2a",
                '(' => @"\28",
                ')' => @"\29",
                '\0' => @"\00",
                _ => c.ToString()
            });
        }
        return sb.ToString();
    }
}
