using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DocuCentral.Api.Domain;
using Microsoft.IdentityModel.Tokens;

namespace DocuCentral.Api.Security;

public class JwtOpciones
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "DocuCentral";
    public string Audience { get; set; } = "DocuCentral";
    public int MinutosExpiracion { get; set; } = 480;
}

public class TokenService
{
    private readonly JwtOpciones _op;

    public TokenService(IConfiguration config)
    {
        _op = config.GetSection("Jwt").Get<JwtOpciones>() ?? new JwtOpciones();
    }

    public (string token, DateTime expira) Generar(Usuario u, IEnumerable<string> permisos)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.NombreUsuario),
            new(ClaimsApp.NombreCompleto, u.NombreCompleto),
            new(ClaimsApp.VersionSeguridad, u.VersionSeguridad.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (u.DebeCambiarPassword)
        {
            // Mientras deba cambiar la contraseña, el token no lleva permisos: solo sirve para /api/auth/*
            claims.Add(new Claim(ClaimsApp.DebeCambiarPassword, "1"));
        }
        else
        {
            claims.AddRange(permisos.Distinct().Select(p => new Claim(ClaimsApp.Permiso, p)));
        }

        var expira = DateTime.UtcNow.AddMinutes(_op.MinutosExpiracion);
        var cred = new SigningCredentials(ClaveFirma(_op.Key), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_op.Issuer, _op.Audience, claims, DateTime.UtcNow, expira, cred);
        // Sin mapeo de nombres cortos: los claims viajan con el mismo tipo con que se leen (MapInboundClaims = false)
        var handler = new JwtSecurityTokenHandler();
        handler.OutboundClaimTypeMap.Clear();
        return (handler.WriteToken(jwt), expira);
    }

    public static SymmetricSecurityKey ClaveFirma(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException(
                "Jwt:Key debe tener al menos 32 caracteres. Defínala en appsettings o en la variable de entorno Jwt__Key.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    }
}
