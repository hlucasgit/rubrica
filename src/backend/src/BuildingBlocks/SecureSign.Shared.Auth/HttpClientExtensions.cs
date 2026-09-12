using Microsoft.Extensions.DependencyInjection;

namespace SecureSign.Shared.Auth;

public static class HttpClientExtensions
{
    /// <summary>
    /// Registra un HttpClient tipado que, antes de cada llamada saliente,
    /// intercambia el token del llamador original por uno interno de
    /// alcance mínimo (ver TokenExchangeService y TokenExchangeHandler) —
    /// requiere haber llamado antes a AddSecureSignTokenExchange.
    /// </summary>
    /// <param name="servicioActor">Identificador de este servicio (p. ej. "securesign-signature-api"), registrado como claim `act` en el token interno emitido, para auditoría.</param>
    public static IHttpClientBuilder AddSecureSignInternalHttpClient<TClient, TImplementation>(
        this IServiceCollection services, Uri baseAddress, string servicioActor)
        where TClient : class
        where TImplementation : class, TClient
    {
        services.AddHttpContextAccessor();

        return services.AddHttpClient<TClient, TImplementation>(client => client.BaseAddress = baseAddress)
            .AddHttpMessageHandler(sp => new TokenExchangeHandler(
                sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
                sp.GetRequiredService<TokenExchangeService>(),
                servicioActor));
    }
}
