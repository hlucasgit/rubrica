using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SecureSign.Shared.Auth;

public sealed record TokenExchangeResult(string AccessToken, int ExpiresIn);

/// <summary>
/// Implementación de un intercambio de tokens (RFC 8693 — OAuth 2.0 Token
/// Exchange) para llamadas servicio-a-servicio: en lugar de reenviar el
/// token del cliente externo tal cual (lo que hacía BearerForwardingHandler
/// antes de esto — ver docs/07-seguridad/modelo-seguridad.md sección 1),
/// cada servicio que necesita llamar a otro cambia el token que recibió por
/// uno nuevo, propio, de alcance mínimo ("internal-service", no las
/// concesiones de negocio del cliente original) y vida corta, con una
/// audiencia distinta (`securesign-internal-services`) que identifica
/// claramente que ese token nunca debería usarse para llamar directamente a
/// una ruta pública del Gateway.
///
/// SIMPLIFICACIÓN frente a un STS (Security Token Service) real: no hay una
/// llamada HTTP a un endpoint /token separado — como todos los servicios ya
/// comparten la misma llave simétrica (ver JwtOptions), el intercambio se
/// hace localmente. Un IdP de producción (Keycloak, Duende) expondría esto
/// como un endpoint token real con grant_type
/// urn:ietf:params:oauth:grant-type:token-exchange.
/// </summary>
public sealed class TokenExchangeService(IOptions<JwtOptions> opciones)
{
    private readonly JwtOptions _opciones = opciones.Value;

    /// <param name="tokenOriginal">Identidad ya autenticada de la petición entrante (el token del llamador externo, validado por el middleware JwtBearer del servicio actual).</param>
    /// <param name="servicioActor">Identificador del servicio que realiza la llamada saliente (claim `act`, para trazabilidad de auditoría — ver docs/07-seguridad).</param>
    /// <param name="scopeInterno">Alcance del token emitido. Deliberadamente NO se copian las concesiones (`scope`) del cliente externo — el servicio de destino nunca ve qué se le concedió al integrador, solo que la llamada viene de un servicio interno autorizado.</param>
    public TokenExchangeResult Exchange(ClaimsPrincipal tokenOriginal, string servicioActor, string scopeInterno = "internal-service")
    {
        var tenantId = tokenOriginal.FindFirstValue(ClaimsSecureSign.TenantId)
            ?? throw new InvalidOperationException("El token original no contiene tenant_id; no se puede intercambiar.");

        var llave = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_opciones.SigningKey));
        var credenciales = new SigningCredentials(llave, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimsSecureSign.TenantId, tenantId),
            new(ClaimsSecureSign.Scope, scopeInterno),
            new("act", servicioActor) // actor del intercambio (RFC 8693 §4.1) — quién llama, no en nombre de quién
        };

        var usuarioId = tokenOriginal.FindFirstValue(ClaimsSecureSign.UsuarioId);
        if (!string.IsNullOrEmpty(usuarioId))
            claims.Add(new Claim(ClaimsSecureSign.UsuarioId, usuarioId));

        var expira = DateTime.UtcNow.AddMinutes(_opciones.MinutosExpiracionInterno);

        var token = new JwtSecurityToken(
            issuer: _opciones.Issuer,
            audience: _opciones.InternalAudience,
            claims: claims,
            expires: expira,
            signingCredentials: credenciales);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        return new TokenExchangeResult(jwt, _opciones.MinutosExpiracionInterno * 60);
    }

    /// <summary>
    /// Emite un token interno SIN partir de un llamador autenticado — para
    /// llamadas servicio-a-servicio que se originan en un endpoint público
    /// (p. ej. SecureSign.Validator, deliberadamente <c>[AllowAnonymous]</c>:
    /// cualquier destinatario de un documento debe poder validarlo, ver
    /// RUNBOOK.md 12.12). <see cref="Exchange"/> exige un <c>tenant_id</c>
    /// del token original y por eso no sirve aquí — no hay tenant que
    /// preservar cuando quien llama no se autenticó. Mismo alcance, misma
    /// audiencia y vida corta que un intercambio normal; sin `tenant_id`.
    /// </summary>
    public TokenExchangeResult EmitirTokenDeSistema(string servicioActor, string scopeInterno = "internal-service")
    {
        var llave = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_opciones.SigningKey));
        var credenciales = new SigningCredentials(llave, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimsSecureSign.Scope, scopeInterno),
            new("act", servicioActor)
        };

        var expira = DateTime.UtcNow.AddMinutes(_opciones.MinutosExpiracionInterno);

        var token = new JwtSecurityToken(
            issuer: _opciones.Issuer,
            audience: _opciones.InternalAudience,
            claims: claims,
            expires: expira,
            signingCredentials: credenciales);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        return new TokenExchangeResult(jwt, _opciones.MinutosExpiracionInterno * 60);
    }
}
