using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace DocuCentral.Api.Security;

/// <summary>Exige un permiso: [RequierePermiso(Permisos.DocumentosVer)]</summary>
public class RequierePermisoAttribute : AuthorizeAttribute
{
    public const string Prefijo = "PERM:";
    public RequierePermisoAttribute(string permiso) => Policy = Prefijo + permiso;
}

/// <summary>Crea al vuelo una política por permiso, para no registrar una política por cada código.</summary>
public class PermisoPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _defecto;

    public PermisoPolicyProvider(IOptions<AuthorizationOptions> opciones) =>
        _defecto = new DefaultAuthorizationPolicyProvider(opciones);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _defecto.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _defecto.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string nombre)
    {
        if (nombre.StartsWith(RequierePermisoAttribute.Prefijo, StringComparison.Ordinal))
        {
            var permiso = nombre[RequierePermisoAttribute.Prefijo.Length..];
            var politica = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(ClaimsApp.Permiso, permiso)
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(politica);
        }
        return _defecto.GetPolicyAsync(nombre);
    }
}
