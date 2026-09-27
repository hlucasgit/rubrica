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
/// Reenvío de cabeceras X-Forwarded-* del Gateway (RUNBOOK.md 12.36) contra un servidor HTTP en memoria con el
/// middleware real. La IP del par TCP se simula con un middleware previo, igual que lo haría Kestrel.
/// </summary>
public sealed class ReenvioDeCabecerasTests
{
    private const string IpProxy = "10.0.0.5";

    private static async Task<IHost> Servidor(string ipDelPar, Dictionary<string, string?> configuracion, int tokenPorMinuto = 1000)
    {
        configuracion["LimitacionDeTasa:TokenPorMinuto"] = tokenPorMinuto.ToString();
        return await new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(configuracion))
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices((contexto, s) =>
                {
                    s.AddRouting();
                    s.AddReenvioDeCabecerasGateway(contexto.Configuration);
                    s.AddLimitacionDeTasaGateway(contexto.Configuration);
                })
                .Configure(app =>
                {
                    app.Use((c, siguiente) => { c.Connection.RemoteIpAddress = IPAddress.Parse(ipDelPar); return siguiente(c); });
                    app.UseReenvioDeCabecerasGateway();
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseEndpoints(e =>
                    {
                        e.MapGet("/quien", (HttpContext c) => $"{c.Connection.RemoteIpAddress}|{c.Request.Scheme}");
                        e.MapPost("/token", () => "ok").RequireRateLimiting(LimitacionDeTasa.PoliticaToken);
                    });
                })).StartAsync();
    }

    private static Dictionary<string, string?> Habilitado(string? proxy = null, string? red = null, int? saltos = null)
    {
        var d = new Dictionary<string, string?> { ["ReenvioDeCabeceras:Habilitado"] = "true" };
        if (proxy is not null) d["ReenvioDeCabeceras:ProxiesConfiables:0"] = proxy;
        if (red is not null) d["ReenvioDeCabeceras:RedesConfiables:0"] = red;
        if (saltos is not null) d["ReenvioDeCabeceras:LimiteDeSaltos"] = saltos.ToString();
        return d;
    }

    private static async Task<string> Quien(IHost host, string? xff = null, string? proto = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, "/quien");
        if (xff is not null) peticion.Headers.Add("X-Forwarded-For", xff);
        if (proto is not null) peticion.Headers.Add("X-Forwarded-Proto", proto);
        return await (await host.GetTestClient().SendAsync(peticion)).Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Desactivado_por_defecto_ignora_X_Forwarded_For()
    {
        using var host = await Servidor(IpProxy, new Dictionary<string, string?>());
        Assert.Equal($"{IpProxy}|http", await Quien(host, xff: "203.0.113.9", proto: "https"));
    }

    [Fact]
    public async Task Activado_con_el_proxy_de_confianza_usa_la_IP_y_el_esquema_del_cliente_real()
    {
        using var host = await Servidor(IpProxy, Habilitado(proxy: IpProxy));
        Assert.Equal("203.0.113.9|https", await Quien(host, xff: "203.0.113.9", proto: "https"));
    }

    [Fact]
    public async Task Una_red_CIDR_de_confianza_tambien_sirve()
    {
        using var host = await Servidor(IpProxy, Habilitado(red: "10.0.0.0/8"));
        Assert.Equal("203.0.113.9|http", await Quien(host, xff: "203.0.113.9"));
    }

    [Fact]
    public async Task Un_par_que_NO_es_de_confianza_no_puede_falsificar_su_IP()
    {
        // Un cliente cualquiera (no el proxy) manda X-Forwarded-For para hacerse pasar por otra IP.
        using var host = await Servidor("198.51.100.77", Habilitado(proxy: IpProxy));
        Assert.Equal("198.51.100.77|http", await Quien(host, xff: "203.0.113.9", proto: "https"));
    }

    [Fact]
    public async Task Con_un_salto_se_toma_la_direccion_que_agrego_el_proxy_no_la_que_escribio_el_cliente()
    {
        // El cliente antepuso "1.2.3.4" (falsa); el proxy de confianza agregó al final la IP real que vio.
        using var host = await Servidor(IpProxy, Habilitado(proxy: IpProxy, saltos: 1));
        Assert.Equal("203.0.113.9|http", await Quien(host, xff: "1.2.3.4, 203.0.113.9"));
    }

    [Fact]
    public async Task La_limitacion_de_tasa_separa_a_los_clientes_detras_del_mismo_proxy()
    {
        using var host = await Servidor(IpProxy, Habilitado(proxy: IpProxy), tokenPorMinuto: 1);
        var http = host.GetTestClient();
        async Task<HttpStatusCode> Post(string ipCliente)
        {
            var p = new HttpRequestMessage(HttpMethod.Post, "/token");
            p.Headers.Add("X-Forwarded-For", ipCliente);
            return (await http.SendAsync(p)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await Post("203.0.113.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Post("203.0.113.1")); // el mismo cliente agotó su cupo
        Assert.Equal(HttpStatusCode.OK, await Post("203.0.113.2"));               // otro cliente, mismo proxy: cupo propio
    }

    [Fact]
    public async Task Sin_el_reenvio_todos_los_clientes_detras_del_proxy_comparten_un_solo_cupo()
    {
        // Control del test anterior: el problema que resuelve esta funcionalidad.
        using var host = await Servidor(IpProxy, new Dictionary<string, string?>(), tokenPorMinuto: 1);
        var http = host.GetTestClient();
        async Task<HttpStatusCode> Post(string ipCliente)
        {
            var p = new HttpRequestMessage(HttpMethod.Post, "/token");
            p.Headers.Add("X-Forwarded-For", ipCliente);
            return (await http.SendAsync(p)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await Post("203.0.113.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Post("203.0.113.2"));
    }

    [Theory]
    [InlineData(null, null)]                 // activado sin declarar en quién confiar
    [InlineData("no-es-una-ip", null)]
    [InlineData(null, "10.0.0.0")]           // CIDR sin longitud
    [InlineData(null, "10.0.0.0/33")]        // longitud fuera de rango
    [InlineData(null, "abc/8")]
    public async Task Configuracion_invalida_impide_arrancar(string? proxy, string? red)
    {
        await Assert.ThrowsAnyAsync<Exception>(() => Servidor(IpProxy, Habilitado(proxy, red)));
    }

    [Fact]
    public async Task Limite_de_saltos_menor_que_uno_impide_arrancar()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => Servidor(IpProxy, Habilitado(proxy: IpProxy, saltos: 0)));
    }
}
