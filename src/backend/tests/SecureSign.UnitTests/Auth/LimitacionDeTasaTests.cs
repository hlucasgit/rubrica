using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecureSign.Gateway;

namespace SecureSign.UnitTests.Auth;

/// <summary>
/// Limitación de tasa de los endpoints de credenciales del Gateway (RUNBOOK.md
/// 12.29) contra un servidor HTTP real en memoria: mismo middleware y misma
/// configuración que en producción, sobre dos rutas con las políticas reales.
/// </summary>
public sealed class LimitacionDeTasaTests
{
    private static async Task<IHost> Servidor(int tokenPorMinuto, int internoPorMinuto)
    {
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LimitacionDeTasa:TokenPorMinuto"] = tokenPorMinuto.ToString(),
            ["LimitacionDeTasa:InternoPorMinuto"] = internoPorMinuto.ToString(),
        }).Build();

        return await new HostBuilder().ConfigureWebHost(web => web
            .UseTestServer()
            .ConfigureServices(s => { s.AddRouting(); s.AddLimitacionDeTasaGateway(configuracion); })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseRateLimiter();
                app.UseEndpoints(e =>
                {
                    e.MapPost("/token", () => "ok").RequireRateLimiting(LimitacionDeTasa.PoliticaToken);
                    e.MapPost("/interno", () => "ok").RequireRateLimiting(LimitacionDeTasa.PoliticaInterno);
                });
            })).StartAsync();
    }

    [Fact]
    public async Task Pasado_el_limite_responde_429_con_Retry_After()
    {
        using var host = await Servidor(tokenPorMinuto: 3, internoPorMinuto: 100);
        var http = host.GetTestClient();

        for (int i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/token", null)).StatusCode);

        var rechazada = await http.PostAsync("/token", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
        Assert.True(rechazada.Headers.TryGetValues("Retry-After", out var valores));
        Assert.InRange(int.Parse(valores!.Single()), 1, 60);
    }

    [Fact]
    public async Task Agotar_el_limite_de_token_no_afecta_al_endpoint_interno()
    {
        using var host = await Servidor(tokenPorMinuto: 1, internoPorMinuto: 100);
        var http = host.GetTestClient();

        await http.PostAsync("/token", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await http.PostAsync("/token", null)).StatusCode);

        for (int i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/interno", null)).StatusCode);
    }

    [Fact]
    public async Task El_endpoint_interno_tambien_tiene_su_propio_tope()
    {
        using var host = await Servidor(tokenPorMinuto: 100, internoPorMinuto: 2);
        var http = host.GetTestClient();

        await http.PostAsync("/interno", null);
        await http.PostAsync("/interno", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await http.PostAsync("/interno", null)).StatusCode);
    }

    [Fact]
    public void Sin_configuracion_aplica_los_valores_por_defecto()
    {
        var porDefecto = new OpcionesLimitacionDeTasa();
        Assert.Equal(10, porDefecto.TokenPorMinuto);
        Assert.Equal(3000, porDefecto.InternoPorMinuto);
    }

    [Fact]
    public void Los_endpoints_de_credenciales_del_controlador_declaran_su_politica()
    {
        var tipo = typeof(SecureSign.Gateway.Controllers.AuthController);

        var token = tipo.GetMethod(nameof(SecureSign.Gateway.Controllers.AuthController.ObtenerToken))!;
        var interno = tipo.GetMethod(nameof(SecureSign.Gateway.Controllers.AuthController.EmitirInterno))!;

        Assert.Equal(LimitacionDeTasa.PoliticaToken, ((EnableRateLimitingAttribute)token.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false).Single()).PolicyName);
        Assert.Equal(LimitacionDeTasa.PoliticaInterno, ((EnableRateLimitingAttribute)interno.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false).Single()).PolicyName);
    }
}
