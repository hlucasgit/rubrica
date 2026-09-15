using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace SecureSign.Shared.Auth;

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
/// Desde RUNBOOK.md 12.21: el intercambio ya NO se hace localmente firmando
/// con una llave compartida — este servicio le PIDE el token al Gateway
/// (único que tiene la llave privada, ver EmisorTokenInterno/RsaKeyStore),
/// exactamente como lo haría un STS real vía un endpoint token con
/// grant_type urn:ietf:params:oauth:grant-type:token-exchange. La firma de
/// Exchange()/EmitirTokenDeSistema() no cambió (solo pasaron a ser async) —
/// TokenExchangeHandler y cada AddSecureSignInternalHttpClient no necesitan
/// saber cómo se firma el token, solo que lo reciben.
/// </summary>
public sealed class TokenExchangeService(EmisorTokenInterno emisor, IOptions<JwtOptions> opciones)
{
    private readonly JwtOptions _opciones = opciones.Value;

    /// <param name="tokenOriginal">Identidad ya autenticada de la petición entrante (el token del llamador externo, validado por el middleware JwtBearer del servicio actual).</param>
    /// <param name="servicioActor">Identificador del servicio que realiza la llamada saliente (claim `act`, para trazabilidad de auditoría — ver docs/07-seguridad).</param>
    /// <param name="scopeInterno">Alcance del token emitido. Deliberadamente NO se copian las concesiones (`scope`) del cliente externo — el servicio de destino nunca ve qué se le concedió al integrador, solo que la llamada viene de un servicio interno autorizado.</param>
    public Task<TokenExchangeResult> Exchange(ClaimsPrincipal tokenOriginal, string servicioActor, string scopeInterno = "internal-service", CancellationToken ct = default)
    {
        var tenantId = tokenOriginal.FindFirstValue(ClaimsSecureSign.TenantId)
            ?? throw new InvalidOperationException("El token original no contiene tenant_id; no se puede intercambiar.");

        var claims = new Dictionary<string, string>
        {
            [ClaimsSecureSign.TenantId] = tenantId,
            [ClaimsSecureSign.Scope] = scopeInterno,
            ["act"] = servicioActor, // actor del intercambio (RFC 8693 §4.1) — quién llama, no en nombre de quién
        };

        var usuarioId = tokenOriginal.FindFirstValue(ClaimsSecureSign.UsuarioId);
        if (!string.IsNullOrEmpty(usuarioId))
            claims[ClaimsSecureSign.UsuarioId] = usuarioId;

        return emisor.EmitirAsync(claims, _opciones.InternalAudience, _opciones.MinutosExpiracionInterno, ct);
    }

    /// <summary>
    /// Pide un token interno SIN partir de un llamador autenticado — para
    /// llamadas servicio-a-servicio que se originan en un endpoint público
    /// (p. ej. SecureSign.Validator, deliberadamente <c>[AllowAnonymous]</c>:
    /// cualquier destinatario de un documento debe poder validarlo, ver
    /// RUNBOOK.md 12.12). <see cref="Exchange"/> exige un <c>tenant_id</c>
    /// del token original y por eso no sirve aquí — no hay tenant que
    /// preservar cuando quien llama no se autenticó. Mismo alcance, misma
    /// audiencia y vida corta que un intercambio normal; sin `tenant_id`.
    /// </summary>
    public Task<TokenExchangeResult> EmitirTokenDeSistema(string servicioActor, string scopeInterno = "internal-service", CancellationToken ct = default)
    {
        var claims = new Dictionary<string, string>
        {
            [ClaimsSecureSign.Scope] = scopeInterno,
            ["act"] = servicioActor,
        };

        return emisor.EmitirAsync(claims, _opciones.InternalAudience, _opciones.MinutosExpiracionInterno, ct);
    }
}
