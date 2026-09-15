using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace SecureSign.Shared.Auth;

/// <summary>
/// Resuelve la llave pública de firma del Gateway a partir de su JWKS
/// (<c>/.well-known/jwks.json</c>, ver OidcController) — implementación
/// PROPIA en vez del descubrimiento OIDC automático de
/// <c>JwtBearerOptions.Authority</c>: ese mecanismo devolvía "no security
/// keys were provided" sin ningún diagnóstico aprovechable (IDX10500,
/// ConfigurationManager interno de Microsoft.IdentityModel) — ver
/// RUNBOOK.md 12.21. Esta clase hace exactamente lo mismo (buscar la llave
/// por <c>kid</c>, cachear, refrescar) pero con cada paso visible y
/// logueado si algo falla.
///
/// Cachea las llaves 5 minutos — suficiente para no golpear al Gateway en
/// cada petición, corto para que una rotación real (ver RsaKeyStore) se
/// propague a todos los servicios sin necesidad de reiniciarlos.
/// </summary>
public sealed class JwksLlaveResolver(HttpClient http, ILogger<JwksLlaveResolver> logger)
{
    private sealed record JwkDto(string Kty, string Kid, string N, string E);
    private sealed record JwksDto(List<JwkDto> Keys);

    private static readonly TimeSpan DuracionCache = TimeSpan.FromMinutes(5);
    private IReadOnlyList<SecurityKey> _cache = [];
    private DateTimeOffset _cacheExpiraEn = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _candado = new(1, 1);

    public async Task<IReadOnlyList<SecurityKey>> ObtenerLlavesAsync(CancellationToken ct = default)
    {
        if (DateTimeOffset.UtcNow < _cacheExpiraEn) return _cache;

        await _candado.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.UtcNow < _cacheExpiraEn) return _cache; // otro hilo ya refrescó mientras esperábamos el candado

            var jwks = await http.GetFromJsonAsync<JwksDto>("/.well-known/jwks.json", ct)
                ?? throw new InvalidOperationException("El Gateway respondió sin cuerpo al pedir /.well-known/jwks.json.");

            _cache = jwks.Keys.Where(k => k.Kty == "RSA").Select(k => (SecurityKey)new RsaSecurityKey(new System.Security.Cryptography.RSAParameters
            {
                Modulus = Base64UrlEncoder.DecodeBytes(k.N),
                Exponent = Base64UrlEncoder.DecodeBytes(k.E),
            })
            { KeyId = k.Kid }).ToList();
            _cacheExpiraEn = DateTimeOffset.UtcNow.Add(DuracionCache);

            logger.LogInformation("JWKS del Gateway actualizado — {Cantidad} llave(s): {Kids}", _cache.Count, string.Join(", ", _cache.Select(k => k.KeyId)));
            return _cache;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo obtener el JWKS del Gateway.");
            return _cache; // lo que hubiera en caché (puede ser vacío) — nunca lanzar aquí: eso rechazaría CUALQUIER token, no solo el actual.
        }
        finally
        {
            _candado.Release();
        }
    }
}
