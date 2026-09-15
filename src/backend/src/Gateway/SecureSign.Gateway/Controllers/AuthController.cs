using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SecureSign.Shared.Auth;

namespace SecureSign.Gateway.Controllers;

/// <summary>
/// POST /api/auth/token — ver docs/05-integracion/manual-integracion-api.md
/// capítulo 3. Implementación de referencia de un endpoint OAuth2
/// client_credentials contra un catálogo de clientes demo (ClientesDemo en
/// appsettings). PRODUCCIÓN: sustituir por un Identity Provider acreditado
/// (ver JwtOptions).
///
/// También hospeda POST /api/auth/interno/emitir — ver RUNBOOK.md 12.21:
/// el Gateway es el único componente de la plataforma con la llave privada
/// (ver RsaKeyStore), así que cualquier otro servicio que necesite un JWT
/// firmado (intercambio de tokens interno, tickets del Firmador Local) se
/// lo pide aquí en vez de firmarlo él mismo.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(JwtTokenService tokenService, IOptions<ClientesDemoOptions> clientesDemo, IOptions<JwtOptions> jwtOpciones) : ControllerBase
{
    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    public IActionResult ObtenerToken([FromForm] string grant_type, [FromForm] string client_id, [FromForm] string client_secret, [FromForm] string? scope)
    {
        if (grant_type != "client_credentials")
            return BadRequest(new { error = "UNSUPPORTED_GRANT_TYPE", mensaje = "Solo se admite client_credentials." });

        var cliente = clientesDemo.Value.Clientes.FirstOrDefault(c => c.ClientId == client_id);
        if (cliente is null || !cliente.SecretosHash.Any(hash => Argon2idSecretHasher.Verificar(client_secret, hash)))
            return Unauthorized(new { error = "INVALID_CLIENT", mensaje = "client_id o client_secret inválidos." });

        var scopesSolicitados = (scope ?? cliente.Scopes).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var scopesPermitidos = cliente.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var scopesConcedidos = scopesSolicitados.Intersect(scopesPermitidos).ToList();

        var token = tokenService.Emitir(new SolicitudEmisionToken(
            cliente.TenantId, cliente.ClienteIntegradorId, UsuarioId: null, scopesConcedidos));

        return Ok(new
        {
            access_token = token.AccessToken,
            token_type = "Bearer",
            expires_in = token.ExpiresIn,
            scope = string.Join(' ', scopesConcedidos)
        });
    }

    private static bool CryptographicEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));

    public sealed record EmitirTokenInternoRequest(Dictionary<string, string> Claims, string Audiencia, int MinutosExpiracion);

    /// <summary>
    /// Firma cualquier conjunto de claims que le pase un servicio interno
    /// autenticado con el secreto compartido — nunca interpreta su
    /// significado (eso lo decide el llamador: TokenExchangeService para
    /// llamadas servicio-a-servicio, EmisorTicketFirmaLocal para tickets del
    /// Firmador Local). No es una ruta pública del catálogo de integración —
    /// mismo tipo de atajo deliberado que /api/interno/identidad/*, ver
    /// README.md.
    /// </summary>
    [HttpPost("interno/emitir")]
    [AllowAnonymous]
    public IActionResult EmitirInterno([FromBody] EmitirTokenInternoRequest body)
    {
        string? secretoRecibido = Request.Headers["X-Internal-Client-Secret"];
        if (string.IsNullOrEmpty(secretoRecibido) || !CryptographicEquals(jwtOpciones.Value.SecretoClienteInterno, secretoRecibido))
            return Unauthorized(new { error = "SECRETO_INVALIDO", mensaje = "X-Internal-Client-Secret ausente o incorrecto." });

        if (body.Claims.Count == 0 || string.IsNullOrWhiteSpace(body.Audiencia) || body.MinutosExpiracion <= 0)
            return BadRequest(new { error = "SOLICITUD_INVALIDA", mensaje = "Se requieren claims, audiencia y minutosExpiracion (> 0)." });

        var token = tokenService.EmitirConClaims(body.Claims, body.Audiencia, body.MinutosExpiracion);
        return Ok(new { access_token = token.AccessToken, expires_in = token.ExpiresIn });
    }
}
