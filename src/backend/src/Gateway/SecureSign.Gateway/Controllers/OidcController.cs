using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SecureSign.Shared.Auth;

namespace SecureSign.Gateway.Controllers;

/// <summary>
/// Documento de descubrimiento OIDC y JWKS del Gateway — ver RUNBOOK.md
/// 12.21. Cada servicio de la plataforma configura
/// <c>AddJwtBearer(o =&gt; o.Authority = &lt;esta URL base&gt;)</c>, y el propio
/// middleware de ASP.NET Core descubre estos dos documentos solo — ningún
/// servicio necesita conocer ni configurar la llave pública a mano.
///
/// Rutas ABSOLUTAS (<c>/.well-known/...</c>, no bajo <c>api/</c>): son las
/// que exige la convención OIDC — un cliente que descubre vía Authority
/// SIEMPRE las busca ahí, sin poder configurar otra ruta.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class OidcController(RsaKeyStore llaves, IOptions<JwtOptions> opciones) : ControllerBase
{
    [HttpGet("/.well-known/openid-configuration")]
    public IActionResult Discovery()
    {
        string base_ = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        return Ok(new
        {
            issuer = opciones.Value.Issuer,
            jwks_uri = $"{base_}/.well-known/jwks.json",
            token_endpoint = $"{base_}/api/auth/token",
            grant_types_supported = new[] { "client_credentials" },
            id_token_signing_alg_values_supported = new[] { "RS256" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_post" },
        });
    }

    /// <summary>
    /// Publica TODAS las llaves vigentes (ver RsaKeyStore.LlavesVigentes),
    /// no solo la activa — así un token firmado con una llave anterior (en
    /// período de gracia de rotación) sigue validando en todos los
    /// servicios sin que ninguno necesite reconfigurarse.
    /// </summary>
    [HttpGet("/.well-known/jwks.json")]
    public IActionResult Jwks()
    {
        var claves = llaves.LlavesVigentes.Select(l =>
        {
            var parametros = l.Rsa.ExportParameters(includePrivateParameters: false);
            return new
            {
                kty = "RSA",
                use = "sig",
                alg = "RS256",
                kid = l.Kid,
                n = Base64UrlEncoder.Encode(parametros.Modulus),
                e = Base64UrlEncoder.Encode(parametros.Exponent),
            };
        });

        return Ok(new { keys = claves });
    }
}
