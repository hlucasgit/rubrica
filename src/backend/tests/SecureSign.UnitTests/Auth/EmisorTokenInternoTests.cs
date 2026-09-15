using System.Net;
using SecureSign.Shared.Auth;

namespace SecureSign.UnitTests.Auth;

/// <summary>
/// Prueba directa del bug real encontrado en la verificación en vivo de
/// RUNBOOK.md 12.21: el Gateway responde en snake_case (<c>access_token</c>,
/// misma convención OAuth2 que <c>POST /api/auth/token</c>), y sin mapeo
/// explícito System.Text.Json lo deserializaba en silencio como
/// <c>AccessToken = null</c> — produciendo un header "Authorization: Bearer"
/// vacío que el servicio de destino trataba como "sin token", con CERO
/// mensaje de error en ningún log. Este archivo prueba exactamente ese
/// camino, con la respuesta real que el Gateway produce (no una
/// aproximación).
/// </summary>
public sealed class EmisorTokenInternoTests
{
    private sealed class HandlerFijo(HttpStatusCode estado, string cuerpoJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(estado) { Content = new StringContent(cuerpoJson, System.Text.Encoding.UTF8, "application/json") });
    }

    private static EmisorTokenInterno Construir(HttpStatusCode estado, string cuerpoJson) =>
        new(new HttpClient(new HandlerFijo(estado, cuerpoJson)) { BaseAddress = new Uri("http://gateway-de-prueba") });

    [Fact]
    public async Task Respuesta_real_del_Gateway_en_snake_case_se_mapea_correctamente()
    {
        var emisor = Construir(HttpStatusCode.OK, """{"access_token":"el.jwt.real","expires_in":120}""");

        var resultado = await emisor.EmitirAsync(new Dictionary<string, string> { ["scope"] = "internal-service" }, "securesign-internal-services", 2);

        Assert.Equal("el.jwt.real", resultado.AccessToken);
        Assert.Equal(120, resultado.ExpiresIn);
    }

    [Fact]
    public async Task Respuesta_200_sin_access_token_utilizable_lanza_en_vez_de_producir_un_Bearer_vacio()
    {
        // Simula el bug real: 200 OK pero el cuerpo no trae lo que el
        // llamador espera (aquí, deliberadamente, un cuerpo vacío) — antes
        // de la corrección esto producía un AccessToken null que
        // TokenExchangeHandler convertía en un header "Bearer" vacío sin
        // ningún error visible.
        var emisor = Construir(HttpStatusCode.OK, """{}""");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            emisor.EmitirAsync(new Dictionary<string, string> { ["scope"] = "internal-service" }, "securesign-internal-services", 2));
    }

    [Fact]
    public async Task Respuesta_no_exitosa_lanza()
    {
        var emisor = Construir(HttpStatusCode.Unauthorized, """{"error":"SECRETO_INVALIDO"}""");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            emisor.EmitirAsync(new Dictionary<string, string> { ["scope"] = "internal-service" }, "securesign-internal-services", 2));
    }
}
