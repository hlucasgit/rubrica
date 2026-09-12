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
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(JwtTokenService tokenService, IOptions<ClientesDemoOptions> clientesDemo) : ControllerBase
{
    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    public IActionResult ObtenerToken([FromForm] string grant_type, [FromForm] string client_id, [FromForm] string client_secret, [FromForm] string? scope)
    {
        if (grant_type != "client_credentials")
            return BadRequest(new { error = "UNSUPPORTED_GRANT_TYPE", mensaje = "Solo se admite client_credentials." });

        var cliente = clientesDemo.Value.Clientes.FirstOrDefault(c => c.ClientId == client_id);
        if (cliente is null || !CryptographicEquals(cliente.ClientSecret, client_secret))
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
}
