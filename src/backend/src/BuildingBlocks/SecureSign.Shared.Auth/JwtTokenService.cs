using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SecureSign.Shared.Auth;

public sealed record SolicitudEmisionToken(Guid TenantId, Guid? ClienteIntegradorId, Guid? UsuarioId, IReadOnlyList<string> Scopes);
public sealed record TokenEmitido(string AccessToken, int ExpiresIn);

/// <summary>
/// Emisor de tokens — usado por el Gateway para el token externo
/// (POST /api/auth/token) y para cualquier petición de firma interna
/// (POST /api/auth/interno/emitir, ver AuthController) — el ÚNICO lugar del
/// sistema que toca la llave privada (ver RsaKeyStore, RUNBOOK.md 12.21).
/// </summary>
public sealed class JwtTokenService(RsaKeyStore llaves, IOptions<JwtOptions> opciones)
{
    private readonly JwtOptions _opciones = opciones.Value;

    public TokenEmitido Emitir(SolicitudEmisionToken solicitud)
    {
        var claims = new Dictionary<string, string>
        {
            [ClaimsSecureSign.TenantId] = solicitud.TenantId.ToString(),
            [ClaimsSecureSign.Scope] = string.Join(' ', solicitud.Scopes),
        };
        if (solicitud.ClienteIntegradorId is { } clienteId)
            claims[ClaimsSecureSign.ClienteIntegradorId] = clienteId.ToString();
        if (solicitud.UsuarioId is { } usuarioId)
            claims[ClaimsSecureSign.UsuarioId] = usuarioId.ToString();

        return EmitirConClaims(claims, _opciones.Audience, _opciones.MinutosExpiracion);
    }

    /// <summary>
    /// Firma cualquier conjunto de claims planos con la llave activa del
    /// Gateway — usado tanto por <see cref="Emitir"/> (token externo) como
    /// por <c>POST /api/auth/interno/emitir</c> (intercambio de tokens y
    /// tickets del Firmador Local, ver TokenExchangeService/EmisorTicketFirmaLocal).
    /// El Gateway nunca interpreta el SIGNIFICADO de los claims que le piden
    /// firmar — eso lo decide cada llamador; el Gateway solo garantiza que,
    /// si los firma, es porque el llamador ya demostró (más arriba en la
    /// pila) que tenía derecho a pedirlo.
    /// </summary>
    public TokenEmitido EmitirConClaims(IReadOnlyDictionary<string, string> claimsPlanos, string audience, int minutosExpiracion)
    {
        var llaveActiva = llaves.LlaveDeFirmaActiva;
        var credenciales = new SigningCredentials(llaveActiva.ComoSecurityKey(), SecurityAlgorithms.RsaSha256);

        var claims = claimsPlanos.Select(kv => new Claim(kv.Key, kv.Value)).ToList();
        var expira = DateTime.UtcNow.AddMinutes(minutosExpiracion);

        var token = new JwtSecurityToken(
            issuer: _opciones.Issuer,
            audience: audience,
            claims: claims,
            expires: expira,
            signingCredentials: credenciales);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        return new TokenEmitido(jwt, minutosExpiracion * 60);
    }
}
