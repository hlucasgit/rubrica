using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace SecureSign.Shared.Auth;

/// <summary>
/// Intercambia el token del llamador original por uno interno de alcance
/// mínimo (ver TokenExchangeService) antes de cada llamada saliente
/// servicio-a-servicio. Sustituye a BearerForwardingHandler — ver ese
/// nombre en el historial si se busca la versión anterior (reenvío directo
/// del token externo, ya no usada).
/// </summary>
public sealed class TokenExchangeHandler(
    IHttpContextAccessor httpContextAccessor,
    TokenExchangeService tokenExchange,
    string servicioActor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;

        // Si quien llama a ESTE servicio se autenticó, se preserva su
        // tenant_id en el token interno (Exchange). Si no — p. ej. una
        // llamada interna disparada desde un endpoint deliberadamente
        // público como SecureSign.Validator (ver RUNBOOK.md 12.12/12.16) —
        // no hay tenant que preservar, pero la llamada interna SIGUE
        // necesitando credencial: antes simplemente no se ponía ningún
        // header Authorization, y el servicio de destino la rechazaba con
        // 401 en silencio (bug real encontrado al conectar SecureSign.Audit
        // a este mismo endpoint — ver RUNBOOK.md 12.16).
        var resultado = principal?.Identity?.IsAuthenticated == true
            ? await tokenExchange.Exchange(principal, servicioActor, ct: cancellationToken)
            : await tokenExchange.EmitirTokenDeSistema(servicioActor, ct: cancellationToken);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resultado.AccessToken);

        return await base.SendAsync(request, cancellationToken);
    }
}
