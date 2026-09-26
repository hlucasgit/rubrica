using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecureSign.Gateway;

namespace SecureSign.UnitTests.Auth;

/// <summary>Cabeceras de seguridad y CORS del Gateway (RUNBOOK.md 12.31) sobre un servidor HTTP real en memoria.</summary>
public sealed class SeguridadHttpTests
{
    private const string OrigenLegitimo = "https://firma.securesign.pe";
    private const string OrigenAjeno = "https://sitio-malicioso.evil";

    private static async Task<IHost> Servidor(string entorno, params string[] origenes)
    {
        var datos = new Dictionary<string, string?>();
        for (int i = 0; i < origenes.Length; i++) datos[$"Cors:OrigenesPermitidos:{i}"] = origenes[i];
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(datos).Build();

        return await new HostBuilder()
            .UseEnvironment(entorno)
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices((contexto, s) =>
                {
                    s.AddRouting();
                    s.AddCorsGateway(configuracion, contexto.HostingEnvironment);
                })
                .Configure(app =>
                {
                    app.UseCabecerasDeSeguridad();
                    app.UseRouting();
                    app.UseCors(SeguridadHttp.PoliticaCors);
                    app.UseEndpoints(e =>
                    {
                        e.MapGet("/json", () => Results.Json(new { ok = true }));
                        e.MapGet("/con-cache", (HttpContext c) => { c.Response.Headers.CacheControl = "private, max-age=60"; return "x"; });
                        e.MapGet("/swagger/index.html", () => Results.Content("<html></html>", "text/html"));
                    });
                })).StartAsync();
    }

    private static async Task<HttpResponseMessage> Get(IHost host, string ruta, string? origen = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (origen is not null) peticion.Headers.Add("Origin", origen);
        return await host.GetTestClient().SendAsync(peticion);
    }

    private static string? Cabecera(HttpResponseMessage r, string nombre) =>
        r.Headers.TryGetValues(nombre, out var v) ? string.Join(",", v) : null;

    // --- Cabeceras de seguridad ---

    [Fact]
    public async Task Toda_respuesta_lleva_las_cabeceras_de_seguridad()
    {
        using var host = await Servidor("Production");
        var r = await Get(host, "/json");

        Assert.Equal("nosniff", Cabecera(r, "X-Content-Type-Options"));
        Assert.Equal("DENY", Cabecera(r, "X-Frame-Options"));
        Assert.Equal("no-referrer", Cabecera(r, "Referrer-Policy"));
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", Cabecera(r, "Content-Security-Policy"));
    }

    [Fact]
    public async Task Sin_Cache_Control_propio_la_respuesta_es_no_store()
    {
        using var host = await Servidor("Production");
        var r = await Get(host, "/json");

        Assert.Equal("no-store", Cabecera(r, "Cache-Control"));
        Assert.Equal("no-cache", Cabecera(r, "Pragma"));
    }

    [Fact]
    public async Task Un_Cache_Control_propio_de_la_respuesta_no_se_pisa()
    {
        using var host = await Servidor("Production");
        var r = await Get(host, "/con-cache");

        var cache = Cabecera(r, "Cache-Control");
        Assert.Contains("max-age=60", cache);
        Assert.DoesNotContain("no-store", cache);
        Assert.Null(Cabecera(r, "Pragma"));
    }

    [Fact]
    public async Task Swagger_no_recibe_el_CSP_restrictivo_pero_si_el_resto()
    {
        using var host = await Servidor("Development");
        var r = await Get(host, "/swagger/index.html");

        Assert.Null(Cabecera(r, "Content-Security-Policy"));
        Assert.Equal("DENY", Cabecera(r, "X-Frame-Options"));
    }

    // --- CORS ---

    [Fact]
    public async Task En_desarrollo_cualquier_origen_es_aceptado()
    {
        using var host = await Servidor("Development");
        var r = await Get(host, "/json", OrigenAjeno);

        Assert.Equal("*", Cabecera(r, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task En_produccion_sin_origenes_configurados_no_se_emite_ninguna_cabecera_CORS()
    {
        using var host = await Servidor("Production");
        var r = await Get(host, "/json", OrigenLegitimo);

        Assert.Null(Cabecera(r, "Access-Control-Allow-Origin"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); // el servidor responde; es el navegador quien bloquea
    }

    [Fact]
    public async Task En_produccion_solo_el_origen_configurado_recibe_CORS()
    {
        using var host = await Servidor("Production", OrigenLegitimo);

        Assert.Equal(OrigenLegitimo, Cabecera(await Get(host, "/json", OrigenLegitimo), "Access-Control-Allow-Origin"));
        Assert.Null(Cabecera(await Get(host, "/json", OrigenAjeno), "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task En_produccion_las_cabeceras_expuestas_del_documento_siguen_disponibles()
    {
        using var host = await Servidor("Production", OrigenLegitimo);
        var r = await Get(host, "/json", OrigenLegitimo);

        var expuestas = Cabecera(r, "Access-Control-Expose-Headers");
        Assert.NotNull(expuestas);
        Assert.Contains("X-Hash-Documento", expuestas);
        Assert.Contains("X-Evidencia-Url", expuestas);
    }
}
