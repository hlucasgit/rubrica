using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SecureSign.Shared.Auth;

public sealed record SolicitudEmisionToken(Guid TenantId, Guid? ClienteIntegradorId, Guid? UsuarioId, IReadOnlyList<string> Scopes);
public sealed record TokenEmitido(string AccessToken, int ExpiresIn);

/// <summary>
/// Emisor de tokens usado exclusivamente por el Gateway (POST /api/auth/token).
/// Ver advertencia de JwtOptions: llave simétrica de desarrollo, reemplazar
/// por un IdP acreditado en producción.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> opciones)
{
    private readonly JwtOptions _opciones = opciones.Value;

    public TokenEmitido Emitir(SolicitudEmisionToken solicitud)
    {
        var llave = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_opciones.SigningKey));
        var credenciales = new SigningCredentials(llave, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimsSecureSign.TenantId, solicitud.TenantId.ToString()),
            new(ClaimsSecureSign.Scope, string.Join(' ', solicitud.Scopes))
        };
        if (solicitud.ClienteIntegradorId is { } clienteId)
            claims.Add(new Claim(ClaimsSecureSign.ClienteIntegradorId, clienteId.ToString()));
        if (solicitud.UsuarioId is { } usuarioId)
            claims.Add(new Claim(ClaimsSecureSign.UsuarioId, usuarioId.ToString()));

        var expira = DateTime.UtcNow.AddMinutes(_opciones.MinutosExpiracion);

        var token = new JwtSecurityToken(
            issuer: _opciones.Issuer,
            audience: _opciones.Audience,
            claims: claims,
            expires: expira,
            signingCredentials: credenciales);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        return new TokenEmitido(jwt, _opciones.MinutosExpiracion * 60);
    }
}
