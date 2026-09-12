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
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated == true)
        {
            var resultado = tokenExchange.Exchange(principal, servicioActor);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resultado.AccessToken);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
