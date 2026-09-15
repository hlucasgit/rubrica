using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SecureSign.Shared.Auth;

public sealed record TokenExchangeResult(string AccessToken, int ExpiresIn);

/// <summary>
/// Cliente HTTP hacia <c>POST /api/auth/interno/emitir</c> del Gateway — el
/// único lugar del sistema que firma un JWT (ver RsaKeyStore,
/// JwtTokenService). Usado por <see cref="TokenExchangeService"/> (llamadas
/// servicio-a-servicio) y por <c>EmisorTicketFirmaLocal</c> (tickets del
/// Firmador Local): ambos ya sabían construir los claims que necesitaban,
/// solo dejaron de tener la llave para firmarlos ellos mismos — ver
/// RUNBOOK.md 12.21.
///
/// El header <c>X-Internal-Client-Secret</c> se agrega una sola vez, al
/// registrar el HttpClient tipado (ver AddSecureSignTokenExchange), no en
/// cada llamada — evita que cada llamador tenga que conocer el detalle de
/// cómo se autentica esta petición.
/// </summary>
public sealed class EmisorTokenInterno(HttpClient http)
{
    private sealed record EmitirTokenInternoRequest(IReadOnlyDictionary<string, string> Claims, string Audiencia, int MinutosExpiracion);

    // El Gateway responde en snake_case (access_token/expires_in, misma
    // convención OAuth2 que POST /api/auth/token) — sin este mapeo explícito,
    // System.Text.Json (incluso con JsonSerializerDefaults.Web) NO empareja
    // "access_token" con "AccessToken": el naming policy de camelCase por
    // defecto solo cambia mayúsculas/minúsculas, nunca inserta o quita
    // guiones bajos. Sin el atributo, AccessToken quedaba en null en
    // silencio — ver RUNBOOK.md 12.21, bug real encontrado en la
    // verificación en vivo.
    private sealed record EmitirTokenInternoResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    public async Task<TokenExchangeResult> EmitirAsync(
        IReadOnlyDictionary<string, string> claims, string audiencia, int minutosExpiracion, CancellationToken ct = default)
    {
        var respuesta = await http.PostAsJsonAsync(
            "/api/auth/interno/emitir",
            new EmitirTokenInternoRequest(claims, audiencia, minutosExpiracion),
            ct);
        respuesta.EnsureSuccessStatusCode();

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<EmitirTokenInternoResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("El Gateway respondió sin cuerpo al pedirle firmar un token interno.");

        // Fail closed: un AccessToken vacío nunca debe convertirse en un
        // header "Authorization: Bearer" vacío enviado en silencio — eso es
        // indistinguible de "no autenticado" para quien lo recibe, y así se
        // manifestó el bug real de mapeo JSON que esta comprobación
        // detectaría de inmediato (ver RUNBOOK.md 12.21).
        if (string.IsNullOrEmpty(cuerpo.AccessToken))
            throw new InvalidOperationException("El Gateway respondió 200 pero sin access_token utilizable.");

        return new TokenExchangeResult(cuerpo.AccessToken, cuerpo.ExpiresIn);
    }
}
