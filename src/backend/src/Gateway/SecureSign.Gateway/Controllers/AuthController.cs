using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    [EnableRateLimiting(LimitacionDeTasa.PoliticaToken)]
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

    /// <summary>
    /// Busca el servicio por nombre y compara el secreto contra TODOS los suyos
    /// (el vigente y los de una rotación en curso) sin cortocircuitar, para no
    /// filtrar por tiempo cuál coincidió. Un servicio desconocido también
    /// recorre una comparación, para que "no existe" y "secreto malo" no se
    /// distingan ni por respuesta ni por tiempo.
    /// </summary>
    private ServicioEmisorOpciones? AutenticarServicio(string? nombre, string? secretoRecibido)
    {
        if (string.IsNullOrEmpty(nombre) || string.IsNullOrEmpty(secretoRecibido)) return null;

        var servicio = jwtOpciones.Value.ServiciosEmisores.FirstOrDefault(s => s.Nombre == nombre);
        bool coincide = false;
        foreach (var aceptado in servicio?.Secretos.Where(s => !string.IsNullOrEmpty(s)) ?? [string.Empty])
            coincide |= CryptographicEquals(aceptado, secretoRecibido);
        return coincide ? servicio : null;
    }

    public sealed record EmitirTokenInternoRequest(Dictionary<string, string> Claims, string Audiencia, int MinutosExpiracion);

    /// <summary>
    /// Firma los claims que le pide un servicio interno autenticado (nombre +
    /// secreto propio) SOLO si cumplen la política de emisión de ese servicio
    /// (ver PoliticaEmisionInterna): los dos tipos de token legítimos, claims
    /// de una lista cerrada, <c>act</c> igual al servicio autenticado. No es
    /// una ruta pública del catálogo de integración — mismo tipo de atajo
    /// deliberado que /api/interno/identidad/*, ver README.md.
    /// </summary>
    [HttpPost("interno/emitir")]
    [EnableRateLimiting(LimitacionDeTasa.PoliticaInterno)]
    [AllowAnonymous]
    public IActionResult EmitirInterno([FromBody] EmitirTokenInternoRequest body)
    {
        var servicio = AutenticarServicio(Request.Headers["X-Internal-Service"], Request.Headers["X-Internal-Client-Secret"]);
        if (servicio is null)
            return Unauthorized(new { error = "SECRETO_INVALIDO", mensaje = "X-Internal-Service o X-Internal-Client-Secret ausente o incorrecto." });

        var motivo = PoliticaEmisionInterna.Evaluar(servicio, body.Claims, body.Audiencia, body.MinutosExpiracion, jwtOpciones.Value);
        if (motivo is not null)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "POLITICA_DE_EMISION", mensaje = motivo });

        var token = tokenService.EmitirConClaims(body.Claims, body.Audiencia, body.MinutosExpiracion);
        return Ok(new { access_token = token.AccessToken, expires_in = token.ExpiresIn });
    }
}
